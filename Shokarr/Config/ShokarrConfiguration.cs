using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using Shoko.Abstractions.Config;
using Shoko.Abstractions.Config.Attributes;
using Shoko.Abstractions.Config.Components;
using Shoko.Abstractions.Config.Enums;
using Shokarr.Services;

namespace Shokarr.Config;

// Shoko names the settings file after the display name; pinned so settings saved as "Shoko Arr" still load.
[StorageLocation(FileName = "shoko-arr")]
[Display(Name = "Shokarr")]
public class ShokarrConfiguration : IConfiguration
{
    [SectionName("Sonarr")]
    [Display(Name = "Sonarr URL")]
    public string? SonarrUrl { get; set; }

    [SectionName("Sonarr")]
    [Display(Name = "Sonarr API Key")]
    [PasswordPropertyText]
    public string? SonarrApiKey { get; set; }

    [SectionName("Sonarr")]
    [Display(Name = "Quality Profile")]
    public SelectComponent<int> SonarrQualityProfile { get; set; } = new();

    [SectionName("Sonarr")]
    [Display(Name = "Root Folder")]
    public SelectComponent<string> SonarrRootFolder { get; set; } = new();

    [SectionName("Radarr")]
    [Display(Name = "Radarr URL")]
    public string? RadarrUrl { get; set; }

    [SectionName("Radarr")]
    [Display(Name = "Radarr API Key")]
    [PasswordPropertyText]
    public string? RadarrApiKey { get; set; }

    [SectionName("Radarr")]
    [Display(Name = "Quality Profile")]
    public SelectComponent<int> RadarrQualityProfile { get; set; } = new();

    [SectionName("Radarr")]
    [Display(Name = "Root Folder")]
    public SelectComponent<string> RadarrRootFolder { get; set; } = new();

    [SectionName("Scanning")]
    [Display(Name = "Scan Interval (hours, 0 disables)")]
    [Range(0, 720)]
    [DefaultValue(24)]
    public int ScanIntervalHours { get; set; } = 24;

    [SectionName("Scanning")]
    [Display(Name = "Include Specials")]
    [DefaultValue(true)]
    public bool IncludeSpecials { get; set; } = true;

    [SectionName("Scanning")]
    [Display(Name = "Hide Unaired Episodes")]
    public bool HideUnaired { get; set; }

    [SectionName("Scanning")]
    [Display(Name = "Count episodes already in Sonarr as missing")]
    [DefaultValue(true)]
    public bool CountSonarrHeldAsMissing { get; set; } = true;

    [SectionName("Notifications")]
    [Display(Name = "Notification Webhook URL (Discord-compatible)")]
    [PasswordPropertyText]
    public string? NotificationWebhookUrl { get; set; }

    public SonarrSettings ToSonarrSettings() => new()
    {
        BaseUrl = SonarrUrl,
        ApiKey = SonarrApiKey,
        QualityProfileId = SonarrQualityProfile.HasSelectedValue ? SonarrQualityProfile.SelectedValue : null,
        RootFolderPath = SonarrRootFolder.HasSelectedValue ? SonarrRootFolder.SelectedValue : null,
        ScanIntervalHours = ScanIntervalHours,
        IncludeSpecials = IncludeSpecials,
        HideUnaired = HideUnaired,
        CountSonarrHeldAsMissing = CountSonarrHeldAsMissing,
        NotificationWebhookUrl = NotificationWebhookUrl,
    };

    public RadarrSettings ToRadarrSettings() => new()
    {
        BaseUrl = RadarrUrl,
        ApiKey = RadarrApiKey,
        QualityProfileId = RadarrQualityProfile.HasSelectedValue ? RadarrQualityProfile.SelectedValue : null,
        RootFolderPath = RadarrRootFolder.HasSelectedValue ? RadarrRootFolder.SelectedValue : null,
    };

    public void ApplySonarr(SonarrSettings s)
    {
        SonarrUrl = s.BaseUrl;
        SonarrApiKey = s.ApiKey;
        SonarrQualityProfile = ChooseProfile(SonarrQualityProfile, s.QualityProfileId);
        SonarrRootFolder = ChooseFolder(SonarrRootFolder, s.RootFolderPath);
        ScanIntervalHours = Math.Clamp(s.ScanIntervalHours, 0, 720);
        IncludeSpecials = s.IncludeSpecials;
        HideUnaired = s.HideUnaired;
        CountSonarrHeldAsMissing = s.CountSonarrHeldAsMissing;
        NotificationWebhookUrl = s.NotificationWebhookUrl;
    }

    public void ApplyRadarr(RadarrSettings s)
    {
        RadarrUrl = s.BaseUrl;
        RadarrApiKey = s.ApiKey;
        RadarrQualityProfile = ChooseProfile(RadarrQualityProfile, s.QualityProfileId);
        RadarrRootFolder = ChooseFolder(RadarrRootFolder, s.RootFolderPath);
    }

    public static SelectComponent<int> ChooseProfile(SelectComponent<int> current, int? id) =>
        id is { } v ? Choose(current, v, $"#{v}") : new();

    public static SelectComponent<string> ChooseFolder(SelectComponent<string> current, string? path) =>
        string.IsNullOrEmpty(path) ? new() : Choose(current, path, path);

    private static SelectComponent<T> Choose<T>(SelectComponent<T> current, T value, string label) where T : IEquatable<T>
    {
        if (current.Options.Any(o => EqualityComparer<T>.Default.Equals(o.Value, value)))
        {
            current.SelectedValues = [value];
            return current;
        }

        return new([new(value, label, isSelected: true)]);
    }

    [CustomAction(Theme = DisplayColorTheme.Primary, Position = DisplayButtonPosition.Top, SectionName = "Sonarr")]
    public ConfigurationActionResult TestSonarr(ConfigurationActionContext<ShokarrConfiguration> context)
    {
        var options = ArrOptionsLoader.LoadAsync(context.PluginManager.GetRequiredService<SonarrClient>(), ToSonarrSettings()).GetAwaiter().GetResult();
        if (!options.Success)
        {
            context.Logger.LogWarning("Sonarr connection test failed: {Error}", options.ErrorMessage!.Replace("\r", "").Replace("\n", " "));
            return new($"Could not reach Sonarr: {options.ErrorMessage}", DisplayColorTheme.Warning);
        }

        ArrOptionsLoader.Apply(options.Data!, SonarrQualityProfile, SonarrRootFolder);
        return new(this);
    }

    [CustomAction(Theme = DisplayColorTheme.Primary, Position = DisplayButtonPosition.Top, SectionName = "Radarr")]
    public ConfigurationActionResult TestRadarr(ConfigurationActionContext<ShokarrConfiguration> context)
    {
        var options = ArrOptionsLoader.LoadAsync(context.PluginManager.GetRequiredService<RadarrClient>(), ToRadarrSettings()).GetAwaiter().GetResult();
        if (!options.Success)
        {
            context.Logger.LogWarning("Radarr connection test failed: {Error}", options.ErrorMessage!.Replace("\r", "").Replace("\n", " "));
            return new($"Could not reach Radarr: {options.ErrorMessage}", DisplayColorTheme.Warning);
        }

        ArrOptionsLoader.Apply(options.Data!, RadarrQualityProfile, RadarrRootFolder);
        return new(this);
    }
}
