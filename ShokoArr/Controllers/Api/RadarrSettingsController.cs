using Microsoft.AspNetCore.Mvc;
using ShokoArr.Config;
using ShokoArr.Services;

namespace ShokoArr.Controllers.Api;

/// <summary>Endpoints for reading/writing Radarr connection settings. Mirrors SettingsController's shape for the Sonarr equivalent.</summary>
public class RadarrSettingsController(ISettingsSource settingsSource, RadarrClient radarrClient) : ShokoArrBaseController
{
    /// <summary>Gets the current Radarr settings, with the API key masked.</summary>
    [HttpGet]
    public IActionResult GetSettings()
    {
        var settings = settingsSource.GetRadarr();
        var masked = new RadarrSettings
        {
            BaseUrl = settings.BaseUrl,
            ApiKey = string.IsNullOrEmpty(settings.ApiKey) ? null : new string('*', 8),
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

    /// <summary>Tests connectivity to Radarr using the given (not-yet-saved) settings. A blank API key falls back to the stored one when the URL is unchanged.</summary>
    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection([FromBody] RadarrSettings settings)
    {
        var (apiKey, error) = ArrUrlRules.ResolveApiKey(settings, settingsSource.GetRadarr());
        if (error is not null)
            return Ok(new ApiResponse<object>(Success: false, Message: error, Data: null));
        settings.ApiKey = apiKey;

        var result = await radarrClient.TestConnectionAsync(settings);
        return Ok(new ApiResponse<object>(Success: result.Success, Message: result.ErrorMessage, Data: null));
    }

    /// <summary>Gets Radarr's quality profiles and root folders, for the dashboard's settings dropdowns.</summary>
    [HttpPost("radarr-options")]
    public async Task<IActionResult> GetRadarrOptions([FromBody] RadarrSettings settings)
    {
        var (apiKey, error) = ArrUrlRules.ResolveApiKey(settings, settingsSource.GetRadarr());
        if (error is not null)
            return Ok(new ApiResponse<object>(Success: false, Message: error, Data: null));
        settings.ApiKey = apiKey;

        var profiles = await radarrClient.GetQualityProfilesAsync(settings);
        if (!profiles.Success)
            return Ok(new ApiResponse<object>(Success: false, Message: profiles.ErrorMessage, Data: null));

        var rootFolders = await radarrClient.GetRootFoldersAsync(settings);
        if (!rootFolders.Success)
            return Ok(new ApiResponse<object>(Success: false, Message: rootFolders.ErrorMessage, Data: null));

        return Ok(new ApiResponse<object>(Success: true, Message: null, Data: new { qualityProfiles = profiles.Data, rootFolders = rootFolders.Data }));
    }

    /// <summary>Resolves the saved quality profile's display name from Radarr, so the dashboard can show it instead of a bare ID before the user re-tests the connection.</summary>
    /// <returns>200 with the profile's {id, name}, or success=false if no profile is saved or Radarr couldn't be reached.</returns>
    [HttpGet("quality-profile")]
    public async Task<IActionResult> GetSavedQualityProfile()
    {
        var settings = settingsSource.GetRadarr();
        if (settings.QualityProfileId is null)
            return Ok(new ApiResponse<object>(Success: false, Message: "No quality profile saved.", Data: null));

        var profiles = await radarrClient.GetQualityProfilesAsync(settings);
        if (!profiles.Success)
            return Ok(new ApiResponse<object>(Success: false, Message: profiles.ErrorMessage, Data: null));

        var match = profiles.Data!.FirstOrDefault(p => p.Id == settings.QualityProfileId);
        return match is null
            ? Ok(new ApiResponse<object>(Success: false, Message: "Saved quality profile no longer exists in Radarr.", Data: null))
            : Ok(new ApiResponse<object>(Success: true, Message: null, Data: match));
    }
}
