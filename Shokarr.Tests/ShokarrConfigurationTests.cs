using Shoko.Abstractions.Config.Components;
using Shokarr.Config;
using Xunit;

namespace Shokarr.Tests;

public class ShokarrConfigurationTests
{
    [Fact]
    public void ToSonarrSettings_MapsConnectionSelectionsAndScanOptions()
    {
        var config = new ShokarrConfiguration
        {
            SonarrUrl = "http://sonarr:8989",
            SonarrApiKey = "key",
            ScanIntervalHours = 12,
            IncludeSpecials = false,
            HideUnaired = true,
            CountSonarrHeldAsMissing = false,
            NotificationWebhookUrl = "http://hook",
            SonarrQualityProfile = new SelectComponent<int>([new(7, "HD", isSelected: true)]),
            SonarrRootFolder = new SelectComponent<string>([new("/storage/Anime", "/storage/Anime", isSelected: true)]),
        };

        var settings = config.ToSonarrSettings();

        Assert.Equal("http://sonarr:8989", settings.BaseUrl);
        Assert.Equal("key", settings.ApiKey);
        Assert.Equal(7, settings.QualityProfileId);
        Assert.Equal("/storage/Anime", settings.RootFolderPath);
        Assert.Equal(12, settings.ScanIntervalHours);
        Assert.False(settings.IncludeSpecials);
        Assert.True(settings.HideUnaired);
        Assert.False(settings.CountSonarrHeldAsMissing);
        Assert.Equal("http://hook", settings.NotificationWebhookUrl);
    }

    [Fact]
    public void ToSonarrSettings_NothingSelected_YieldsNullProfileAndFolder()
    {
        var settings = new ShokarrConfiguration().ToSonarrSettings();

        Assert.Null(settings.QualityProfileId);
        Assert.Null(settings.RootFolderPath);
        Assert.Equal(24, settings.ScanIntervalHours);
        Assert.True(settings.IncludeSpecials);
        Assert.True(settings.CountSonarrHeldAsMissing);
    }

    [Fact]
    public void ToRadarrSettings_MapsConnectionAndSelections()
    {
        var config = new ShokarrConfiguration
        {
            RadarrUrl = "http://radarr:7878",
            RadarrApiKey = "rkey",
            RadarrQualityProfile = new SelectComponent<int>([new(3, "1080p", isSelected: true)]),
            RadarrRootFolder = new SelectComponent<string>([new("/movies", "/movies", isSelected: true)]),
        };

        var settings = config.ToRadarrSettings();

        Assert.Equal("http://radarr:7878", settings.BaseUrl);
        Assert.Equal("rkey", settings.ApiKey);
        Assert.Equal(3, settings.QualityProfileId);
        Assert.Equal("/movies", settings.RootFolderPath);
    }

    [Fact]
    public void ApplySonarr_CopiesFieldsAndClampsInterval_LeavingRadarrAlone()
    {
        var config = new ShokarrConfiguration { RadarrUrl = "http://radarr", RadarrApiKey = "rk" };

        config.ApplySonarr(new SonarrSettings { BaseUrl = "http://s", ApiKey = "k", QualityProfileId = 3, RootFolderPath = "/a", ScanIntervalHours = 1000, IncludeSpecials = false, HideUnaired = true, CountSonarrHeldAsMissing = false, NotificationWebhookUrl = "http://h" });

        var s = config.ToSonarrSettings();
        Assert.Equal("http://s", s.BaseUrl);
        Assert.Equal("k", s.ApiKey);
        Assert.Equal(3, s.QualityProfileId);
        Assert.Equal("/a", s.RootFolderPath);
        Assert.Equal(720, s.ScanIntervalHours);
        Assert.False(s.IncludeSpecials);
        Assert.True(s.HideUnaired);
        Assert.False(s.CountSonarrHeldAsMissing);
        Assert.Equal("http://h", s.NotificationWebhookUrl);
        Assert.Equal("http://radarr", config.RadarrUrl);
        Assert.Equal("rk", config.RadarrApiKey);
    }

    [Fact]
    public void ApplyRadarr_CopiesFields_LeavingSonarrAlone()
    {
        var config = new ShokarrConfiguration { SonarrUrl = "http://s", ScanIntervalHours = 5 };

        config.ApplyRadarr(new RadarrSettings { BaseUrl = "http://r", ApiKey = "k", QualityProfileId = 2, RootFolderPath = "/m" });

        var r = config.ToRadarrSettings();
        Assert.Equal("http://r", r.BaseUrl);
        Assert.Equal("k", r.ApiKey);
        Assert.Equal(2, r.QualityProfileId);
        Assert.Equal("/m", r.RootFolderPath);
        Assert.Equal("http://s", config.SonarrUrl);
        Assert.Equal(5, config.ScanIntervalHours);
    }

    [Fact]
    public void ApplySonarr_KeepsRealOptionLabelWhenIdIsAnOption()
    {
        var config = new ShokarrConfiguration { SonarrQualityProfile = new SelectComponent<int>([new(7, "HD", isSelected: false), new(8, "SD", isSelected: true)]) };

        config.ApplySonarr(new SonarrSettings { QualityProfileId = 7 });

        Assert.Equal(2, config.SonarrQualityProfile.Options.Count);
        Assert.Equal(7, config.SonarrQualityProfile.SelectedValue);
        Assert.Equal("HD", config.SonarrQualityProfile.Options.First(o => o.Value == 7).Label);
    }

    [Fact]
    public void ApplySonarr_UnknownIdGetsPlaceholderLabel_NullClearsSelection()
    {
        var config = new ShokarrConfiguration();

        config.ApplySonarr(new SonarrSettings { QualityProfileId = 9, RootFolderPath = "/x" });
        Assert.Equal("#9", config.SonarrQualityProfile.Options.Single().Label);
        Assert.Equal("/x", config.SonarrRootFolder.SelectedValue);

        config.ApplySonarr(new SonarrSettings());
        Assert.False(config.SonarrQualityProfile.HasSelectedValue);
        Assert.False(config.SonarrRootFolder.HasSelectedValue);
    }
}
