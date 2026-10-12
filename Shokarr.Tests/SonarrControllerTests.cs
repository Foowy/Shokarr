using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Shoko.Abstractions.Metadata.Services;
using Shoko.Abstractions.Metadata.Shoko;
using Shokarr.Config;
using Shokarr.Controllers;
using Shokarr.Controllers.Api;
using Shokarr.Models;
using Shokarr.Services;
using Xunit;

namespace Shokarr.Tests;

public class SonarrControllerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScanCacheStore _cacheStore;
    private readonly FakeSettingsSource _settings = new();
    private readonly Mock<IMetadataService> _metadata = new();

    public SonarrControllerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "shokarr-controller-tests-" + Guid.NewGuid());
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
        return new SonarrController(matcher, searchService, sonarrClient, cacheStore, notificationService, _settings, _metadata.Object);
    }

    /// <summary>Makes the given series owned in Shoko. The TVDB ID is stored as a confirmed override, which the controller resolves the same way as a TMDB-linked one.</summary>
    private void Owns(params (int Id, int TvdbId, string Group)[] series)
    {
        var mocks = series.Select(x =>
        {
            var group = new Mock<IShokoGroup>();
            group.Setup(g => g.Title).Returns(x.Group);
            var m = new Mock<IShokoSeries>();
            m.Setup(s => s.LocalID).Returns(x.Id);
            m.Setup(s => s.Title).Returns($"S{x.Id}");
            m.Setup(s => s.ParentGroup).Returns(group.Object);
            m.Setup(s => s.LinkedSeries).Returns([]);
            _cacheStore.SetSeriesTvdbId(x.Id, x.TvdbId);
            return m.Object;
        }).ToList();
        _metadata.Setup(m => m.GetAllShokoSeries()).Returns(mocks);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body)) };

    [Fact]
    public async Task GetMatch_SeriesNotInLastScan_ReturnsNotFound()
    {
        var controller = MakeController(_ => new HttpResponseMessage(HttpStatusCode.OK), _cacheStore);

        var result = await controller.GetMatch(999);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var response = Assert.IsType<ShokarrBaseController.ApiResponse<object>>(notFound.Value);
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
        var response = Assert.IsType<ShokarrBaseController.ApiResponse<object>>(ok.Value);
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
        var response = Assert.IsType<ShokarrBaseController.ApiResponse<object>>(ok.Value);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task AddDiscovery_NoQualityProfileConfigured_ReturnsBadRequest()
    {
        var controller = MakeController(_ => new HttpResponseMessage(HttpStatusCode.OK), _cacheStore);

        var result = await controller.AddDiscovery(new AddDiscoveryRequest(42, "New Show"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<ShokarrBaseController.ApiResponse<object>>(badRequest.Value);
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
        var response = Assert.IsType<ShokarrBaseController.ApiResponse<object>>(conflict.Value);
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
        var response = Assert.IsType<ShokarrBaseController.ApiResponse<object>>(ok.Value);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task AddAndSearch_SeriesNotInLastScan_ReturnsNotFound()
    {
        var controller = MakeController(_ => new HttpResponseMessage(HttpStatusCode.OK), _cacheStore);

        var result = await controller.AddAndSearch(new AddAndSearchRequest(999, 42, [1]));

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var response = Assert.IsType<ShokarrBaseController.ApiResponse<object>>(notFound.Value);
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
        var response = Assert.IsType<ShokarrBaseController.ApiResponse<object>>(badRequest.Value);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task SyncTags_NoCandidates_ReturnsZeroedSummary()
    {
        Owns();
        var controller = MakeController(_ => new HttpResponseMessage(HttpStatusCode.OK), _cacheStore);

        var result = await controller.SyncTags();

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ShokarrBaseController.ApiResponse<object>>(ok.Value);
        var data = Assert.IsType<TagSyncResult>(response.Data);
        Assert.Equal(0, data.Updated);
        Assert.Equal(0, data.SkippedNoMatch);
        Assert.Equal(0, data.Failed);
    }

    [Fact]
    public async Task SyncTags_SeriesNotInSonarr_CountsAsSkipped()
    {
        Owns((1, 42, "Some Group"));
        var controller = MakeController(
            _ => JsonResponse(HttpStatusCode.OK, Array.Empty<object>()), // GetExistingSeriesByTvdbIdAsync returns no matches
            _cacheStore);

        var result = await controller.SyncTags();

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ShokarrBaseController.ApiResponse<object>>(ok.Value);
        var data = Assert.IsType<TagSyncResult>(response.Data);
        Assert.Equal(1, data.SkippedNoMatch);
    }

    [Fact]
    public async Task SyncTags_SonarrUnreachable_ReportsFailureInsteadOfSkips()
    {
        Owns((1, 42, "Some Group"));
        var controller = MakeController(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError), _cacheStore);

        var result = await controller.SyncTags();

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ShokarrBaseController.ApiResponse<object>>(ok.Value);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task SyncTags_LooksUpEachGroupTagOnce()
    {
        Owns((1, 10, "Group"), (2, 11, "Group"));
        var tagRequests = 0;
        var controller = MakeController(r =>
        {
            var path = r.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/v3/tag"))
            {
                tagRequests++;
                return JsonResponse(HttpStatusCode.OK, new[] { new { id = 5, label = "Group" } });
            }
            if (path == "/api/v3/series")
                return JsonResponse(HttpStatusCode.OK, new[] { new { id = 1, tvdbId = 10 }, new { id = 2, tvdbId = 11 } });
            return JsonResponse(HttpStatusCode.OK, new { id = 1, tags = Array.Empty<int>() });
        }, _cacheStore);

        var result = await controller.SyncTags();

        var data = Assert.IsType<TagSyncResult>(Assert.IsType<ShokarrBaseController.ApiResponse<object>>(Assert.IsType<OkObjectResult>(result).Value).Data);
        Assert.Equal(2, data.Updated);
        Assert.Equal(1, tagRequests);
    }

    private (SonarrController Controller, Func<int> CreateAttempts) SyncTagsController(int groupSize, Func<int, bool> createSucceeds)
    {
        Owns([.. Enumerable.Range(1, groupSize).Select(i => (i, 10 + i, "Group"))]);
        var attempts = 0;
        var controller = MakeController(r =>
        {
            var path = r.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/v3/tag"))
            {
                if (r.Method == HttpMethod.Get)
                    return JsonResponse(HttpStatusCode.OK, Array.Empty<object>());
                return createSucceeds(++attempts) ? JsonResponse(HttpStatusCode.OK, new { id = 5, label = "Group" }) : new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }
            if (path == "/api/v3/series")
                return JsonResponse(HttpStatusCode.OK, Enumerable.Range(1, groupSize).Select(i => new { id = i, tvdbId = 10 + i }).ToArray());
            return JsonResponse(HttpStatusCode.OK, new { id = 1, tags = Array.Empty<int>() });
        }, _cacheStore);
        return (controller, () => attempts);
    }

    private static TagSyncResult TagResultOf(IActionResult result) =>
        Assert.IsType<TagSyncResult>(Assert.IsType<ShokarrBaseController.ApiResponse<object>>(Assert.IsType<OkObjectResult>(result).Value).Data);

    [Fact]
    public async Task SyncTags_TransientCreateFailure_IsRetriedForTheNextSeries()
    {
        var (controller, createAttempts) = SyncTagsController(groupSize: 2, createSucceeds: n => n > 1);

        var data = TagResultOf(await controller.SyncTags());

        Assert.Equal(1, data.Failed);
        Assert.Equal(1, data.Updated);
        Assert.Equal(2, createAttempts());
    }

    [Fact]
    public async Task SyncTags_RejectedLabel_StopsRetryingAfterTwoAttempts()
    {
        var (controller, createAttempts) = SyncTagsController(groupSize: 5, createSucceeds: _ => false);

        var data = TagResultOf(await controller.SyncTags());

        Assert.Equal(5, data.Failed);
        Assert.Equal(2, createAttempts());
    }

    [Fact]
    public async Task SyncTags_TagListUnavailable_ReportsFailure()
    {
        Owns((1, 10, "Group"));
        var controller = MakeController(r => r.RequestUri!.PathAndQuery == "/api/v3/series"
            ? JsonResponse(HttpStatusCode.OK, new[] { new { id = 1, tvdbId = 10 } })
            : new HttpResponseMessage(HttpStatusCode.InternalServerError), _cacheStore);

        var result = await controller.SyncTags();

        Assert.False(Assert.IsType<ShokarrBaseController.ApiResponse<object>>(Assert.IsType<OkObjectResult>(result).Value).Success);
    }

    [Fact]
    public async Task SyncTags_CoversOwnedSeriesThatAreNotInTheLastScan()
    {
        _cacheStore.SaveScan(new ScanSnapshot { Series = [] });
        Owns((1, 10, "Group"));
        var tagged = 0;
        var controller = MakeController(r =>
        {
            var path = r.RequestUri!.PathAndQuery;
            if (path.StartsWith("/api/v3/tag"))
                return JsonResponse(HttpStatusCode.OK, new[] { new { id = 5, label = "Group" } });
            if (path == "/api/v3/series")
                return JsonResponse(HttpStatusCode.OK, new[] { new { id = 1, tvdbId = 10 } });
            tagged++;
            return JsonResponse(HttpStatusCode.OK, new { id = 1, tags = Array.Empty<int>() });
        }, _cacheStore);

        var data = TagResultOf(await controller.SyncTags());

        Assert.Equal(1, data.Updated);
        Assert.True(tagged > 0);
    }

    [Fact]
    public async Task AddAndSearch_TitleSearchMatch_IsRememberedForTheSeries()
    {
        _settings.Sonarr = new SonarrSettings { BaseUrl = "http://sonarr.local:8989", ApiKey = "testkey", QualityProfileId = 1, RootFolderPath = "/tv" };
        _cacheStore.SaveScan(new ScanSnapshot { Series = [new SeriesMissingResult { ShokoSeriesId = 1, Title = "Test Series" }] });
        var controller = MakeController(r =>
            JsonResponse(HttpStatusCode.OK, r.RequestUri!.PathAndQuery.Contains("tvdbId") ? new[] { new { id = 9, tvdbId = 42, titleSlug = "test" } } : Array.Empty<object>()), _cacheStore);
        controller.ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };

        await controller.AddAndSearch(new AddAndSearchRequest(1, 42, [1]));

        Assert.Equal(42, _cacheStore.GetSeriesOverride(1)!.TvdbId);
    }
}
