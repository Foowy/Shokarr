namespace Shokarr.Models;

/// <summary>A full snapshot of the most recent missing-episode scan.</summary>
public class ScanSnapshot
{
    /// <summary>When the scan completed, in UTC.</summary>
    public DateTime ScannedAtUtc { get; set; }

    /// <summary>Per-series missing-episode results, series with zero missing episodes are omitted.</summary>
    public List<SeriesMissingResult> Series { get; set; } = [];
}
