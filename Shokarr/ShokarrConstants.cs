namespace Shokarr;

/// <summary>Centralized constants for plugin identity, routing, and storage.</summary>
public static class ShokarrConstants
{
    /// <summary>Display name of the plugin.</summary>
    public const string Name = "Shokarr";

    /// <summary>API version used for versioning attributes and Swagger doc grouping.</summary>
    public const string ApiVersion = "1.0";

    /// <summary>Base HTTP path for plugin endpoints (dashboard + API).</summary>
    public const string BasePath = "/api/plugin/Shokarr";

    /// <summary>Subfolder name under the host's data directory for this plugin's LiteDB file and settings. Not
    /// "shokarr": on case-insensitive filesystems that is the plugin's own install folder for manual deploys, which an uninstall deletes.</summary>
    public const string PluginDataSubfolder = "shokarr_data";

    /// <summary>Filename of the plugin's LiteDB database.</summary>
    public const string LiteDbFileName = "shokarr.db";

    /// <summary>Placeholder returned in place of a stored secret.</summary>
    public const string SecretMask = "********";
}
