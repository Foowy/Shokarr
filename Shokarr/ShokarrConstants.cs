namespace Shokarr;

/// <summary>Centralized constants for plugin identity, routing, and storage.</summary>
public static class ShokarrConstants
{
    /// <summary>Display name of the plugin.</summary>
    public const string Name = "Shokarr";

    /// <summary>Description of the plugin.</summary>
    public const string Description = "Scans your Shoko collection for missing episodes and bridges them to Sonarr for automated download, with related-series discovery routed to Sonarr or Radarr.";

    /// <summary>Current version string.</summary>
    /// <summary>API version used for versioning attributes and Swagger doc grouping.</summary>
    public const string ApiVersion = "1.0";

    /// <summary>Unique plugin ID.</summary>
    public const string PluginId = "8f2c1a4e-6b9d-4e3a-9c7f-2d5b8a1e6f3c";

    /// <summary>Base HTTP path for plugin endpoints (dashboard + API).</summary>
    public const string BasePath = "/api/plugin/Shokarr";

    /// <summary>Subfolder name under the host's data directory for this plugin's LiteDB file and settings. Not
    /// "shokarr": on case-insensitive filesystems that is the plugin's own install folder for manual deploys, which an uninstall deletes.</summary>
    public const string PluginDataSubfolder = "shokarr_data";

    /// <summary>Filename of the plugin's LiteDB database.</summary>
    public const string LiteDbFileName = "shokarr.db";
}
