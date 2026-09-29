using System.Net.Http.Json;
using NLog;
using Shokarr.Config;

namespace Shokarr.Services;

/// <summary>Posts optional notifications to a Discord-compatible webhook. Never throws - a failed or unconfigured
/// webhook must never interrupt the scan/search flow that triggered the notification.</summary>
public class NotificationService(HttpClient httpClient)
{
    private static readonly Logger s_logger = LogManager.GetCurrentClassLogger();

    /// <summary>Posts <paramref name="message"/> to the configured webhook, if one is set. No-ops silently when <see cref="SonarrSettings.NotificationWebhookUrl"/> is unset.</summary>
    public async Task NotifyAsync(SonarrSettings settings, string message, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(settings.NotificationWebhookUrl))
            return;

        try
        {
            using var response = await httpClient.PostAsJsonAsync(settings.NotificationWebhookUrl, new { content = message }, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                s_logger.Warn("Shokarr: notification webhook returned {StatusCode}: {Message}", (int)response.StatusCode, SanitizeForLog(message));
        }
        catch (Exception ex)
        {
            s_logger.Warn(ex, "Shokarr: failed to post notification: {Message}", SanitizeForLog(message));
        }
    }

    private static string SanitizeForLog(string message) => message.Replace("\r", "").Replace("\n", " ");
}
