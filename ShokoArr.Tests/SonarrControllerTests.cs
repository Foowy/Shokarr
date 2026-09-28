using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ShokoArr.Config;
using ShokoArr.Controllers;
using ShokoArr.Controllers.Api;
using ShokoArr.Models;
using ShokoArr.Services;
using Xunit;

namespace ShokoArr.Tests;

public class SonarrControllerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScanCacheStore _cacheStore;
    private readonly FakeSettingsSource _settings = new();

    public SonarrControllerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "shoko-arr-controller-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
        _cacheStore = new ScanCacheStore(_tempDir);
        _settings.Sonarr = new SonarrSettings { BaseUrl = "http://sonarr.local:8989", ApiKey = "testkey" };
    }

    public void Dispose()
    {
        _cacheStore.Dispose();
        Directory.Delete(_tempDir, recursive: true);
    }

    private class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private SonarrController MakeController(Func<HttpRequestMessage, HttpResponseMessage> respond, ScanCacheStore cacheStore)
    {
        var handler = new FakeHandler(respond);
        var httpClient = new HttpClient(handler);
        var sonarrClient = new SonarrClient(httpClient);
        var notificationService = new NotificationService(httpClient); // no webhook configured - NotifyAsync no-ops
        var matcher = new SeriesMatcher(sonarrClient);
        var searchService = new SonarrSearchService(sonarrClient, cacheStore, notificationService);
        return new SonarrController(matcher, searchService, sonarrClient, cacheStore, notificationService, _settings);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body)) };

    [Fact]
    public async Task GetMatch_SeriesNotInLastScan_ReturnsNotFound()
    {
        var controller = MakeController(_ => new HttpResponseMessage(HttpStatusCode.OK), _cacheStore);

        var result = await controller.GetMatch(999);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var response = Assert.IsType<ShokoArrBaseController.ApiResponse<object>>(notFound.Value);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task GetMatch_SeriesWithTvdbId_AutoResolves()
    {
        _cacheStore.SaveScan(new ScanSnapshot
        {
            Series = [new SeriesMissingResult { ShokoSeriesId = 1, Title = "Test Series", TvdbId = 42 }],
        });
        var controller = MakeController(
            _ => JsonResponse(HttpStatusCode.OK, new[] { new { id = 1 } }),
            _cacheStore);

        var result = await controller.GetMatch(1);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ShokoArrBaseController.ApiResponse<object>>(ok.Value);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task SearchTitle_PassesThroughMatcherResult()
    {
        var controller = MakeController(
            _ => JsonResponse(HttpStatusCode.OK, new[] { new { id = 7, title = "Some Show" } }),
            _cacheStore);

        var result = await controller.SearchTitle(new SearchTitleRequest("Some Show"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ShokoArrBaseController.ApiResponse<object>>(ok.Value);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task AddDiscovery_NoQualityProfileConfigured_ReturnsBadRequest()
    {
        var controller = MakeController(_ => new HttpResponseMessage(HttpStatusCode.OK), _cacheStore);

        var result = await controller.AddDiscovery(new AddDiscoveryRequest(42, "New Show"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<ShokoArrBaseController.ApiResponse<object>>(badRequest.Value);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task AddDiscovery_SonarrAddFails_ReturnsConflict()
    {
        _settings.Sonarr = new SonarrSettings { BaseUrl = "http://sonarr.local:8989", ApiKey = "testkey", QualityProfileId = 1, RootFolderPath = "/anime" };
        var controller = MakeController(
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            _cacheStore);

        var result = await controller.AddDiscovery(new AddDiscoveryRequest(42, "New Show"));

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var response = Assert.IsType<ShokoArrBaseController.ApiResponse<object>>(conflict.Value);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task AddDiscovery_Success_ReturnsOk()
    {
        _settings.Sonarr = new SonarrSettings { BaseUrl = "http://sonarr.local:8989", ApiKey = "testkey", QualityProfileId = 1, RootFolderPath = "/anime" };
        var controller = MakeController(
            _ => JsonResponse(HttpStatusCode.OK, new { id = 5 }),
            _cacheStore);

        var result = await controller.AddDiscovery(new AddDiscoveryRequest(42, "New Show"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ShokoArrBaseController.ApiResponse<object>>(ok.Value);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task AddAndSearch_SeriesNotInLastScan_ReturnsNotFound()
    {
        var controller = MakeController(_ => new HttpResponseMessage(HttpStatusCode.OK), _cacheStore);

        var result = await controller.AddAndSearch(new AddAndSearchRequest(999, 42, [1]));

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var response = Assert.IsType<ShokoArrBaseController.ApiResponse<object>>(notFound.Value);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task AddAndSearch_NoQualityProfileConfigured_ReturnsBadRequest()
    {
        _cacheStore.SaveScan(new ScanSnapshot
        {
            Series = [new SeriesMissingResult { ShokoSeriesId = 1, Title = "Test Series" }],
        });
        var controller = MakeController(_ => new HttpResponseMessage(HttpStatusCode.OK), _cacheStore);

        var result = await controller.AddAndSearch(new AddAndSearchRequest(1, 42, [1]));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<ShokoArrBaseController.ApiResponse<object>>(badRequest.Value);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task SyncTags_NoCandidates_ReturnsZeroedSummary()
    {
        _cacheStore.SaveScan(new ScanSnapshot { Series = [] });
        var controller = MakeController(_ => new HttpResponseMessage(HttpStatusCode.OK), _cacheStore);

        var result = await controller.SyncTags();

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ShokoArrBaseController.ApiResponse<object>>(ok.Value);
        var data = Assert.IsType<TagSyncResult>(response.Data);
        Assert.Equal(0, data.Updated);
        Assert.Equal(0, data.SkippedNoMatch);
        Assert.Equal(0, data.Failed);
    }

    [Fact]
    public async Task SyncTags_SeriesNotInSonarr_CountsAsSkipped()
    {
        _cacheStore.SaveScan(new ScanSnapshot
        {
            Series = [new SeriesMissingResult { ShokoSeriesId = 1, Title = "Test Series", TvdbId = 42, GroupTitle = "Some Group" }],
        });
        var controller = MakeController(
            _ => JsonResponse(HttpStatusCode.OK, Array.Empty<object>()), // GetExistingSeriesByTvdbIdAsync returns no matches
            _cacheStore);

        var result = await controller.SyncTags();

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ShokoArrBaseController.ApiResponse<object>>(ok.Value);
        var data = Assert.IsType<TagSyncResult>(response.Data);
        Assert.Equal(1, data.SkippedNoMatch);
    }
}
