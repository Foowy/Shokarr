using Shoko.Abstractions.Plugin;
using Shoko.Abstractions.Plugin.Models;

namespace Shokarr;

/// <summary>Plugin entry point and descriptor for Shoko Server.</summary>
public class Plugin : IPlugin
{
    /// <inheritdoc/>
    public Guid ID => new(ShokarrConstants.PluginId);

    /// <inheritdoc/>
    public string Name => ShokarrConstants.Name;

    /// <inheritdoc/>
    public string? Description => ShokarrConstants.Description;

    /// <inheritdoc/>
    public string? EmbeddedThumbnailResourceName => "Shokarr.assets.Thumbnail.png";

    /// <inheritdoc/>
    public string? EmbeddedIconResourceName => "Shokarr.assets.Icon.png";

    /// <inheritdoc/>
    public IReadOnlyList<PluginPage> GetPages() =>
        [new() { Name = "Missing Episodes", Url = $"{ShokarrConstants.BasePath}/dashboard" }];
}
