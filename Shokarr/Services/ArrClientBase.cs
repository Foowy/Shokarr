using System.Net.Http.Json;
using System.Text.Json;
using Shokarr.Config;
using Shokarr.Models;

namespace Shokarr.Services;

/// <summary>Shared request-building and error-handling logic for typed *arr-family (Sonarr/Radarr) v3 API clients. Never throws on HTTP/connectivity failure - all calls return a typed result.</summary>
public abstract class ArrClientBase(HttpClient httpClient)
{
    private protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private string ServiceName => GetType().Name.Replace("Client", "");

    /// <summary>Validates connectivity and API key against the system status endpoint.</summary>
    public async Task<ArrActionResult<bool>> TestConnectionAsync(IArrSettings settings, CancellationToken ct = default)
    {
        var status = await SendAsync<JsonElement>(BuildRequest(HttpMethod.Get, settings, "/api/v3/system/status"), ct).ConfigureAwait(false);
        if (!status.Success)
            return ArrActionResult<bool>.Fail(status.ErrorMessage!);

        // A login proxy in front of the service answers 200 with HTML, which fails to parse above; any other non-object body isn't a status response either.
        return status.Data.ValueKind == JsonValueKind.Object
            ? ArrActionResult<bool>.Ok(true)
            : ArrActionResult<bool>.Fail($"{ServiceName} returned an unexpected status response");
    }

    /// <summary>Gets the configured quality profiles, for the settings dropdown.</summary>
    public Task<ArrActionResult<List<ArrQualityProfileResource>>> GetQualityProfilesAsync(IArrSettings settings, CancellationToken ct = default) =>
        SendAsync<List<ArrQualityProfileResource>>(BuildRequest(HttpMethod.Get, settings, "/api/v3/qualityprofile"), ct);

    /// <summary>Gets the configured root folders, for the settings dropdown.</summary>
    public Task<ArrActionResult<List<ArrRootFolderResource>>> GetRootFoldersAsync(IArrSettings settings, CancellationToken ct = default) =>
        SendAsync<List<ArrRootFolderResource>>(BuildRequest(HttpMethod.Get, settings, "/api/v3/rootfolder"), ct);

    private protected static HttpRequestMessage BuildRequest(HttpMethod method, IArrSettings settings, string path)
    {
        // Nothing here may throw: callers build the request before SendAsync's try/catch, so a malformed URL or key would escape as an exception
        // (and a scan that keeps failing is retried by the scheduler every minute). A null, blank or unparseable URL becomes a relative URI that
        // fails inside SendAsync instead, and TryAddWithoutValidation defers header validation to send time.
        var url = $"{settings.BaseUrl?.Trim().TrimEnd('/') ?? string.Empty}{path}";
        var request = new HttpRequestMessage(method, Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri) ? uri : new Uri(string.Empty, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("X-Api-Key", settings.ApiKey);
        return request;
    }

    private protected Task<ArrActionResult<T>> SendAsync<T>(HttpRequestMessage request, CancellationToken ct) =>
        SendAsync(request, content => content.ReadFromJsonAsync<T>(JsonOptions, ct), ct);

    /// <summary>For calls whose response body is ignored (monitor, command, status).</summary>
    private protected async Task<ArrActionResult<bool>> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        await SendAsync(request, _ => Task.FromResult(true), ct).ConfigureAwait(false);

    private async Task<ArrActionResult<T>> SendAsync<T>(HttpRequestMessage request, Func<HttpContent, Task<T?>> readBody, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await ReadErrorDetailAsync(response.Content, ct).ConfigureAwait(false);
                return ArrActionResult<T>.Fail($"{ServiceName} returned {(int)response.StatusCode} {response.ReasonPhrase}" + (detail is null ? string.Empty : $": {detail}"));
            }

            var data = await readBody(response.Content).ConfigureAwait(false);
            return data is null ? ArrActionResult<T>.Fail($"{ServiceName} returned an empty response body") : ArrActionResult<T>.Ok(data);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller cancellation (scan aborted / request dropped) must propagate. A 30s HttpClient
            // timeout also lands here as a TaskCanceledException but with ct not signalled -- that
            // one is a genuine call failure, so it falls through to the Fail below.
            throw;
        }
        catch (JsonException)
        {
            return ArrActionResult<T>.Fail($"{ServiceName} returned a response that wasn't valid JSON - the URL may point at a login page or proxy");
        }
        catch (Exception ex)
        {
            return ArrActionResult<T>.Fail(ex.Message);
        }
    }

    /// <summary>The validation message from an *arr error body (<c>[{"errorMessage":...}]</c> or <c>{"message":...}</c>), flattened to one short line, or null when there isn't one.</summary>
    private static async Task<string?> ReadErrorDetailAsync(HttpContent content, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var node = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0 ? doc.RootElement[0] : doc.RootElement;
            if (node.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var name in new[] { "errorMessage", "message" })
                if (node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text)
                {
                    var oneLine = new string([.. text.Select(c => char.IsControl(c) ? ' ' : c)]).Trim();
                    return oneLine.Length > 200 ? oneLine[..200] : oneLine;
                }
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or InvalidOperationException)
        {
            // An unreadable error body just means no extra detail.
        }

        return null;
    }
}
