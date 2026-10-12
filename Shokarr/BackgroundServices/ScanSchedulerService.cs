using Microsoft.Extensions.Hosting;
using NLog;
using Shoko.Abstractions.Core.Services;
using Shokarr.Services;

namespace Shokarr.BackgroundServices;

/// <summary>Runs the missing-episode scan on a configurable interval, in addition to the on-demand dashboard trigger.</summary>
public class ScanSchedulerService(ISystemService systemService, MissingEpisodeScanner scanner, ScanCacheStore cacheStore, ISettingsSource settingsSource) : BackgroundService
{
    private static readonly Logger s_logger = LogManager.GetCurrentClassLogger();

    /// <summary>Longest the loop sleeps before re-reading the interval setting, so a changed interval takes effect within this window.</summary>
    private static readonly TimeSpan s_maxSleep = TimeSpan.FromHours(1);

    /// <summary>How long to sleep until the next due check: at most an hour, and a minute when already overdue.</summary>
    /// <param name="dueAt">When the next scan is due.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <returns>The delay before the loop re-evaluates.</returns>
    public static TimeSpan NextDelay(DateTime dueAt, DateTime nowUtc)
    {
        var delay = dueAt - nowUtc;
        return delay <= TimeSpan.Zero ? TimeSpan.FromMinutes(1) : delay > s_maxSleep ? s_maxSleep : delay;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        s_logger.Info("Shokarr scan scheduler waiting for Shoko Server to reach 'Started' state...");
        while (!systemService.IsStarted && !stoppingToken.IsCancellationRequested)
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
        if (stoppingToken.IsCancellationRequested)
            return;

        s_logger.Info("Shokarr scan scheduler started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var intervalHours = settingsSource.GetSonarr().ScanIntervalHours;
                if (intervalHours <= 0)
                {
                    // Scheduled scanning disabled - re-check hourly in case the user re-enables it.
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var lastScan = cacheStore.GetLastScan();
                var dueAt = (lastScan?.ScannedAtUtc ?? DateTime.MinValue).AddHours(intervalHours);
                if (DateTime.UtcNow >= dueAt)
                {
                    s_logger.Info("Shokarr: running scheduled missing-episode scan.");
                    await scanner.ScanAsync(stoppingToken).ConfigureAwait(false);
                    dueAt = DateTime.UtcNow.AddHours(intervalHours);
                }

                await Task.Delay(NextDelay(dueAt, DateTime.UtcNow), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                s_logger.Warn(ex, "Shokarr: scheduled scan loop error, retrying in 1 minute.");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
