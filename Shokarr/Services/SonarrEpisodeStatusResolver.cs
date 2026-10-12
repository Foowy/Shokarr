using Shokarr.Config;
using Shokarr.Models;

namespace Shokarr.Services;

/// <summary>Marks missing episodes Sonarr already has ("downloaded") or is downloading ("downloading").</summary>
public class SonarrEpisodeStatusResolver(SonarrClient sonarrClient)
{
    /// <summary>Starts a status session. <paramref name="bulk"/> is true for a full scan, which fetches Sonarr's series list once instead of once per series; leave it false for a single series.</summary>
    /// <param name="settings">The Sonarr settings.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="bulk">Whether to fetch the whole series list up front.</param>
    public Task<Session> BeginAsync(SonarrSettings settings, CancellationToken ct, bool bulk = false) =>
        Task.FromResult(new Session(sonarrClient, settings, ct, bulk));

    /// <summary>State for one scan: the queue is fetched once, and the first failed Sonarr call disables all further calls. Safe to call concurrently.</summary>
    public class Session(SonarrClient sonarrClient, SonarrSettings settings, CancellationToken ct, bool bulk = false)
    {
        // Each failed call can burn the full 30 s HttpClient timeout; without this a scan of
        // 100+ series against a dead Sonarr would hang for over an hour.
        private volatile bool _unreachable = string.IsNullOrWhiteSpace(settings.BaseUrl);
        private readonly Lazy<Task<ArrActionResult<HashSet<int>>>> _queue = new(() => sonarrClient.GetQueuedEpisodeIdsAsync(settings, ct));

        private readonly Lazy<Task<ArrActionResult<List<SonarrSeriesResource>>>> _allSeries = new(() => sonarrClient.GetAllSeriesAsync(settings, ct));

        private async Task<ArrActionResult<SonarrSeriesResource?>> FindSeriesAsync(int tvdbId)
        {
            if (bulk)
            {
                var all = await _allSeries.Value.ConfigureAwait(false);
                return all.Success ? ArrActionResult<SonarrSeriesResource?>.Ok(all.Data!.Find(s => s.TvdbId == tvdbId)) : ArrActionResult<SonarrSeriesResource?>.Fail(all.ErrorMessage!);
            }

            var existing = await sonarrClient.GetExistingSeriesByTvdbIdAsync(settings, tvdbId, ct).ConfigureAwait(false);
            return existing.Success ? ArrActionResult<SonarrSeriesResource?>.Ok(existing.Data!.FirstOrDefault()) : ArrActionResult<SonarrSeriesResource?>.Fail(existing.ErrorMessage!);
        }

        public async Task ApplyAsync(SeriesMissingResult series)
        {
            if (_unreachable || series.TvdbId is not { } tvdbId || series.MissingEpisodes.Count == 0)
                return;

            var existing = await FindSeriesAsync(tvdbId).ConfigureAwait(false);
            if (!existing.Success)
            {
                _unreachable = true;
                return;
            }

            if (existing.Data is null)
                return;

            var episodes = await sonarrClient.GetEpisodesAsync(settings, existing.Data.Id, ct).ConfigureAwait(false);
            if (!episodes.Success)
            {
                _unreachable = true;
                return;
            }

            var queue = await _queue.Value.ConfigureAwait(false);
            if (!queue.Success)
            {
                _unreachable = true;
                return;
            }

            var anyAbsolute = episodes.Data!.Any(se => se.AbsoluteEpisodeNumber.HasValue);
            foreach (var ep in series.MissingEpisodes)
            {
                var match = SonarrEpisodeMatcher.Match(episodes.Data!, anyAbsolute, ep);
                ep.SonarrState = match is null ? "none" : match.HasFile ? "downloaded" : queue.Data!.Contains(match.Id) ? "downloading" : "none";
            }
        }
    }
}
