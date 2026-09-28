namespace ShokoArr.Config;

/// <summary>Validation shared by the Sonarr and Radarr settings endpoints.</summary>
public static class ArrUrlRules
{
    public const string InvalidUrlMessage = "URL must start with http:// or https://.";

    public const string KeyRequiredMessage = "Enter the API key again when changing the URL.";

    /// <summary>Whether <paramref name="url"/> is an absolute http or https URL.</summary>
    public static bool IsHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// Resolves the API key to use for <paramref name="incoming"/>'s URL. A blank key falls back to the stored one only
    /// when the URL is the stored URL, so a caller can't send the stored key to a host of their choosing.
    /// </summary>
    /// <returns>The key to use, or an error message when the request can't be honoured.</returns>
    public static (string? Key, string? Error) ResolveApiKey(IArrSettings incoming, IArrSettings stored)
    {
        if (string.IsNullOrEmpty(incoming.BaseUrl))
            return (incoming.ApiKey, null);
        if (!IsHttpUrl(incoming.BaseUrl))
            return (null, InvalidUrlMessage);
        if (!string.IsNullOrEmpty(incoming.ApiKey))
            return (incoming.ApiKey, null);
        if (!string.Equals(incoming.BaseUrl.TrimEnd('/'), stored.BaseUrl?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            return (null, KeyRequiredMessage);

        return (stored.ApiKey, null);
    }
}
