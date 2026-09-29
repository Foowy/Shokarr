using NLog;
using Shoko.Abstractions.Config.Services;
using Shokarr.Config;

namespace Shokarr.Services;

public interface ISettingsSource
{
    SonarrSettings GetSonarr();

    RadarrSettings GetRadarr();

    void SaveSonarr(SonarrSettings settings);

    void SaveRadarr(RadarrSettings settings);
}

public class NativeSettingsSource(IConfigurationService configurationService, ScanCacheStore legacyStore) : ISettingsSource
{
    private static readonly Logger s_logger = LogManager.GetCurrentClassLogger();
    private readonly RetryOnceGate _migration = new();

    public SonarrSettings GetSonarr() => Load().ToSonarrSettings();

    public RadarrSettings GetRadarr() => Load().ToRadarrSettings();

    public void SaveSonarr(SonarrSettings settings) => Save(c => c.ApplySonarr(settings));

    public void SaveRadarr(RadarrSettings settings) => Save(c => c.ApplyRadarr(settings));

    private void Save(Action<ShokarrConfiguration> apply)
    {
        EnsureMigrated();
        var config = configurationService.Load<ShokarrConfiguration>(copy: true);
        apply(config);
        configurationService.Save(config);
    }

    private ShokarrConfiguration Load()
    {
        EnsureMigrated();
        return configurationService.Load<ShokarrConfiguration>();
    }

    private void EnsureMigrated()
    {
        _migration.Run(() =>
        {
            try
            {
                var config = configurationService.Load<ShokarrConfiguration>(copy: true);
                if (LegacySettingsMigrator.TryMigrate(legacyStore.GetLegacySettings(), legacyStore.GetLegacyRadarrSettings(), config))
                    configurationService.Save(config);
            }
            catch (Exception ex)
            {
                s_logger.Warn(ex, "Shokarr: could not migrate legacy settings, continuing with native configuration as-is: {Error}", ex.Message.Replace("\r", "").Replace("\n", " "));
            }
        });
    }
}
