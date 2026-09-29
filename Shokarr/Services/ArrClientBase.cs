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
    public Task<ArrActionResult<bool>> TestConnectionAsync(IArrSettings settings, CancellationToken ct = default) =>
        SendAsync(BuildRequest(HttpMethod.Get, settings, "/api/v3/system/status"), ct);

    /// <summary>Gets the configured quality profiles, for the settings dropdown.</summary>
    public Task<ArrActionResult<List<ArrQualityProfileResource>>> GetQualityProfilesAsync(IArrSettings settings, CancellationToken ct = default) =>
        SendAsync<List<ArrQualityProfileResource>>(BuildRequest(HttpMethod.Get, settings, "/api/v3/qualityprofile"), ct);

    /// <summary>Gets the configured root folders, for the settings dropdown.</summary>
    public Task<ArrActionResult<List<ArrRootFolderResource>>> GetRootFoldersAsync(IArrSettings settings, CancellationToken ct = default) =>
        SendAsync<List<ArrRootFolderResource>>(BuildRequest(HttpMethod.Get, settings, "/api/v3/rootfolder"), ct);

    private protected static HttpRequestMessage BuildRequest(HttpMethod method, IArrSettings settings, string path)
    {
        // A null/blank BaseUrl produces a relative URI here rather than throwing, so the failure surfaces inside SendAsync's try/catch instead of crashing the caller.
        var request = new HttpRequestMessage(method, $"{settings.BaseUrl?.TrimEnd('/') ?? string.Empty}{path}");
        request.Headers.Add("X-Api-Key", settings.ApiKey);
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
                return ArrActionResult<T>.Fail($"{ServiceName} returned {(int)response.StatusCode} {response.ReasonPhrase}");

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
        catch (Exception ex)
        {
            return ArrActionResult<T>.Fail(ex.Message);
        }
    }
}
