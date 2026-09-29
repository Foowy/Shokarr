namespace Shokarr.Config;

/// <summary>The connection details shared by Sonarr and Radarr settings.</summary>
public interface IArrSettings
{
    string? BaseUrl { get; }

    string? ApiKey { get; set; }

    int? QualityProfileId { get; }

    string? RootFolderPath { get; }
}
