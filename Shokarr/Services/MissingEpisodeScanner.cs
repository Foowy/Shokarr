using NLog;
using Shoko.Abstractions.Metadata;
using Shoko.Abstractions.Metadata.Enums;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Abstractions.Metadata.Shoko;
using Shokarr.Models;

namespace Shokarr.Services;

/// <summary>Scans the Shoko collection for missing episodes on already-inventoried series, and reconciles previously-triggered Sonarr searches once Shoko confirms an episode was imported.</summary>
public class MissingEpisodeScanner(IMetadataService metadataService, ScanCacheStore cacheStore, SonarrClient sonarrClient, NotificationService notificationService, ISettingsSource settingsSource, SonarrEpisodeStatusResolver statusResolver)
{
    private static readonly Logger s_logger = LogManager.GetCurrentClassLogger();

    /// <summary>Pending entries older than this are dropped even if Sonarr keeps rejecting the unmonitor call (e.g. the Sonarr episode was deleted out-of-band), so a permanently-failing entry doesn't retry forever.</summary>
    private static readonly TimeSpan MaxPendingAge = TimeSpan.FromDays(14);

    // ponytail: single global lock -- serializes full scans and the per-series patch so their
    // read-modify-write of the persisted snapshot can't interleave. No need to cache/share the
    // in-flight result.
    private readonly SemaphoreSlim _scanLock = new(1, 1);

