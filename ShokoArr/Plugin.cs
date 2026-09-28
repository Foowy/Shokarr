using Shoko.Abstractions.Plugin;
using Shoko.Abstractions.Plugin.Models;

namespace ShokoArr;

/// <summary>Plugin entry point and descriptor for Shoko Server.</summary>
public class Plugin : IPlugin
{
    /// <inheritdoc/>
    public Guid ID => new(ShokoArrConstants.PluginId);

    /// <inheritdoc/>
    public string Name => ShokoArrConstants.Name;

    /// <inheritdoc/>
    public string? Description => ShokoArrConstants.Description;

    /// <inheritdoc/>
    public string? EmbeddedThumbnailResourceName => "ShokoArr.assets.Thumbnail.png";

    /// <inheritdoc/>
    public string? EmbeddedIconResourceName => "ShokoArr.assets.Icon.png";

    /// <inheritdoc/>
    public IReadOnlyList<PluginPage> GetPages() =>
        [new() { Name = "Missing Episodes", Url = $"{ShokoArrConstants.BasePath}/dashboard" }];
}
