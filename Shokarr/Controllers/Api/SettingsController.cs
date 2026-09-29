using Microsoft.AspNetCore.Mvc;
using Shokarr.Config;
using Shokarr.Services;

namespace Shokarr.Controllers.Api;

/// <summary>Endpoints for reading/writing Sonarr connection settings.</summary>
public class SettingsController(ISettingsSource settingsSource, SonarrClient sonarrClient) : ArrSettingsControllerBase<SonarrSettings, SonarrClient>(sonarrClient)
{
    protected override SonarrSettings Stored() => settingsSource.GetSonarr();

    /// <summary>Gets the current Sonarr settings, with the API key masked.</summary>
    /// <returns>The current settings, API key redacted.</returns>
    [HttpGet]
    public IActionResult GetSettings()
    {
        var settings = settingsSource.GetSonarr();
        var masked = new SonarrSettings
        {
            BaseUrl = settings.BaseUrl,
            ApiKey = string.IsNullOrEmpty(settings.ApiKey) ? null : ShokarrConstants.SecretMask,
            ScanIntervalHours = settings.ScanIntervalHours,
            QualityProfileId = settings.QualityProfileId,
            RootFolderPath = settings.RootFolderPath,
            IncludeSpecials = settings.IncludeSpecials,
            HideUnaired = settings.HideUnaired,
            CountSonarrHeldAsMissing = settings.CountSonarrHeldAsMissing,
            NotificationWebhookUrl = string.IsNullOrEmpty(settings.NotificationWebhookUrl) ? null : ShokarrConstants.SecretMask,
        };
        return Ok(new ApiResponse<SonarrSettings>(Success: true, Message: null, Data: masked));
    }

    /// <summary>Saves new Sonarr settings. If the incoming API key, quality profile, or root folder is blank/unset
    /// (e.g. the dashboard re-saved without re-testing the connection, which is the only way those dropdowns get
    /// populated), the previously-stored value is kept instead of being wiped. The stored API key is only kept when
    /// the URL is unchanged.</summary>
    /// <param name="settings">The settings to save.</param>
    /// <returns>200 on success.</returns>
    [HttpPut]
    public IActionResult SaveSettings([FromBody] SonarrSettings settings)
    {
        var stored = settingsSource.GetSonarr();
        var (apiKey, error) = ArrUrlRules.ResolveApiKey(settings, stored);
        if (error is null && !string.IsNullOrEmpty(settings.NotificationWebhookUrl) && !ArrUrlRules.IsHttpUrl(settings.NotificationWebhookUrl))
            error = "Webhook " + ArrUrlRules.InvalidUrlMessage;
        if (error is not null)
            return Ok(new ApiResponse<object>(Success: false, Message: error, Data: null));
        settings.ApiKey = apiKey;
        if (settings.QualityProfileId is null)
            settings.QualityProfileId = stored.QualityProfileId;
        if (string.IsNullOrEmpty(settings.RootFolderPath))
            settings.RootFolderPath = stored.RootFolderPath;
        if (string.IsNullOrEmpty(settings.NotificationWebhookUrl))
            settings.NotificationWebhookUrl = stored.NotificationWebhookUrl;

        settingsSource.SaveSonarr(settings);
        return Ok(new ApiResponse<object>(Success: true, Message: null, Data: null));
    }

    /// <summary>Pings Sonarr using the currently stored settings, for the dashboard's persistent header indicator.</summary>
    /// <returns>200 with success=true if reachable, success=false with an error message otherwise.</returns>
    [HttpGet("health")]
    public async Task<IActionResult> GetHealth()
    {
        var result = await Client.TestConnectionAsync(Stored());
        return Ok(new ApiResponse<object>(Success: result.Success, Message: result.ErrorMessage, Data: null));
    }

    /// <summary>Gets Sonarr's quality profiles and root folders, for the dashboard's settings dropdowns.</summary>
    /// <param name="settings">The settings to use for the lookup (not necessarily saved yet).</param>
    /// <returns>200 with the available quality profiles and root folders, or success=false with an error message.</returns>
    [HttpPost("sonarr-options")]
    public Task<IActionResult> GetSonarrOptions([FromBody] SonarrSettings settings) => LoadOptions(settings);
}
