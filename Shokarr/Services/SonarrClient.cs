using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Shokarr.Config;
using Shokarr.Models;

namespace Shokarr.Services;

/// <summary>Sonarr episode resource, as returned by Sonarr's v3 API.</summary>
public record SonarrEpisodeResource(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("seasonNumber")] int SeasonNumber,
    [property: JsonPropertyName("episodeNumber")] int EpisodeNumber,
    [property: JsonPropertyName("absoluteEpisodeNumber")] int? AbsoluteEpisodeNumber = null,
    [property: JsonPropertyName("title")] string? Title = null,
    [property: JsonPropertyName("airDate")] string? AirDate = null,
    [property: JsonPropertyName("hasFile")] bool HasFile = false);

/// <summary>Sonarr quality profile resource, as returned by Sonarr's v3 API.</summary>
public record ArrQualityProfileResource(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name);

/// <summary>Sonarr root folder resource, as returned by Sonarr's v3 API.</summary>
public record ArrRootFolderResource(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("path")] string Path);

/// <summary>Minimal Sonarr series resource, used only to detect whether a series already exists.</summary>
public record SonarrSeriesResource([property: JsonPropertyName("id")] int Id, [property: JsonPropertyName("titleSlug")] string? TitleSlug);

/// <summary>Sonarr tag resource, as returned by Sonarr's v3 API.</summary>
public record SonarrTagResource([property: JsonPropertyName("id")] int Id, [property: JsonPropertyName("label")] string Label);

/// <summary>Typed HTTP client for Sonarr's v3 API. Never throws on HTTP/connectivity failure - all calls return a typed result.</summary>
public class SonarrClient(HttpClient httpClient) : ArrClientBase(httpClient)
{
    /// <summary>Looks up Sonarr series candidates by TVDB ID.</summary>
    public Task<ArrActionResult<List<SonarrSeriesLookupResult>>> LookupByTvdbIdAsync(SonarrSettings settings, int tvdbId, CancellationToken ct = default) =>
        SendAsync<List<SonarrSeriesLookupResult>>(BuildRequest(HttpMethod.Get, settings, $"/api/v3/series/lookup?term={Uri.EscapeDataString($"tvdb:{tvdbId}")}"), ct);

    /// <summary>Looks up Sonarr series candidates by free-text title, for series with no TMDB-linked TVDB ID.</summary>
    public Task<ArrActionResult<List<SonarrSeriesLookupResult>>> LookupByTitleAsync(SonarrSettings settings, string title, CancellationToken ct = default) =>
        SendAsync<List<SonarrSeriesLookupResult>>(BuildRequest(HttpMethod.Get, settings, $"/api/v3/series/lookup?term={Uri.EscapeDataString(title)}"), ct);

    /// <summary>Adds an anime series to Sonarr, unmonitored with no search by default; the owned-series flow then monitors only its missing episodes. The discovery flow passes monitorMode "all" and searchOnAdd true.</summary>
    public async Task<ArrActionResult<SonarrSeriesResource>> AddSeriesAsync(SonarrSettings settings, int tvdbId, string title, int qualityProfileId, string rootFolderPath, string monitorMode = "none", bool searchOnAdd = false, List<int>? tagIds = null, CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Post, settings, "/api/v3/series");
        request.Content = JsonContent.Create(new
        {
            tvdbId,
            title,
            qualityProfileId,
            rootFolderPath,
            monitored = true,
            seriesType = "anime",
            tags = tagIds ?? [],
            addOptions = new { monitor = monitorMode, searchForMissingEpisodes = searchOnAdd },
        }, options: JsonOptions);

        var result = await SendAsync<JsonElement>(request, ct).ConfigureAwait(false);
        if (!result.Success)
            return ArrActionResult<SonarrSeriesResource>.Fail(result.ErrorMessage!);

        if (!result.Data.TryGetProperty("id", out var idProp))
            return ArrActionResult<SonarrSeriesResource>.Fail("Sonarr's add-series response did not contain an id.");