    /// <summary>Runs a full scan, reconciles any pending Sonarr searches against the fresh results, persists the snapshot, and returns it.</summary>
    public async Task<ScanSnapshot> ScanAsync(CancellationToken ct = default)
    {
        await _scanLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await ScanInternalAsync(ct).ConfigureAwait(false);
            cacheStore.SaveScan(snapshot);
            return snapshot;
        }
        finally
        {
            _scanLock.Release();
        }
    }

    /// <summary>Recomputes one series and splices it into the persisted snapshot, under the same lock as <see cref="ScanAsync"/>. Does not reconcile.</summary>
    public async Task<ScanSnapshot> PatchSeriesAsync(int shokoSeriesId, CancellationToken ct = default)
    {
        var updated = await ScanSeriesAsync(shokoSeriesId, ct).ConfigureAwait(false);
        await _scanLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var previous = cacheStore.GetLastScan();
            var series = (previous?.Series ?? []).Where(s => s.ShokoSeriesId != shokoSeriesId).ToList();
            if (updated is not null)
                series.Add(updated);

            var snapshot = new ScanSnapshot
            {
                ScannedAtUtc = previous?.ScannedAtUtc ?? DateTime.UtcNow,
                Series = [.. series.OrderByDescending(s => s.MissingEpisodes.Count)],
            };
            cacheStore.SaveScan(snapshot);
            return snapshot;
        }
        finally
        {
            _scanLock.Release();
        }
    }

    private async Task<ScanSnapshot> ScanInternalAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var results = new List<SeriesMissingResult>();
        var settings = settingsSource.GetSonarr();
        var pending = cacheStore.GetPendingSearches();
        var pendingByKey = pending.ToLookup(p => (p.ShokoSeriesId, p.AnidbEpisodeId));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var statusSession = await statusResolver.BeginAsync(settings, ct).ConfigureAwait(false);

        // Tracks every actually-missing episode regardless of HideUnaired, so reconciliation doesn't mistake "hidden because unaired" for "no longer missing".
        var stillMissingKeys = new HashSet<(int ShokoSeriesId, int AnidbEpisodeId)>();
        // Every non-hidden, file-less episode/special key regardless of the specials scope, so reconciliation can tell "imported" from "dropped from scope by an override".
        var missingIgnoringScope = new HashSet<(int ShokoSeriesId, int AnidbEpisodeId)>();

        foreach (var series in metadataService.GetAllShokoSeries())
        {
            ct.ThrowIfCancellationRequested();
            // Only consider series already inventoried (v1 scope excludes fully-unowned anime).
            if (series.LocalEpisodeCounts.Episodes + series.LocalEpisodeCounts.Specials <= 0)
                continue;

            var candidates = MissingCandidates(series);
            var inScope = ScopedTo(candidates, series, settings);
            foreach (var e in candidates)
                missingIgnoringScope.Add((series.LocalID, e.AnidbEpisodeID));
            foreach (var e in inScope)
                stillMissingKeys.Add((series.LocalID, e.AnidbEpisodeID));

            var result = BuildSeriesResult(series, inScope, settings, pendingByKey, today);
            if (result is not null)
                results.Add(result);
        }

        await Parallel.ForEachAsync(results, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, (r, _) => new ValueTask(statusSession.ApplyAsync(r))).ConfigureAwait(false);

        var descopedKeys = new HashSet<(int ShokoSeriesId, int AnidbEpisodeId)>(missingIgnoringScope);
        descopedKeys.ExceptWith(stillMissingKeys);

        await ReconcilePendingSearchesAsync(pending, stillMissingKeys, descopedKeys, settings, ct).ConfigureAwait(false);

        return new ScanSnapshot
        {
            ScannedAtUtc = DateTime.UtcNow,
            Series = [.. results.OrderByDescending(s => s.MissingEpisodes.Count)],
        };
    }

    /// <summary>An episode Shoko knows about but has no file for, and that isn't user-hidden - i.e. a candidate for a Sonarr search. Type-filtering (specials scope) is applied separately by each caller.</summary>
    private static bool IsMissingVideo(IShokoEpisode e) => !e.IsHidden && e.Videos.Count == 0;

    /// <summary>Every episode or special of a series with no file that isn't user-hidden, regardless of the specials scope.</summary>
    private static List<IShokoEpisode> MissingCandidates(IShokoSeries series) =>
        [.. series.Episodes.Where(e => (e.Type == EpisodeType.Episode || e.Type == EpisodeType.Special) && IsMissingVideo(e))];

    /// <summary>Narrows candidates to the scanned types for a series, honoring the global specials setting and any per-series override.</summary>
    private IEnumerable<IShokoEpisode> ScopedTo(List<IShokoEpisode> candidates, IShokoSeries series, Config.SonarrSettings settings)
    {
        var includeSpecials = cacheStore.GetSeriesOverride(series.LocalID)?.IncludeSpecials ?? settings.IncludeSpecials;
        return includeSpecials ? candidates : candidates.Where(e => e.Type == EpisodeType.Episode);
    }

    /// <summary>Builds the missing-episode result for one series, or null if it has nothing missing after the specials scope and HideUnaired filters.</summary>
    private SeriesMissingResult? BuildSeriesResult(IShokoSeries series, IEnumerable<IShokoEpisode> inScope, Config.SonarrSettings settings, ILookup<(int, int), PendingSearch> pendingByKey, DateOnly today)
    {
        var seriesOverride = cacheStore.GetSeriesOverride(series.LocalID);
        var overrideValue = seriesOverride?.IncludeSpecials;

        var missing = inScope
            .Select(e => new MissingEpisodeInfo
            {
                AnidbEpisodeId = e.AnidbEpisodeID,
                EpisodeNumber = e.EpisodeNumber,
                IsSpecial = e.Type == EpisodeType.Special,
                Title = e.Title,
                AirDate = e.AirDate,
                ActionStatus = pendingByKey.Contains((series.LocalID, e.AnidbEpisodeID)) ? "search-triggered" : "none",
            })
            .ToList();

        var displayMissing = (settings.HideUnaired ? missing.Where(e => e.AirDate is { } airDate && airDate <= today) : missing)
            .OrderBy(e => e.IsSpecial).ThenBy(e => e.EpisodeNumber)
            .ToList();

        if (displayMissing.Count == 0)
            return null;

        var tvdbId = series.GetLinkedSeries(MetadataSource.TMDB)
            .SelectMany(s => s.CrossSourceIDs)
            .Where(id => id.Source.Value == "tvdb" && id.EntityType == MetadataEntityType.Series && id.TryGetNumericID<int>(out _))
            .Select(id => (int?)id.GetNumericID<int>())
            .FirstOrDefault();

        return new SeriesMissingResult
        {
            ShokoSeriesId = series.LocalID,
            Title = series.Title,
            TvdbId = tvdbId,
            GroupTitle = series.ParentGroup?.Title,
            QualityProfileIdOverride = seriesOverride?.QualityProfileId,
            RootFolderPathOverride = seriesOverride?.RootFolderPath,
            IncludeSpecialsOverride = overrideValue,
            MissingEpisodes = displayMissing,
        };
    }

    /// <summary>Recomputes a single series' missing-episode result, e.g. after a per-series override changed. Does not run reconciliation -- that only happens on a full <see cref="ScanAsync"/>.</summary>
    public async Task<SeriesMissingResult?> ScanSeriesAsync(int shokoSeriesId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var series = metadataService.GetShokoSeriesByID(shokoSeriesId);
        if (series is null
            || series.LocalEpisodeCounts.Episodes + series.LocalEpisodeCounts.Specials <= 0)
            return null;

        var settings = settingsSource.GetSonarr();
        var pendingByKey = cacheStore.GetPendingSearches().ToLookup(p => (p.ShokoSeriesId, p.AnidbEpisodeId));
        var inScope = ScopedTo(MissingCandidates(series), series, settings);
        var result = BuildSeriesResult(series, inScope, settings, pendingByKey, DateOnly.FromDateTime(DateTime.UtcNow));
        if (result is not null)
            await (await statusResolver.BeginAsync(settings, ct).ConfigureAwait(false)).ApplyAsync(result).ConfigureAwait(false);
        return result;
    }

    /// <summary>Unmonitors in Sonarr, and stops tracking, each pending episode no longer missing in the fresh results. Failures are logged and retried next scan, never thrown.
    /// "No longer missing" covers both an actual Shoko import and a scope change (e.g. a specials-exclude override).
    /// <paramref name="stillMissingKeys"/> ignores HideUnaired: an unaired episode hidden from the dashboard is still missing.</summary>
    private async Task ReconcilePendingSearchesAsync(List<PendingSearch> pending, HashSet<(int ShokoSeriesId, int AnidbEpisodeId)> stillMissingKeys, HashSet<(int ShokoSeriesId, int AnidbEpisodeId)> descopedKeys, Config.SonarrSettings settings, CancellationToken ct)
    {
        if (pending.Count == 0)
            return;

        foreach (var entry in pending)
        {
            ct.ThrowIfCancellationRequested();
            if (stillMissingKeys.Contains((entry.ShokoSeriesId, entry.AnidbEpisodeId)))
                continue;

            try
            {
                var result = await sonarrClient.UnmonitorEpisodesAsync(settings, [entry.SonarrEpisodeId], ct).ConfigureAwait(false);
                if (result.Success)
                {
                    cacheStore.RemovePendingSearch(entry.ShokoSeriesId, entry.AnidbEpisodeId);
                    var outcome = descopedKeys.Contains((entry.ShokoSeriesId, entry.AnidbEpisodeId)) ? SearchHistoryOutcome.Descoped : SearchHistoryOutcome.Imported;
                    cacheStore.AddHistoryEntry(SearchHistoryEntry.From(entry, outcome));
                }
                else
                {
                    s_logger.Warn("Shokarr: failed to unmonitor Sonarr episode {SonarrEpisodeId} for AniDB episode {AnidbEpisodeId}: {Error}", entry.SonarrEpisodeId, entry.AnidbEpisodeId, result.ErrorMessage);
                    cacheStore.RecordPendingFailure(entry, result.ErrorMessage!);
                    await ExpireIfStaleAsync(settings, entry, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                s_logger.Warn(ex, "Shokarr: failed to unmonitor Sonarr episode {SonarrEpisodeId} for AniDB episode {AnidbEpisodeId}", entry.SonarrEpisodeId, entry.AnidbEpisodeId);
                cacheStore.RecordPendingFailure(entry, ex.Message);
                await ExpireIfStaleAsync(settings, entry, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Drops a pending entry that has failed reconciliation for longer than <see cref="MaxPendingAge"/>, instead of retrying it forever.</summary>
    private async Task ExpireIfStaleAsync(Config.SonarrSettings settings, PendingSearch entry, CancellationToken ct)
    {
        if (DateTime.UtcNow - entry.TriggeredAtUtc < MaxPendingAge)
            return;

        s_logger.Warn("Shokarr: giving up on Sonarr episode {SonarrEpisodeId} for AniDB episode {AnidbEpisodeId} after {MaxPendingAge} of failed reconciliation attempts", entry.SonarrEpisodeId, entry.AnidbEpisodeId, MaxPendingAge);
        cacheStore.RemovePendingSearch(entry.ShokoSeriesId, entry.AnidbEpisodeId);
        cacheStore.AddHistoryEntry(SearchHistoryEntry.From(entry, SearchHistoryOutcome.Expired));

        var seriesLabel = string.IsNullOrEmpty(entry.SeriesTitle) ? $"series #{entry.ShokoSeriesId}" : entry.SeriesTitle;
        var episodeLabel = string.IsNullOrEmpty(entry.EpisodeTitle) ? $"AniDB episode {entry.AnidbEpisodeId}" : entry.EpisodeTitle;
        await notificationService.NotifyAsync(settings, $"Gave up tracking **{seriesLabel}** - {episodeLabel} - after {MaxPendingAge.TotalDays:0} days of failed reconciliation", ct).ConfigureAwait(false);
    }
}
