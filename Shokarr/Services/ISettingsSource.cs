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
    private const int MaxMigrationAttempts = 3;
    private readonly RetryOnceGate _migration = new();
    private int _migrationFailures;

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
        if (Volatile.Read(ref _migrationFailures) >= MaxMigrationAttempts)
            return;

        try
        {
            _migration.Run(() =>
            {
                var config = configurationService.Load<ShokarrConfiguration>(copy: true);
                if (LegacySettingsMigrator.TryMigrate(legacyStore.GetLegacySettings(), legacyStore.GetLegacyRadarrSettings(), config))
                    configurationService.Save(config);
            });
        }
        catch (Exception ex)
        {
            // The gate stays open on failure so a transient error is retried, up to a cap so a persistent one doesn't log on every settings read.
            Interlocked.Increment(ref _migrationFailures);
            s_logger.Warn(ex, "Shokarr: could not migrate legacy settings, continuing with native configuration as-is: {Error}", ex.Message.ForLog());
        }
    }
}