        var titleSlug = result.Data.TryGetProperty("titleSlug", out var slugProp) ? slugProp.GetString() : null;
        return ArrActionResult<SonarrSeriesResource>.Ok(new SonarrSeriesResource(idProp.GetInt32(), titleSlug));
    }

    /// <summary>Looks up a series already added to Sonarr by TVDB ID (as opposed to <see cref="LookupByTvdbIdAsync"/>, which searches TheTVDB regardless of whether it's already added).</summary>
    public Task<ArrActionResult<List<SonarrSeriesResource>>> GetExistingSeriesByTvdbIdAsync(SonarrSettings settings, int tvdbId, CancellationToken ct = default) =>
        SendAsync<List<SonarrSeriesResource>>(BuildRequest(HttpMethod.Get, settings, $"/api/v3/series?tvdbId={tvdbId}"), ct);

    /// <summary>Gets all of Sonarr's configured tags.</summary>
    public Task<ArrActionResult<List<SonarrTagResource>>> GetTagsAsync(SonarrSettings settings, CancellationToken ct = default) =>
        SendAsync<List<SonarrTagResource>>(BuildRequest(HttpMethod.Get, settings, "/api/v3/tag"), ct);

    /// <summary>Creates a new Sonarr tag with the given label.</summary>
    public async Task<ArrActionResult<SonarrTagResource>> CreateTagAsync(SonarrSettings settings, string label, CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Post, settings, "/api/v3/tag");
        request.Content = JsonContent.Create(new { label }, options: JsonOptions);
        return await SendAsync<SonarrTagResource>(request, ct).ConfigureAwait(false);
    }

    /// <summary>Finds an existing tag matching the label (case-insensitive), or creates one if none exists.</summary>
    public async Task<ArrActionResult<int>> EnsureTagIdAsync(SonarrSettings settings, string label, CancellationToken ct = default)
    {
        var existing = await GetTagsAsync(settings, ct).ConfigureAwait(false);
        if (!existing.Success)
            return ArrActionResult<int>.Fail(existing.ErrorMessage!);

        var match = existing.Data!.FirstOrDefault(t => string.Equals(t.Label, label, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            return ArrActionResult<int>.Ok(match.Id);

        var created = await CreateTagAsync(settings, label, ct).ConfigureAwait(false);
        return created.Success ? ArrActionResult<int>.Ok(created.Data!.Id) : ArrActionResult<int>.Fail(created.ErrorMessage!);
    }

    /// <summary>Adds a tag to a Sonarr series if missing. Sonarr needs a full-resource PUT, so the fetched JSON node is edited and sent back whole.</summary>
    public async Task<ArrActionResult<bool>> UpdateSeriesTagAsync(SonarrSettings settings, int sonarrSeriesId, int tagId, CancellationToken ct = default)
    {
        var getResult = await SendAsync<JsonNode>(BuildRequest(HttpMethod.Get, settings, $"/api/v3/series/{sonarrSeriesId}"), ct).ConfigureAwait(false);
        if (!getResult.Success)
            return ArrActionResult<bool>.Fail(getResult.ErrorMessage!);

        var series = getResult.Data!;
        var tags = series["tags"]?.AsArray() ?? [];
        if (!tags.Any(t => t!.GetValue<int>() == tagId))
            tags.Add(tagId);
        series["tags"] = tags;

        var putRequest = BuildRequest(HttpMethod.Put, settings, $"/api/v3/series/{sonarrSeriesId}");
        putRequest.Content = JsonContent.Create(series, options: JsonOptions);
        return await SendAsync(putRequest, ct).ConfigureAwait(false);
    }

    /// <summary>Gets all episodes for a Sonarr series (used to map AniDB episode numbers to Sonarr episode IDs).</summary>
    public Task<ArrActionResult<List<SonarrEpisodeResource>>> GetEpisodesAsync(SonarrSettings settings, int sonarrSeriesId, CancellationToken ct = default) =>
        SendAsync<List<SonarrEpisodeResource>>(BuildRequest(HttpMethod.Get, settings, $"/api/v3/episode?seriesId={sonarrSeriesId}"), ct);

    /// <summary>Gets the Sonarr episode IDs currently in the download queue, paging until a short page.</summary>
    public async Task<ArrActionResult<HashSet<int>>> GetQueuedEpisodeIdsAsync(SonarrSettings settings, CancellationToken ct = default)
    {
        const int PageSize = 1000;
        var ids = new HashSet<int>();
        for (var page = 1; ; page++)
        {
            var result = await SendAsync<JsonElement>(BuildRequest(HttpMethod.Get, settings, $"/api/v3/queue?page={page}&pageSize={PageSize}"), ct).ConfigureAwait(false);
            if (!result.Success)
                return ArrActionResult<HashSet<int>>.Fail(result.ErrorMessage!);

            var count = 0;
            if (result.Data.ValueKind == JsonValueKind.Object && result.Data.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array)
                foreach (var record in records.EnumerateArray())
                {
                    count++;
                    if (record.ValueKind == JsonValueKind.Object && record.TryGetProperty("episodeId", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var value))
                        ids.Add(value);
                }

            if (count < PageSize)
                return ArrActionResult<HashSet<int>>.Ok(ids);
        }
    }

    /// <summary>Sets the given episodes to monitored, without touching any other episode's monitored state.</summary>
    public Task<ArrActionResult<bool>> MonitorEpisodesAsync(SonarrSettings settings, List<int> sonarrEpisodeIds, CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Put, settings, "/api/v3/episode/monitor");
        request.Content = JsonContent.Create(new { episodeIds = sonarrEpisodeIds, monitored = true }, options: JsonOptions);
        return SendAsync(request, ct);
    }

    /// <summary>Sets the given episodes to unmonitored, without touching any other episode's monitored state. Used to reconcile episodes Shoko has already imported so Sonarr's own automatic/RSS search stops re-fetching them.</summary>
    public virtual Task<ArrActionResult<bool>> UnmonitorEpisodesAsync(SonarrSettings settings, List<int> sonarrEpisodeIds, CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Put, settings, "/api/v3/episode/monitor");
        request.Content = JsonContent.Create(new { episodeIds = sonarrEpisodeIds, monitored = false }, options: JsonOptions);
        return SendAsync(request, ct);
    }

    /// <summary>Triggers Sonarr's EpisodeSearch command for the given episodes.</summary>
    public Task<ArrActionResult<bool>> TriggerEpisodeSearchAsync(SonarrSettings settings, List<int> sonarrEpisodeIds, CancellationToken ct = default)
    {
        var request = BuildRequest(HttpMethod.Post, settings, "/api/v3/command");
        request.Content = JsonContent.Create(new { name = "EpisodeSearch", episodeIds = sonarrEpisodeIds }, options: JsonOptions);
        return SendAsync(request, ct);
    }
}
