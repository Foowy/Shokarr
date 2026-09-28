using LiteDB;
using ShokoArr.Config;
using ShokoArr.Models;

namespace ShokoArr.Services;

/// <summary>LiteDB-backed persistence for plugin settings, scan snapshots, and per-episode action status.
/// Registered as a DI singleton; holds one shared <see cref="LiteDatabase"/> connection for the plugin's
/// lifetime, since LiteDB's direct-mode connections take an exclusive file lock per instance and opening
/// one per call would throw <see cref="IOException"/> under concurrent access (scheduler + API requests).</summary>
public class ScanCacheStore : IDisposable
{
    private const string SettingsCollectionName = "settings";
    private const string RadarrSettingsCollectionName = "radarrSettings";
    private const string ScanCollectionName = "scans";
    private const string SeriesOverridesCollectionName = "seriesOverrides";
    private const string PendingSearchesCollectionName = "pendingSearches";
    private const string SearchHistoryCollectionName = "searchHistory";
    private const int SettingsDocumentId = 1;
    private const int ScanDocumentId = 1;

    /// <summary>Caps the search-history log so it can't grow unbounded; oldest entries are trimmed on insert once exceeded.</summary>
    private const int MaxHistoryEntries = 500;

    private readonly LiteDatabase _db;

    /// <summary>Creates a store rooted at the given data directory, creating the directory if needed.</summary>
    /// <param name="dataPath">The host's data directory (typically <see cref="Shoko.Abstractions.Plugin.IApplicationPaths.DataPath"/>).</param>
    public ScanCacheStore(string dataPath)
    {
        var pluginDir = Path.Combine(dataPath, "plugins", ShokoArrConstants.PluginDataSubfolder);
        MigrateLegacyDataFolder(Path.Combine(dataPath, "plugins", "shoko_sonarr"), pluginDir);
        Directory.CreateDirectory(pluginDir);
        _db = new LiteDatabase(Path.Combine(pluginDir, ShokoArrConstants.LiteDbFileName));
    }

    // Builds before 1.0 kept the database under the ShokoSonarr names. Each step checks its own target so a
    // move interrupted between the folder and the files finishes on the next start instead of opening an empty database.
    private static void MigrateLegacyDataFolder(string legacyDir, string pluginDir)
    {
        if (!Directory.Exists(pluginDir) && Directory.Exists(legacyDir))
            Directory.Move(legacyDir, pluginDir);

        var legacyDb = Path.Combine(pluginDir, "shoko_sonarr.db");
        var db = Path.Combine(pluginDir, ShokoArrConstants.LiteDbFileName);
        if (File.Exists(db) || !File.Exists(legacyDb))
            return;

        // LiteDB keeps uncommitted pages in "<name>-log<ext>" beside the database, so the pair moves together.
        var legacyLog = Path.Combine(pluginDir, "shoko_sonarr-log.db");
        if (File.Exists(legacyLog))
            File.Move(legacyLog, Path.Combine(pluginDir, Path.GetFileNameWithoutExtension(db) + "-log.db"));
        File.Move(legacyDb, db);
    }

    /// <inheritdoc/>
    public void Dispose() => _db.Dispose();

    /// <summary>Gets the current Sonarr settings, or defaults if none have been saved.</summary>
    // Read-only migration source; the native configuration is authoritative.
    public SonarrSettings GetLegacySettings()
    {
        var col = _db.GetCollection<SettingsDocument>(SettingsCollectionName);
        var doc = col.FindById(SettingsDocumentId);
        return doc?.Settings ?? new SonarrSettings();
    }

    /// <summary>Gets the current Radarr settings, or defaults if none have been saved.</summary>
    // Read-only migration source; the native configuration is authoritative.
    public RadarrSettings GetLegacyRadarrSettings()
    {
        var col = _db.GetCollection<RadarrSettingsDocument>(RadarrSettingsCollectionName);
        var doc = col.FindById(SettingsDocumentId);
        return doc?.Settings ?? new RadarrSettings();
    }

    /// <summary>Sets (or clears, when <paramref name="includeSpecials"/> is null) the specials override for a series, preserving any Sonarr quality-profile/root-folder override already set.</summary>
    public void SetSeriesOverride(int shokoSeriesId, bool? includeSpecials) =>
        UpsertSeriesOverride(shokoSeriesId, o => o.IncludeSpecials = includeSpecials);

    /// <summary>Sets (or clears, when both parameters are null) the Sonarr quality-profile/root-folder override for a series, preserving any specials override already set.</summary>
    public void SetSeriesSonarrOverride(int shokoSeriesId, int? qualityProfileId, string? rootFolderPath) =>
        UpsertSeriesOverride(shokoSeriesId, o =>
        {
            o.QualityProfileId = qualityProfileId;
            o.RootFolderPath = rootFolderPath;
        });

