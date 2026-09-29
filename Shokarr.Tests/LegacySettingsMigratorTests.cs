using Shokarr.Config;
using Shokarr.Services;
using Xunit;

namespace Shokarr.Tests;

public class LegacySettingsMigratorTests
{
    [Fact]
    public void TryMigrate_UnconfiguredTarget_CopiesEverything()
    {
        var sonarr = new SonarrSettings
        {
            BaseUrl = "http://s",
            ApiKey = "sk",
            QualityProfileId = 7,
            RootFolderPath = "/anime",
            ScanIntervalHours = 6,
            IncludeSpecials = false,
            HideUnaired = true,
            NotificationWebhookUrl = "http://hook",
        };
        var radarr = new RadarrSettings { BaseUrl = "http://r", ApiKey = "rk", QualityProfileId = 3, RootFolderPath = "/movies" };
        var target = new ShokarrConfiguration();

        var migrated = LegacySettingsMigrator.TryMigrate(sonarr, radarr, target);

        Assert.True(migrated);
        var roundTrip = target.ToSonarrSettings();
        Assert.Equal("http://s", roundTrip.BaseUrl);
        Assert.Equal("sk", roundTrip.ApiKey);
        Assert.Equal(7, roundTrip.QualityProfileId);
        Assert.Equal("/anime", roundTrip.RootFolderPath);
        Assert.Equal(6, roundTrip.ScanIntervalHours);
        Assert.False(roundTrip.IncludeSpecials);
        Assert.True(roundTrip.HideUnaired);
        Assert.Equal("http://hook", roundTrip.NotificationWebhookUrl);
        var radarrRoundTrip = target.ToRadarrSettings();
        Assert.Equal("http://r", radarrRoundTrip.BaseUrl);
        Assert.Equal(3, radarrRoundTrip.QualityProfileId);
        Assert.Equal("/movies", radarrRoundTrip.RootFolderPath);
    }

    [Fact]
    public void TryMigrate_TargetAlreadyConfigured_DoesNothing()
    {
        var target = new ShokarrConfiguration { SonarrUrl = "http://existing" };

        var migrated = LegacySettingsMigrator.TryMigrate(new SonarrSettings { BaseUrl = "http://s" }, new RadarrSettings(), target);

        Assert.False(migrated);
        Assert.Equal("http://existing", target.SonarrUrl);
    }

    [Fact]
    public void TryMigrate_NothingLegacy_DoesNothing()
    {
        Assert.False(LegacySettingsMigrator.TryMigrate(new SonarrSettings(), new RadarrSettings(), new ShokarrConfiguration()));
    }

    [Theory]
    [InlineData(1000, 720)]
    [InlineData(-5, 0)]
    public void TryMigrate_ScanIntervalOutOfRange_IsClamped(int legacy, int expected)
    {
        var target = new ShokarrConfiguration();

        LegacySettingsMigrator.TryMigrate(new SonarrSettings { BaseUrl = "http://s", ScanIntervalHours = legacy }, new RadarrSettings(), target);

        Assert.Equal(expected, target.ScanIntervalHours);
    }
}
