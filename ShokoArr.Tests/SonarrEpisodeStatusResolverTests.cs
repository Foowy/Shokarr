using System.Net;
using ShokoArr.Config;
using ShokoArr.Models;
using ShokoArr.Services;
using Xunit;

namespace ShokoArr.Tests;

public class SonarrEpisodeStatusResolverTests
{
    private static SonarrSettings Settings => new() { BaseUrl = "http://sonarr.local:8989", ApiKey = "k" };

    private class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static HttpResponseMessage HappyPath(HttpRequestMessage r)
    {
        var path = r.RequestUri!.PathAndQuery;
        if (path.StartsWith("/api/v3/queue"))
            return Json("""{"records":[{"episodeId":102},{"episodeId":null},{}],"totalRecords":3}""");
        if (path.StartsWith("/api/v3/series"))
            return Json("""[{"id":7,"titleSlug":"x"}]""");
        return Json("""
            [{"id":101,"seasonNumber":1,"episodeNumber":1,"absoluteEpisodeNumber":1,"hasFile":true},
            {"id":102,"seasonNumber":1,"episodeNumber":2,"absoluteEpisodeNumber":2,"hasFile":false},
            {"id":103,"seasonNumber":1,"episodeNumber":3,"absoluteEpisodeNumber":3,"hasFile":false},
            {"id":104,"seasonNumber":1,"episodeNumber":4,"absoluteEpisodeNumber":4,"hasFile":true}]
            """);
    }

    private static SeriesMissingResult Series(int? tvdb, params int[] numbers) => new()
    {
        ShokoSeriesId = 1,
        TvdbId = tvdb,
        MissingEpisodes = [.. numbers.Select(n => new MissingEpisodeInfo { AnidbEpisodeId = n, EpisodeNumber = n })],
    };

    [Fact]
    public async Task Apply_MapsDownloadedDownloadingAndNone()
    {
        var handler = new CountingHandler(HappyPath);
        var session = await new SonarrEpisodeStatusResolver(new SonarrClient(new HttpClient(handler))).BeginAsync(Settings, TestContext.Current.CancellationToken);
        var series = Series(1, 1, 2, 3, 9);

        await session.ApplyAsync(series);

        Assert.Equal(["downloaded", "downloading", "none", "none"], series.MissingEpisodes.Select(e => e.SonarrState));
    }

    [Fact]
    public async Task Apply_HasFileAndQueued_IsDownloaded()
    {
        var handler = new CountingHandler(r => r.RequestUri!.PathAndQuery.StartsWith("/api/v3/queue")
            ? Json("""{"records":[{"episodeId":104}]}""")
            : HappyPath(r));
        var session = await new SonarrEpisodeStatusResolver(new SonarrClient(new HttpClient(handler))).BeginAsync(Settings, TestContext.Current.CancellationToken);
        var series = Series(1, 4);

        await session.ApplyAsync(series);

        Assert.Equal("downloaded", series.MissingEpisodes[0].SonarrState);
    }

    [Fact]
    public async Task Apply_NoTvdbId_LeavesNoneWithoutCalls()
    {
        var handler = new CountingHandler(HappyPath);
        var session = await new SonarrEpisodeStatusResolver(new SonarrClient(new HttpClient(handler))).BeginAsync(Settings, TestContext.Current.CancellationToken);
        var series = Series(null, 1);

        await session.ApplyAsync(series);

        Assert.Equal("none", series.MissingEpisodes[0].SonarrState);
        Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task Apply_SeriesNotInSonarr_LeavesNone()
    {
        var handler = new CountingHandler(r => r.RequestUri!.PathAndQuery.StartsWith("/api/v3/series") ? Json("[]") : HappyPath(r));
        var session = await new SonarrEpisodeStatusResolver(new SonarrClient(new HttpClient(handler))).BeginAsync(Settings, TestContext.Current.CancellationToken);
        var series = Series(1, 1);

        await session.ApplyAsync(series);

        Assert.Equal("none", series.MissingEpisodes[0].SonarrState);
    }

    [Fact]
    public async Task Begin_EmptyBaseUrl_MakesNoHttpCalls()
    {
        var handler = new CountingHandler(HappyPath);
        var session = await new SonarrEpisodeStatusResolver(new SonarrClient(new HttpClient(handler))).BeginAsync(new SonarrSettings(), TestContext.Current.CancellationToken);
        var series = Series(1, 1);

        await session.ApplyAsync(series);

        Assert.Empty(handler.Paths);
        Assert.Equal("none", series.MissingEpisodes[0].SonarrState);
    }

    [Fact]
    public async Task Apply_QueueFetchedOncePerScan()
    {
        var handler = new CountingHandler(HappyPath);
        var session = await new SonarrEpisodeStatusResolver(new SonarrClient(new HttpClient(handler))).BeginAsync(Settings, TestContext.Current.CancellationToken);

        await session.ApplyAsync(Series(1, 1));
        await session.ApplyAsync(Series(2, 1));
        await session.ApplyAsync(Series(3, 1));

        Assert.Single(handler.Paths, p => p.StartsWith("/api/v3/queue"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Apply_FirstCallFails_CircuitBreakerSkipsAllFurtherCalls(bool handlerThrows)
    {
        var handler = new CountingHandler(_ => handlerThrows
            ? throw new HttpRequestException("connection refused")
            : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var session = await new SonarrEpisodeStatusResolver(new SonarrClient(new HttpClient(handler))).BeginAsync(Settings, TestContext.Current.CancellationToken);
        var all = new[] { Series(1, 1), Series(2, 1), Series(3, 1) };

        foreach (var s in all)
            await session.ApplyAsync(s);

        Assert.Single(handler.Paths);
        Assert.All(all, s => Assert.Equal("none", s.MissingEpisodes[0].SonarrState));
    }
}
