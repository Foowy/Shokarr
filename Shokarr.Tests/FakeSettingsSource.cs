using Shokarr.Config;
using Shokarr.Services;

namespace Shokarr.Tests;

public class FakeSettingsSource : ISettingsSource
{
    public SonarrSettings Sonarr { get; set; } = new();

    public RadarrSettings Radarr { get; set; } = new();

    public SonarrSettings GetSonarr() => Sonarr;

    public RadarrSettings GetRadarr() => Radarr;

    public void SaveSonarr(SonarrSettings settings) => Sonarr = settings;

    public void SaveRadarr(RadarrSettings settings) => Radarr = settings;
}
