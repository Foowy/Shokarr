using Shokarr.Config;
using Shokarr.Models;

namespace Shokarr.Services;

/// <summary>Monitors and searches for specific missing episodes on a Sonarr series. Shared by the
/// dashboard's add-and-search/search endpoints and the native <see cref="Actions.SearchMissingEpisodesAction"/>.</summary>
public class SonarrSearchService(SonarrClient sonarrClient, ScanCacheStore cacheStore, NotificationService notificationService)
{
    /// <returns>Success with an optional caveat message (unmapped episodes skipped), or failure with a reason.</returns>
    public async Task<ArrActionResult<string?>> MonitorAndSearchAsync(SonarrSettings settings, int shokoSeriesId, int sonarrSeriesId, List<int> anidbEpisodeIds, SeriesMissingResult series, string? sonarrTitleSlug = null, CancellationToken ct = default)
    {
        var episodesResult = await sonarrClient.GetEpisodesAsync(settings, sonarrSeriesId, ct);
        if (!episodesResult.Success)
            return ArrActionResult<string?>.Fail(episodesResult.ErrorMessage!);

        var targetEpisodes = series.MissingEpisodes.Where(e => anidbEpisodeIds.Contains(e.AnidbEpisodeId)).ToList();
        var anySonarrAbsolute = episodesResult.Data!.Any(se => se.AbsoluteEpisodeNumber.HasValue);
        var sonarrEpisodeIds = new List<int>();
        var sonarrEpisodeIdByAnidbId = new Dictionary<int, int>();
        var unmappedIds = new List<int>();
        var unmappedTitles = new List<string>();
        foreach (var ep in targetEpisodes)
        {
            var match = SonarrEpisodeMatcher.Match(episodesResult.Data!, anySonarrAbsolute, ep);
            if (match is null)
            {
                unmappedIds.Add(ep.AnidbEpisodeId);
                unmappedTitles.Add(ep.Title);
            }
            else
            {
                sonarrEpisodeIds.Add(match.Id);
                sonarrEpisodeIdByAnidbId[ep.AnidbEpisodeId] = match.Id;
            }
        }

        if (sonarrEpisodeIds.Count == 0)
            return ArrActionResult<string?>.Fail($"No episodes could be mapped to Sonarr. Unmapped: {string.Join(", ", unmappedTitles)}");

        var monitorResult = await sonarrClient.MonitorEpisodesAsync(settings, sonarrEpisodeIds, ct);
        if (!monitorResult.Success)
            return ArrActionResult<string?>.Fail(monitorResult.ErrorMessage!);

        var searchResult = await sonarrClient.TriggerEpisodeSearchAsync(settings, sonarrEpisodeIds, ct);
        if (!searchResult.Success)
            return ArrActionResult<string?>.Fail(searchResult.ErrorMessage!);

        var triggeredAt = DateTime.UtcNow;
        var pendingEntries = targetEpisodes.Where(e => !unmappedIds.Contains(e.AnidbEpisodeId)).Select(ep => new PendingSearch
        {
            ShokoSeriesId = shokoSeriesId,
            SeriesTitle = series.Title,
            AnidbEpisodeId = ep.AnidbEpisodeId,
            EpisodeTitle = ep.Title,
            SonarrSeriesId = sonarrSeriesId,
            SonarrTitleSlug = sonarrTitleSlug,
            SonarrEpisodeId = sonarrEpisodeIdByAnidbId[ep.AnidbEpisodeId],
            TriggeredAtUtc = triggeredAt,
        }).ToList();
        foreach (var pending in pendingEntries)
            cacheStore.AddPendingSearch(pending);
        cacheStore.AddHistoryEntries(pendingEntries.Select(p => SearchHistoryEntry.From(p, SearchHistoryOutcome.Triggered, triggeredAt)));

        var triggeredCount = targetEpisodes.Count - unmappedIds.Count;
        await notificationService.NotifyAsync(settings, $"Triggered Sonarr search for {triggeredCount} episode(s) of **{series.Title}**");

        var message = unmappedTitles.Count > 0 ? $"Search triggered. Unmapped episodes skipped: {string.Join(", ", unmappedTitles)}" : null;
        return ArrActionResult<string?>.Ok(message);
    }
}
