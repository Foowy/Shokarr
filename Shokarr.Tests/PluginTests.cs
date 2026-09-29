using Xunit;

namespace Shokarr.Tests;

public class PluginTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47];

    [Fact]
    public void EmbeddedIconResourceName_PointsAtAnEmbeddedPng() => AssertEmbeddedPng(new Plugin().EmbeddedIconResourceName);

    [Fact]
    public void EmbeddedThumbnailResourceName_PointsAtAnEmbeddedPng() => AssertEmbeddedPng(new Plugin().EmbeddedThumbnailResourceName);

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
