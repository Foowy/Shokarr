using Shoko.Abstractions.Config;
using Shoko.Abstractions.Plugin;
using Shokarr.Config;
using Xunit;

namespace Shokarr.Tests;

public class PluginTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47];

    [Fact]
    public void EmbeddedIconResourceName_PointsAtAnEmbeddedPng() => AssertEmbeddedPng(new Plugin().EmbeddedIconResourceName);

    [Fact]
    public void EmbeddedThumbnailResourceName_PointsAtAnEmbeddedPng() => AssertEmbeddedPng(new Plugin().EmbeddedThumbnailResourceName);

    [Fact]
    public void GeneratedIdentity_KeepsTheShippedPluginIdNameAndDescription()
    {
        var plugin = new Plugin();
        Assert.Equal(new Guid("8f2c1a4e-6b9d-4e3a-9c7f-2d5b8a1e6f3c"), plugin.ID);
        Assert.Equal(ShokarrConstants.Name, plugin.Name);
        Assert.False(string.IsNullOrWhiteSpace(plugin.Description));
    }

    [Fact]
    public void Plugin_DeclaresShokarrConfigurationAsItsMainConfiguration()
    {
        Assert.Contains(typeof(IPlugin<ShokarrConfiguration>), typeof(Plugin).GetInterfaces());
        Assert.True(typeof(IPluginConfiguration).IsAssignableFrom(typeof(ShokarrConfiguration)));
        Assert.True(typeof(IVisibleConfiguration).IsAssignableFrom(typeof(ShokarrConfiguration)));
    }

    private static void AssertEmbeddedPng(string? name)
    {
        Assert.NotNull(name);
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(name);
        Assert.NotNull(stream);
        var header = new byte[4];
        Assert.Equal(4, stream.Read(header, 0, 4));
        Assert.Equal(PngSignature, header);
    }
}
