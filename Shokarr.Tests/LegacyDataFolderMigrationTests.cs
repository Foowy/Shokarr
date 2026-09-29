using Shokarr.Services;
using Xunit;

namespace Shokarr.Tests;

public class LegacyDataFolderMigrationTests : IDisposable
{
    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), "shokarr-migration-tests-" + Guid.NewGuid());

    private string PluginsDir => Path.Combine(_dataPath, "plugins");

    private string NewDir => Path.Combine(PluginsDir, "shokarr_data");

    public void Dispose()
    {
        if (Directory.Exists(_dataPath))
            Directory.Delete(_dataPath, recursive: true);
    }

    private string LegacyDir(string legacyName) => Path.Combine(PluginsDir, legacyName);

    // Writes real data under the current names, then renames everything back to what an older build used.
    private void SeedLegacyData(string legacyName)
    {
        using (var store = new ScanCacheStore(_dataPath))
            store.SetSeriesOverride(42, includeSpecials: true);
        File.Move(Path.Combine(NewDir, "shokarr.db"), Path.Combine(NewDir, legacyName + ".db"));
        Directory.Move(NewDir, LegacyDir(legacyName));
    }

    [Theory]
    [InlineData("shoko_sonarr")]
    [InlineData("shoko_arr")]
    public void LegacyFolder_IsMovedAndRenamed_WithDataIntact(string legacyName)
    {
        SeedLegacyData(legacyName);

        using var store = new ScanCacheStore(_dataPath);

        Assert.True(store.GetSeriesOverride(42)?.IncludeSpecials);
        Assert.False(Directory.Exists(LegacyDir(legacyName)));
        Assert.False(File.Exists(Path.Combine(NewDir, legacyName + ".db")));
    }

    [Theory]
    [InlineData("shoko_sonarr")]
    [InlineData("shoko_arr")]
    public void LegacyLogFile_MovesWithTheDatabase(string legacyName)
    {
        SeedLegacyData(legacyName);
        File.WriteAllText(Path.Combine(LegacyDir(legacyName), legacyName + "-log.db"), string.Empty);

        using var store = new ScanCacheStore(_dataPath);

        Assert.False(File.Exists(Path.Combine(NewDir, legacyName + "-log.db")));
        Assert.True(store.GetSeriesOverride(42)?.IncludeSpecials);
    }

    [Theory]
    [InlineData("shoko_sonarr")]
    [InlineData("shoko_arr")]
    public void InterruptedMove_FolderMovedButFilesNot_FinishesOnNextStart(string legacyName)
    {
        SeedLegacyData(legacyName);
        Directory.Move(LegacyDir(legacyName), NewDir);

        using var store = new ScanCacheStore(_dataPath);

        Assert.True(store.GetSeriesOverride(42)?.IncludeSpecials);
    }

    [Theory]
    [InlineData("shoko_sonarr")]
    [InlineData("shoko_arr")]
    public void ExistingNewFolder_LeavesLegacyFolderAlone(string legacyName)
    {
        SeedLegacyData(legacyName);
        Directory.CreateDirectory(NewDir);

        using var store = new ScanCacheStore(_dataPath);

        Assert.Null(store.GetSeriesOverride(42));
        Assert.True(File.Exists(Path.Combine(LegacyDir(legacyName), legacyName + ".db")));
    }
}
