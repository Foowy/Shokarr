using Shokarr.Config;
using Shokarr.Models;

namespace Shokarr.Services;

/// <summary>Monitors and searches for specific missing episodes on a Sonarr series. Shared by the
/// dashboard's add-and-search/search endpoints and the native <see cref="Actions.SearchMissingEpisodesAction"/>.</summary>
public class SonarrSearchService(SonarrClient sonarrClient, ScanCacheStore cacheStore, NotificationService notificationService)
{
    /// <param name="settings">The Sonarr settings.</param>
    /// <param name="shokoSeriesId">The Shoko series ID.</param>
    /// <param name="sonarrSeriesId">The Sonarr series ID.</param>
    /// <param name="anidbEpisodeIds">The AniDB episode IDs to search for.</param>
    /// <param name="series">The series' scan result.</param>
    /// <param name="sonarrTitleSlug">The Sonarr title slug, for deep links.</param>
    /// <param name="episodeLookupAttempts">How many times to ask Sonarr for the episode list while it is still empty. A newly added series has no episodes until Sonarr's refresh finishes.</param>
    /// <param name="episodeLookupDelay">The wait between those attempts.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success with an optional caveat message (unmapped episodes skipped), or failure with a reason.</returns>
    public async Task<ArrActionResult<string?>> MonitorAndSearchAsync(SonarrSettings settings, int shokoSeriesId, int sonarrSeriesId, List<int> anidbEpisodeIds, SeriesMissingResult series, string? sonarrTitleSlug = null, int episodeLookupAttempts = 1, TimeSpan episodeLookupDelay = default, CancellationToken ct = default)
    {
        var episodesResult = await sonarrClient.GetEpisodesAsync(settings, sonarrSeriesId, ct);
        for (var attempt = 1; episodesResult.Success && episodesResult.Data!.Count == 0 && attempt < episodeLookupAttempts; attempt++)
        {
            await Task.Delay(episodeLookupDelay, ct);
            episodesResult = await sonarrClient.GetEpisodesAsync(settings, sonarrSeriesId, ct);
        }

        if (!episodesResult.Success)
            return ArrActionResult<string?>.Fail(episodesResult.ErrorMessage!);

        // An episode already being searched is left alone, so a repeated click or bulk run doesn't fire the same search again.
        var alreadyPending = cacheStore.GetPendingSearches().Where(p => p.ShokoSeriesId == shokoSeriesId).Select(p => p.AnidbEpisodeId).ToHashSet();
        var targetEpisodes = series.MissingEpisodes.Where(e => anidbEpisodeIds.Contains(e.AnidbEpisodeId) && !alreadyPending.Contains(e.AnidbEpisodeId)).ToList();
        if (targetEpisodes.Count == 0 && anidbEpisodeIds.Any(alreadyPending.Contains))
            return ArrActionResult<string?>.Ok("A search is already running for these episodes.");
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

        var searchResult = await sonarrClient.TriggerEpisodeSearchAsync(settings, sonarrEpisodeIds, ct);
        if (!searchResult.Success)
        {
            // The episodes are monitored now, so undo that. If Sonarr can't be reached for the undo either, track them as pending so a later scan unmonitors them.
            var reverted = await sonarrClient.UnmonitorEpisodesAsync(settings, sonarrEpisodeIds, ct);
            if (!reverted.Success)
                foreach (var pending in pendingEntries)
                    cacheStore.AddPendingSearch(pending);
            return ArrActionResult<string?>.Fail(searchResult.ErrorMessage!);
        }

        foreach (var pending in pendingEntries)
            cacheStore.AddPendingSearch(pending);
        cacheStore.AddHistoryEntries(pendingEntries.Select(p => SearchHistoryEntry.From(p, SearchHistoryOutcome.Triggered, triggeredAt)));

        var triggeredCount = targetEpisodes.Count - unmappedIds.Count;
        await notificationService.NotifyAsync(settings, $"Triggered Sonarr search for {triggeredCount} episode(s) of **{series.Title}**");

        var message = unmappedTitles.Count > 0 ? $"Search triggered. Unmapped episodes skipped: {string.Join(", ", unmappedTitles)}" : null;
        return ArrActionResult<string?>.Ok(message);
    }
}
