using Shoko.Abstractions.Plugin;
using Shoko.Abstractions.Plugin.Models;
using Shokarr.Config;

namespace Shokarr;

/// <summary>Plugin entry point and descriptor for Shoko Server. ID, name and description are generated from the project's Plugin* properties and manifest.json.</summary>
public partial class Plugin : IPlugin<ShokarrConfiguration>
{
    /// <inheritdoc/>
    public string? EmbeddedThumbnailResourceName => "Shokarr.assets.Thumbnail.png";

    /// <inheritdoc/>
    public string? EmbeddedIconResourceName => "Shokarr.assets.Icon.png";

    /// <inheritdoc/>
    public IReadOnlyList<PluginPage> GetPages() =>
        [new() { Name = "Missing Episodes", Url = $"{ShokarrConstants.BasePath}/dashboard" }];
}
