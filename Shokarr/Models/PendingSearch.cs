namespace Shokarr.Models;

/// <summary>An episode the plugin has told Sonarr to monitor and search for, pending confirmation (via a later scan) that Shoko has actually imported it.</summary>
public class PendingSearch
{
    /// <summary>The Shoko series ID.</summary>
    public int ShokoSeriesId { get; set; }

    /// <summary>The series title at the time the search was triggered, for display without a re-lookup. Empty for entries persisted before this field was added.</summary>
    public string SeriesTitle { get; set; } = string.Empty;

    /// <summary>The AniDB episode ID - the stable key used to match this entry back to a scan result.</summary>
    public int AnidbEpisodeId { get; set; }

    /// <summary>The episode title at the time the search was triggered, for display without a re-lookup. Empty for entries persisted before this field was added.</summary>
    public string EpisodeTitle { get; set; } = string.Empty;

    /// <summary>The Sonarr series ID this episode belongs to.</summary>
    public int SonarrSeriesId { get; set; }

    /// <summary>The Sonarr series' title slug, used to build a direct link to its Sonarr UI page. Null for entries persisted before this field was added.</summary>
    public string? SonarrTitleSlug { get; set; }

    /// <summary>The Sonarr episode ID to unmonitor once Shoko confirms the episode is no longer missing.</summary>
    public int SonarrEpisodeId { get; set; }

    /// <summary>When the search was triggered, in UTC.</summary>
    public DateTime TriggeredAtUtc { get; set; }

    /// <summary>How many scans have failed to unmonitor this episode in Sonarr since the search was triggered.</summary>
    public int FailedReconciliations { get; set; }

    /// <summary>The error from the most recent failed reconciliation attempt.</summary>
    public string? LastError { get; set; }
}