    /// <summary>Applies <paramref name="mutate"/> to the existing override row and replaces it, deleting the row if no fields remain set. Read-mutate-write so setting one field never wipes another.</summary>
    private void UpsertSeriesOverride(int shokoSeriesId, Action<SeriesOverride> mutate)
    {
        var col = _db.GetCollection<SeriesOverride>(SeriesOverridesCollectionName);
        var existing = col.Find(o => o.ShokoSeriesId == shokoSeriesId).FirstOrDefault() ?? new SeriesOverride { ShokoSeriesId = shokoSeriesId };
        mutate(existing);
        col.DeleteMany(o => o.ShokoSeriesId == shokoSeriesId);
        if (existing.IncludeSpecials is not null || existing.QualityProfileId is not null || !string.IsNullOrEmpty(existing.RootFolderPath))
            col.Insert(existing);
    }

    /// <summary>Gets the most recent scan snapshot, or null if no scan has run yet.</summary>
    public ScanSnapshot? GetLastScan()
    {
        var col = _db.GetCollection<ScanDocument>(ScanCollectionName);
        return col.FindById(ScanDocumentId)?.Snapshot;
    }

    /// <summary>Persists a scan snapshot, replacing any previous snapshot wholesale.</summary>
    public void SaveScan(ScanSnapshot snapshot)
    {
        var col = _db.GetCollection<ScanDocument>(ScanCollectionName);
        col.Upsert(new ScanDocument { Id = ScanDocumentId, Snapshot = snapshot });
    }

    /// <summary>Gets the specials override for a series, or null if no override is set (inherit the global default).</summary>
    public SeriesOverride? GetSeriesOverride(int shokoSeriesId)
    {
        var col = _db.GetCollection<SeriesOverride>(SeriesOverridesCollectionName);
        return col.Find(o => o.ShokoSeriesId == shokoSeriesId).FirstOrDefault();
    }


    /// <summary>Records that an episode's search was triggered in Sonarr, replacing any existing pending entry for the same episode.</summary>
    public void AddPendingSearch(PendingSearch entry)
    {
        var col = _db.GetCollection<PendingSearch>(PendingSearchesCollectionName);
        col.DeleteMany(p => p.ShokoSeriesId == entry.ShokoSeriesId && p.AnidbEpisodeId == entry.AnidbEpisodeId);
        col.Insert(entry);
    }

    /// <summary>Gets all episodes currently pending reconciliation with Sonarr.</summary>
    public List<PendingSearch> GetPendingSearches()
    {
        var col = _db.GetCollection<PendingSearch>(PendingSearchesCollectionName);
        return col.FindAll().ToList();
    }

    /// <summary>Bumps the failed-reconciliation count and stores the latest error on an existing pending entry.</summary>
    public void RecordPendingFailure(PendingSearch entry, string error)
    {
        entry.FailedReconciliations++;
        entry.LastError = error;
        AddPendingSearch(entry);
    }

    /// <summary>Removes a pending search entry once it's been reconciled (or is being replaced - see <see cref="AddPendingSearch"/>).</summary>
    public void RemovePendingSearch(int shokoSeriesId, int anidbEpisodeId)
    {
        var col = _db.GetCollection<PendingSearch>(PendingSearchesCollectionName);
        col.DeleteMany(p => p.ShokoSeriesId == shokoSeriesId && p.AnidbEpisodeId == anidbEpisodeId);
    }

    /// <summary>Appends a search-history entry, trimming the oldest entries once <see cref="MaxHistoryEntries"/> is exceeded.</summary>
    public void AddHistoryEntry(SearchHistoryEntry entry) => AddHistoryEntries([entry]);

    /// <summary>Appends one or more search-history entries, trimming the oldest down to <see cref="MaxHistoryEntries"/> once after the whole batch is inserted, not once per entry.</summary>
    public void AddHistoryEntries(IEnumerable<SearchHistoryEntry> entries)
    {
        var col = _db.GetCollection<SearchHistoryEntry>(SearchHistoryCollectionName);
        col.InsertBulk(entries);

        // Ids auto-increment and only the oldest are ever deleted, so the ids are contiguous and a cutoff on id keeps exactly the newest MaxHistoryEntries.
        var cutoffId = col.Max().AsInt32 - MaxHistoryEntries;
        col.DeleteMany(e => e.Id <= cutoffId);
    }

    /// <summary>Gets the most recent search-history entries, newest first.</summary>
    public List<SearchHistoryEntry> GetHistory(int limit = 200)
    {
        var col = _db.GetCollection<SearchHistoryEntry>(SearchHistoryCollectionName);
        return col.FindAll().OrderByDescending(e => e.TimestampUtc).Take(limit).ToList();
    }

    private class SettingsDocument
    {
        public int Id { get; set; }
        public SonarrSettings Settings { get; set; } = new();
    }

    private class RadarrSettingsDocument
    {
        public int Id { get; set; }
        public RadarrSettings Settings { get; set; } = new();
    }

    private class ScanDocument
    {
        public int Id { get; set; }
        public ScanSnapshot Snapshot { get; set; } = new();
    }
}
