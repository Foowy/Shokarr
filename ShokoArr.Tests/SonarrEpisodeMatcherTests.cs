using ShokoArr.Models;
using ShokoArr.Services;
using Xunit;

namespace ShokoArr.Tests;

public class SonarrEpisodeMatcherTests
{
    [Fact]
    public void Match_Special_UsesAirDate()
    {
        var episodes = new List<SonarrEpisodeResource> { new(5, 0, 1, Title: "Beach Episode", AirDate: "2020-01-02") };
        var special = new MissingEpisodeInfo { IsSpecial = true, EpisodeNumber = 9, Title = "Beach Episode", AirDate = new DateOnly(2020, 1, 2) };

        Assert.Equal(5, SonarrEpisodeMatcher.Match(episodes, false, special)?.Id);
    }

    [Fact]
    public void Match_NoAbsolute_FallsBackToSeasonOne()
    {
        var episodes = new List<SonarrEpisodeResource> { new(1, 2, 3), new(2, 1, 3) };

        Assert.Equal(2, SonarrEpisodeMatcher.Match(episodes, false, new MissingEpisodeInfo { EpisodeNumber = 3 })?.Id);
    }

    // A two-cour show: Sonarr/TheTVDB number it continuously (absolute 1-24), AniDB gives cour 2 its own entry numbered from 1.
    private static List<SonarrEpisodeResource> TwoCourShow() =>
    [
        .. Enumerable.Range(1, 12).Select(n => new SonarrEpisodeResource(n, 1, n, n, $"Episode {n}", new DateOnly(2024, 1, 5).AddDays(7 * (n - 1)).ToString("yyyy-MM-dd"))),
        .. Enumerable.Range(13, 12).Select(n => new SonarrEpisodeResource(n, 1, n, n, $"Episode {n}", new DateOnly(2024, 7, 5).AddDays(7 * (n - 13)).ToString("yyyy-MM-dd"))),
    ];

    [Theory]
    [InlineData(1, 13)]
    [InlineData(5, 17)]
    [InlineData(12, 24)]
    public void Match_SecondCourEntry_UsesAirDateNotAbsoluteNumber(int anidbEpisode, int expectedSonarrId)
    {
        var ep = new MissingEpisodeInfo { EpisodeNumber = anidbEpisode, AirDate = new DateOnly(2024, 7, 5).AddDays(7 * (anidbEpisode - 1)) };

        Assert.Equal(expectedSonarrId, SonarrEpisodeMatcher.Match(TwoCourShow(), true, ep)?.Id);
    }

    [Fact]
    public void Match_FirstCourEntry_StillMatchesByNumber()
    {
        var ep = new MissingEpisodeInfo { EpisodeNumber = 3, AirDate = new DateOnly(2024, 1, 19) };

        Assert.Equal(3, SonarrEpisodeMatcher.Match(TwoCourShow(), true, ep)?.Id);
    }

    [Fact]
    public void Match_NumberContradictedByAirDate_ReturnsNull()
    {
        // Cour 2 episode 1 whose date Sonarr doesn't list at all: absolute 1 exists but aired months earlier.
        var ep = new MissingEpisodeInfo { EpisodeNumber = 1, AirDate = new DateOnly(2025, 3, 1) };

        Assert.Null(SonarrEpisodeMatcher.Match(TwoCourShow(), true, ep));
    }

    [Fact]
    public void Match_NoAirDates_FallsBackToNumber()
    {
        var episodes = new List<SonarrEpisodeResource> { new(7, 1, 1, 1), new(8, 1, 2, 2) };

        Assert.Equal(8, SonarrEpisodeMatcher.Match(episodes, true, new MissingEpisodeInfo { EpisodeNumber = 2, AirDate = new DateOnly(2024, 1, 1) })?.Id);
    }

    [Fact]
    public void Match_SameDayRelease_PrefersNumberThenTitle()
    {
        // A binge drop: every episode shares one air date.
        var episodes = Enumerable.Range(1, 4).Select(n => new SonarrEpisodeResource(n, 1, n, n, $"Chapter {n} The {(n == 3 ? "Storm" : "Calm")}", "2024-05-01")).ToList();
        var aired = new DateOnly(2024, 5, 1);

        Assert.Equal(2, SonarrEpisodeMatcher.Match(episodes, true, new MissingEpisodeInfo { EpisodeNumber = 2, AirDate = aired })?.Id);
        Assert.Equal(3, SonarrEpisodeMatcher.Match(episodes, true, new MissingEpisodeInfo { EpisodeNumber = 9, Title = "Chapter 3 The Storm", AirDate = aired })?.Id);
    }
}
