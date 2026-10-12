using Moq;
using Shoko.Abstractions.Config.Services;
using Shokarr.Config;
using Shokarr.Services;
using Xunit;

namespace Shokarr.Tests;

public class NativeSettingsSourceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "shokarr-native-settings-tests-" + Guid.NewGuid());
    private readonly ScanCacheStore _legacyStore;

    public NativeSettingsSourceTests()
    {
        Directory.CreateDirectory(_tempDir);
        _legacyStore = new ScanCacheStore(_tempDir);
    }

    public void Dispose()
    {
        _legacyStore.Dispose();
        Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void GetSonarr_MigrationFails_IsRetriedUntilItSucceedsThenStops()
    {
        var migrationLoads = 0;
        var configurationService = new Mock<IConfigurationService>();
        configurationService.Setup(c => c.Load<ShokarrConfiguration>(true)).Returns(() =>
        {
            if (++migrationLoads == 1)
                throw new IOException("disk busy");
            return new ShokarrConfiguration();
        });
        configurationService.Setup(c => c.Load<ShokarrConfiguration>(false)).Returns(new ShokarrConfiguration());
        var source = new NativeSettingsSource(configurationService.Object, _legacyStore);

        source.GetSonarr();
        source.GetSonarr();
        source.GetSonarr();

        Assert.Equal(2, migrationLoads);
    }

    [Fact]
    public void GetSonarr_MigrationKeepsFailing_StopsRetryingAfterThreeAttempts()
    {
        var migrationLoads = 0;
        var configurationService = new Mock<IConfigurationService>();
        configurationService.Setup(c => c.Load<ShokarrConfiguration>(true)).Returns(() =>
        {
            migrationLoads++;
            throw new IOException("disk busy");
        });
        configurationService.Setup(c => c.Load<ShokarrConfiguration>(false)).Returns(new ShokarrConfiguration());
        var source = new NativeSettingsSource(configurationService.Object, _legacyStore);

        for (var i = 0; i < 6; i++)
            source.GetSonarr();

        Assert.Equal(3, migrationLoads);
    }
}
