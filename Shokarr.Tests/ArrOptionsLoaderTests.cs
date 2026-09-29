using Shoko.Abstractions.Config.Components;
using Shokarr.Services;
using Xunit;

namespace Shokarr.Tests;

public class ArrOptionsLoaderTests
{
    private static readonly ArrOptions Options = new(
        [new ArrQualityProfileResource(1, "Any"), new ArrQualityProfileResource(7, "HD")],
        [new ArrRootFolderResource(1, "/a"), new ArrRootFolderResource(2, "/b")]);

    [Fact]
    public void Apply_ReplacesOptionsAndKeepsPreviousSelection()
    {
        var profile = new SelectComponent<int>([new(7, "#7", isSelected: true)]);
        var folder = new SelectComponent<string>([new("/b", "/b", isSelected: true)]);

        ArrOptionsLoader.Apply(Options, profile, folder);

        Assert.Equal(2, profile.Options.Count);
        Assert.Equal([7], profile.SelectedValues);
        Assert.Equal("HD", profile.Options.Single(o => o.Value == 7).Label);
        Assert.Equal(["/b"], folder.SelectedValues);
    }

    [Fact]
    public void Apply_SelectionMissingFromNewOptions_LeavesNothingSelected()
    {
        var profile = new SelectComponent<int>([new(99, "#99", isSelected: true)]);

        ArrOptionsLoader.Apply(Options, profile, new SelectComponent<string>());

        Assert.False(profile.HasSelectedValue);
    }
}
