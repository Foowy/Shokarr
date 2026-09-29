using Shokarr.Config;

namespace Shokarr.Services;

public static class LegacySettingsMigrator
{
    public static bool TryMigrate(SonarrSettings sonarr, RadarrSettings radarr, ShokarrConfiguration target)
    {
        var configured = !string.IsNullOrEmpty(target.SonarrUrl) || !string.IsNullOrEmpty(target.SonarrApiKey)
            || !string.IsNullOrEmpty(target.RadarrUrl) || !string.IsNullOrEmpty(target.RadarrApiKey);
        if (configured || (string.IsNullOrEmpty(sonarr.BaseUrl) && string.IsNullOrEmpty(radarr.BaseUrl)))
            return false;

        target.SonarrUrl = sonarr.BaseUrl;
        target.SonarrApiKey = sonarr.ApiKey;
        target.SonarrQualityProfile = ShokarrConfiguration.ChooseProfile(target.SonarrQualityProfile, sonarr.QualityProfileId);
        target.SonarrRootFolder = ShokarrConfiguration.ChooseFolder(target.SonarrRootFolder, sonarr.RootFolderPath);
        target.ScanIntervalHours = Math.Clamp(sonarr.ScanIntervalHours, 0, 720);
        target.IncludeSpecials = sonarr.IncludeSpecials;
        target.HideUnaired = sonarr.HideUnaired;
        target.NotificationWebhookUrl = sonarr.NotificationWebhookUrl;
        target.RadarrUrl = radarr.BaseUrl;
        target.RadarrApiKey = radarr.ApiKey;
        target.RadarrQualityProfile = ShokarrConfiguration.ChooseProfile(target.RadarrQualityProfile, radarr.QualityProfileId);
        target.RadarrRootFolder = ShokarrConfiguration.ChooseFolder(target.RadarrRootFolder, radarr.RootFolderPath);
        return true;
    }

}
