using ShokoArr.Services;
using Xunit;

namespace ShokoArr.Tests;

public class LegacyDataFolderMigrationTests : IDisposable
{
    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), "shoko-arr-migration-tests-" + Guid.NewGuid());

    private string PluginsDir => Path.Combine(_dataPath, "plugins");

    private string NewDir => Path.Combine(PluginsDir, "shoko_arr");

    private string LegacyDir => Path.Combine(PluginsDir, "shoko_sonarr");

    public void Dispose()
    {
        if (Directory.Exists(_dataPath))
            Directory.Delete(_dataPath, recursive: true);
    }

    // Writes real data under the current names, then renames everything back to what builds before 1.0 used.
    private void SeedLegacyData()
    {
        using (var store = new ScanCacheStore(_dataPath))
            store.SetSeriesOverride(42, includeSpecials: true);
        File.Move(Path.Combine(NewDir, "shoko_arr.db"), Path.Combine(NewDir, "shoko_sonarr.db"));
        Directory.Move(NewDir, LegacyDir);
    }

    [Fact]
    public void LegacyFolder_IsMovedAndRenamed_WithDataIntact()
    {
        SeedLegacyData();

        using var store = new ScanCacheStore(_dataPath);

        Assert.True(store.GetSeriesOverride(42)?.IncludeSpecials);
        Assert.False(Directory.Exists(LegacyDir));
        Assert.False(File.Exists(Path.Combine(NewDir, "shoko_sonarr.db")));
    }

    [Fact]
    public void LegacyLogFile_MovesWithTheDatabase()
    {
        SeedLegacyData();
        File.WriteAllText(Path.Combine(LegacyDir, "shoko_sonarr-log.db"), string.Empty);

        using var store = new ScanCacheStore(_dataPath);

        Assert.False(File.Exists(Path.Combine(NewDir, "shoko_sonarr-log.db")));
        Assert.True(store.GetSeriesOverride(42)?.IncludeSpecials);
    }

    [Fact]
    public void InterruptedMove_FolderMovedButFilesNot_FinishesOnNextStart()
    {
        SeedLegacyData();
        Directory.Move(LegacyDir, NewDir);

        using var store = new ScanCacheStore(_dataPath);

        Assert.True(store.GetSeriesOverride(42)?.IncludeSpecials);
    }

    [Fact]
    public void ExistingNewFolder_LeavesLegacyFolderAlone()
    {
        SeedLegacyData();
        Directory.CreateDirectory(NewDir);

        using var store = new ScanCacheStore(_dataPath);

        Assert.Null(store.GetSeriesOverride(42));
        Assert.True(File.Exists(Path.Combine(LegacyDir, "shoko_sonarr.db")));
    }
}
