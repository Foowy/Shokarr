using Microsoft.AspNetCore.Mvc;
using Shokarr.Config;
using Shokarr.Services;

namespace Shokarr.Controllers.Api;

/// <summary>Endpoints for reading/writing Radarr connection settings. Mirrors SettingsController's shape for the Sonarr equivalent.</summary>
public class RadarrSettingsController(ISettingsSource settingsSource, RadarrClient radarrClient) : ArrSettingsControllerBase<RadarrSettings, RadarrClient>(radarrClient)
{
    protected override RadarrSettings Stored() => settingsSource.GetRadarr();

    /// <summary>Gets the current Radarr settings, with the API key masked.</summary>
    [HttpGet]
    public IActionResult GetSettings()
    {
        var settings = settingsSource.GetRadarr();
        var masked = new RadarrSettings
        {
            BaseUrl = settings.BaseUrl,
            ApiKey = string.IsNullOrEmpty(settings.ApiKey) ? null : ShokarrConstants.SecretMask,
            QualityProfileId = settings.QualityProfileId,
            RootFolderPath = settings.RootFolderPath,
        };
        return Ok(new ApiResponse<RadarrSettings>(Success: true, Message: null, Data: masked));
    }

    /// <summary>Saves new Radarr settings. A blank API key, quality profile, or root folder preserves the previously-stored value (the key only when the URL is unchanged), same as SettingsController.SaveSettings.</summary>
    [HttpPut]
    public IActionResult SaveSettings([FromBody] RadarrSettings settings)
    {
        var stored = settingsSource.GetRadarr();
        var (apiKey, error) = ArrUrlRules.ResolveApiKey(settings, stored);
        if (error is not null)
            return Ok(new ApiResponse<object>(Success: false, Message: error, Data: null));
        settings.ApiKey = apiKey;
        if (settings.QualityProfileId is null)
            settings.QualityProfileId = stored.QualityProfileId;
        if (string.IsNullOrEmpty(settings.RootFolderPath))
            settings.RootFolderPath = stored.RootFolderPath;

        settingsSource.SaveRadarr(settings);
        return Ok(new ApiResponse<object>(Success: true, Message: null, Data: null));
    }

    /// <summary>Gets Radarr's quality profiles and root folders, for the dashboard's settings dropdowns.</summary>
    [HttpPost("radarr-options")]
    public Task<IActionResult> GetRadarrOptions([FromBody] RadarrSettings settings) => LoadOptions(settings);
}
