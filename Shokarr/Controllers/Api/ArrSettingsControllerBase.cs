using Microsoft.AspNetCore.Mvc;
using Shokarr.Config;
using Shokarr.Services;

namespace Shokarr.Controllers.Api;

/// <summary>Connection-test, saved-profile and options endpoints shared by the Sonarr and Radarr settings controllers.</summary>
public abstract class ArrSettingsControllerBase<TSettings, TClient>(TClient client) : ShokarrBaseController
    where TSettings : IArrSettings
    where TClient : ArrClientBase
{
    private static string ServiceName => typeof(TClient).Name.Replace("Client", "");

    /// <summary>The typed client for this service.</summary>
    protected TClient Client => client;

    /// <summary>Gets the currently stored settings.</summary>
    protected abstract TSettings Stored();

    /// <summary>Tests connectivity using the given (not-yet-saved) settings. A blank API key falls back to the stored one when the URL is unchanged.</summary>
    /// <param name="settings">The settings to test.</param>
    /// <returns>200 with success=true if reachable, success=false with an error message otherwise.</returns>
    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection([FromBody] TSettings settings)
    {
        var error = ResolveKey(settings);
        if (error is not null)
            return Failure(error);

        var result = await client.TestConnectionAsync(settings);
        return Ok(new ApiResponse<object>(Success: result.Success, Message: result.ErrorMessage, Data: null));
    }

    /// <summary>Resolves the saved quality profile's display name, so the dashboard can show it instead of a bare ID before the user re-tests the connection.</summary>
    /// <returns>200 with the profile's {id, name}, or success=false if no profile is saved or the service couldn't be reached.</returns>
    [HttpGet("quality-profile")]
    public async Task<IActionResult> GetSavedQualityProfile()
    {
        var settings = Stored();
        if (settings.QualityProfileId is null)
            return Failure("No quality profile saved.");

        var profiles = await client.GetQualityProfilesAsync(settings);
        if (!profiles.Success)
            return Failure(profiles.ErrorMessage);

        var match = profiles.Data!.FirstOrDefault(p => p.Id == settings.QualityProfileId);
        return match is null
            ? Failure($"Saved quality profile no longer exists in {ServiceName}.")
            : Ok(new ApiResponse<object>(Success: true, Message: null, Data: match));
    }

    /// <summary>Gets the quality profiles and root folders, for the dashboard's settings dropdowns.</summary>
    /// <param name="settings">The settings to use for the lookup (not necessarily saved yet).</param>
    /// <returns>200 with the available quality profiles and root folders, or success=false with an error message.</returns>
    protected async Task<IActionResult> LoadOptions(TSettings settings)
    {
        var error = ResolveKey(settings);
        if (error is not null)
            return Failure(error);

        var profiles = await client.GetQualityProfilesAsync(settings);
        if (!profiles.Success)
            return Failure(profiles.ErrorMessage);

        var rootFolders = await client.GetRootFoldersAsync(settings);
        if (!rootFolders.Success)
            return Failure(rootFolders.ErrorMessage);

        return Ok(new ApiResponse<object>(Success: true, Message: null, Data: new { qualityProfiles = profiles.Data, rootFolders = rootFolders.Data }));
    }

    private string? ResolveKey(TSettings settings)
    {
        var (apiKey, error) = ArrUrlRules.ResolveApiKey(settings, Stored());
        if (error is null)
            settings.ApiKey = apiKey;
        return error;
    }

    private OkObjectResult Failure(string? message) => Ok(new ApiResponse<object>(Success: false, Message: message, Data: null));
}
