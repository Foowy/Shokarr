using System.Net;
using System.Text.Json;
using Shokarr.Config;
using Shokarr.Models;
using Shokarr.Services;
using Xunit;

namespace Shokarr.Tests;

public class SonarrSearchServiceTests : IDisposable
{
    private readonly List<ScanCacheStore> _stores = [];
    private readonly List<string> _dirs = [];

    public void Dispose()
    {
        foreach (var store in _stores)
            store.Dispose();
        foreach (var dir in _dirs)
            Directory.Delete(dir, recursive: true);
    }

    private static SonarrSettings TestSettings => new() { BaseUrl = "http://sonarr.local:8989", ApiKey = "testkey" };

    private class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private static SonarrClient MakeClient(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new FakeHandler(respond)));

    private SonarrSearchService MakeService(SonarrClient client)
    {
        var dir = Path.Combine(Path.GetTempPath(), "shokarr-tests-" + Guid.NewGuid());
        _dirs.Add(dir);
        var cacheStore = new ScanCacheStore(dir);
        _stores.Add(cacheStore);
        var notificationService = new NotificationService(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));
        return new SonarrSearchService(client, cacheStore, notificationService);
    }

    private static SeriesMissingResult SeriesWith(params (int anidbId, int epNum, bool special, string title)[] eps) =>
        new()
        {
            ShokoSeriesId = 1,
            Title = "Test Anime",
            TvdbId = 100,
            MissingEpisodes = [.. eps.Select(e => new MissingEpisodeInfo
            {
                AnidbEpisodeId = e.anidbId, EpisodeNumber = e.epNum, IsSpecial = e.special, Title = e.title,
            })],
        };

    [Fact]
    public async Task MonitorAndSearch_MultiSeasonSonarrSeries_MapsNormalEpisodeByAbsoluteNumber()
    {
        var episodes = new List<object>();
        for (int i = 1; i <= 12; i++) episodes.Add(new { id = 100 + i, seasonNumber = 1, episodeNumber = i, absoluteEpisodeNumber = i });
        for (int i = 1; i <= 12; i++) episodes.Add(new { id = 200 + i, seasonNumber = 2, episodeNumber = i, absoluteEpisodeNumber = 12 + i });

        HttpRequestMessage? searchReq = null;
        var client = MakeClient(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/episode") && req.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(episodes)) };
            if (req.RequestUri.AbsolutePath.EndsWith("/command")) { searchReq = req; }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });
        var service = MakeService(client);
        var series = SeriesWith((anidbId: 5014, epNum: 14, special: false, title: "Episode 14"));

        var result = await service.MonitorAndSearchAsync(TestSettings, shokoSeriesId: 1, sonarrSeriesId: 9, anidbEpisodeIds: [5014], series, ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var body = await searchReq!.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("202", body);            // mapped to the season-2 episode (abs 14)
        Assert.DoesNotContain("102", body);       // not the season-1 episode
    }

    [Fact]
    public async Task MonitorAndSearch_PersistsTitleSlugOnPendingSearch()
    {
        var episodes = new[] { new { id = 101, seasonNumber = 1, episodeNumber = 1, absoluteEpisodeNumber = 1 } };
        var client = MakeClient(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(req.RequestUri!.AbsolutePath.Contains("/episode") && req.Method == HttpMethod.Get ? JsonSerializer.Serialize(episodes) : "{}"),
        });
        var service = MakeService(client);

        var result = await service.MonitorAndSearchAsync(TestSettings, 1, 9, [5001], SeriesWith((5001, 1, false, "Episode 1")), "some-anime", ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("some-anime", _stores[^1].GetPendingSearches().Single().SonarrTitleSlug);
        Assert.Equal(SearchHistoryOutcome.Triggered, _stores[^1].GetHistory().Single().Outcome);
    }

    [Fact]
    public async Task MonitorAndSearch_SpecialStillMapsViaSeasonZero()
    {
        var episodes = new List<object>
        {
            new { id = 300, seasonNumber = 0, episodeNumber = 1, absoluteEpisodeNumber = (int?)null },
            new { id = 301, seasonNumber = 1, episodeNumber = 1, absoluteEpisodeNumber = (int?)1 },
        };
        HttpRequestMessage? searchReq = null;
        var client = MakeClient(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/episode") && req.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(episodes)) };
            if (req.RequestUri.AbsolutePath.EndsWith("/command")) searchReq = req;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });
        var service = MakeService(client);
        var series = SeriesWith((anidbId: 5900, epNum: 1, special: true, title: "OVA 1"));

        var result = await service.MonitorAndSearchAsync(TestSettings, 1, 9, [5900], series, ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains("300", await searchReq!.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MonitorAndSearch_LegacyStandardSeries_FallsBackToSeasonOneMatch()
    {
        var episodes = new List<object>
        {
            new { id = 401, seasonNumber = 1, episodeNumber = 1, absoluteEpisodeNumber = (int?)null },
            new { id = 402, seasonNumber = 1, episodeNumber = 2, absoluteEpisodeNumber = (int?)null },
        };
        HttpRequestMessage? searchReq = null;
        var client = MakeClient(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/episode") && req.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(episodes)) };
            if (req.RequestUri.AbsolutePath.EndsWith("/command")) searchReq = req;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });
        var service = MakeService(client);
        var series = SeriesWith((anidbId: 5002, epNum: 2, special: false, title: "Episode 2"));

        var result = await service.MonitorAndSearchAsync(TestSettings, 1, 9, [5002], series, ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains("402", await searchReq!.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body) };

    private static string OneEpisode => JsonSerializer.Serialize(new[] { new { id = 101, seasonNumber = 1, episodeNumber = 1, absoluteEpisodeNumber = 1 } });

    [Fact]
    public async Task MonitorAndSearch_SearchFails_UnmonitorsEpisodesAgain()
    {
        var monitorBodies = new List<string>();
        var client = MakeClient(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get) return Json(OneEpisode);
            if (path.EndsWith("/command")) return Json("{}", HttpStatusCode.InternalServerError);
            monitorBodies.Add(req.Content!.ReadAsStringAsync().Result);
            return Json("{}");
        });
        var service = MakeService(client);

        var result = await service.MonitorAndSearchAsync(TestSettings, 1, 9, [5001], SeriesWith((5001, 1, false, "Episode 1")), ct: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(2, monitorBodies.Count);
        Assert.Contains("true", monitorBodies[0]);
        Assert.Contains("false", monitorBodies[1]);
        Assert.Empty(_stores[^1].GetPendingSearches());
    }

    [Fact]
    public async Task MonitorAndSearch_SearchAndUnmonitorFail_TracksEpisodesAsPending()
    {
        var monitorCalls = 0;
        var client = MakeClient(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get) return Json(OneEpisode);
            if (path.EndsWith("/command")) return Json("{}", HttpStatusCode.InternalServerError);
            return ++monitorCalls == 1 ? Json("{}") : Json("{}", HttpStatusCode.InternalServerError);
        });
        var service = MakeService(client);

        var result = await service.MonitorAndSearchAsync(TestSettings, 1, 9, [5001], SeriesWith((5001, 1, false, "Episode 1")), ct: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(101, _stores[^1].GetPendingSearches().Single().SonarrEpisodeId);
    }

    [Fact]
    public async Task MonitorAndSearch_EpisodeAlreadyPending_IsNotSearchedAgain()
    {
        var requests = new List<string>();
        var client = MakeClient(req =>
        {
            requests.Add(req.Method + " " + req.RequestUri!.AbsolutePath);
            return Json(req.Method == HttpMethod.Get ? OneEpisode : "{}");
        });
        var service = MakeService(client);
        var series = SeriesWith((5001, 1, false, "Episode 1"));
        _stores[^1].AddPendingSearch(new PendingSearch { ShokoSeriesId = 1, AnidbEpisodeId = 5001, SonarrSeriesId = 9, SonarrEpisodeId = 101, TriggeredAtUtc = DateTime.UtcNow });

        var result = await service.MonitorAndSearchAsync(TestSettings, 1, 9, [5001], series, ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.DoesNotContain(requests, r => r.EndsWith("/command") || r.EndsWith("/episode/monitor"));
        Assert.Empty(_stores[^1].GetHistory());
    }

    [Fact]
    public async Task MonitorAndSearch_EmptyEpisodeList_IsPolledUntilEpisodesAppear()
    {
        var episodeGets = 0;
        var client = MakeClient(req =>
        {
            if (req.Method == HttpMethod.Get) return Json(++episodeGets < 3 ? "[]" : OneEpisode);
            return Json("{}");
        });
        var service = MakeService(client);

        var result = await service.MonitorAndSearchAsync(TestSettings, 1, 9, [5001], SeriesWith((5001, 1, false, "Episode 1")), episodeLookupAttempts: 5, episodeLookupDelay: TimeSpan.Zero, ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(3, episodeGets);
        Assert.Single(_stores[^1].GetPendingSearches());
    }
}
