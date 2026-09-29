using Shoko.Abstractions.Config.Components;
using Shokarr.Config;
using Shokarr.Models;

namespace Shokarr.Services;

public record ArrOptions(List<ArrQualityProfileResource> Profiles, List<ArrRootFolderResource> RootFolders);

public static class ArrOptionsLoader
{
    public static async Task<ArrActionResult<ArrOptions>> LoadAsync(ArrClientBase client, IArrSettings settings, CancellationToken ct = default)
    {
        var profiles = await client.GetQualityProfilesAsync(settings, ct).ConfigureAwait(false);
        if (!profiles.Success)
            return ArrActionResult<ArrOptions>.Fail(profiles.ErrorMessage!);

        var folders = await client.GetRootFoldersAsync(settings, ct).ConfigureAwait(false);
        if (!folders.Success)
            return ArrActionResult<ArrOptions>.Fail(folders.ErrorMessage!);

        return ArrActionResult<ArrOptions>.Ok(new ArrOptions(profiles.Data!, folders.Data!));
    }

    public static void Apply(ArrOptions options, SelectComponent<int> profile, SelectComponent<string> folder)
    {
        var selectedProfiles = profile.SelectedValues;
        var selectedFolders = folder.SelectedValues;
        profile.Options = [.. options.Profiles.Select(p => new SelectOption<int>(p.Id, p.Name, isSelected: selectedProfiles.Contains(p.Id)))];
        folder.Options = [.. options.RootFolders.Select(f => new SelectOption<string>(f.Path, f.Path, isSelected: selectedFolders.Contains(f.Path)))];
    }
}
