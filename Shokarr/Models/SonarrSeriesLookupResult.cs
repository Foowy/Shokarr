namespace Shokarr.Models;

/// <summary>A single candidate result from Sonarr's series lookup endpoint.</summary>
public class SonarrSeriesLookupResult
{
    /// <summary>The candidate's TVDB ID.</summary>
    public int TvdbId { get; set; }

    /// <summary>The candidate's title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The candidate's first-air year, if known.</summary>
    public int? Year { get; set; }
}
