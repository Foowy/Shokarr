using Shokarr.Models;
using Shokarr.Services;
using Xunit;

namespace Shokarr.Tests;

public class SpecialMatcherTests
{
    private static SonarrEpisodeResource Ep(int id, int number, string title, string? airDate) =>
        new(id, 0, number, null, title, airDate);

    private static MissingEpisodeInfo Special(int number, string title, DateOnly? airDate) =>
        new() { EpisodeNumber = number, IsSpecial = true, Title = title, AirDate = airDate };

    [Fact]
    public void Match_PrefersAirDateAndTitleOverMatchingNumber()
    {
        var episodes = new List<SonarrEpisodeResource>
        {
            Ep(1, 12, "SLF Theater Mini #2", "2023-10-01"),
            Ep(2, 20, "SLF Theater Mini #3 Bird-Frog Ensemble", "2023-10-08"),
        };

        var match = SpecialMatcher.Match(episodes, Special(12, "SLF Mini #3: Bird-Frog Ensemble", new DateOnly(2023, 10, 8)));

        Assert.Equal(2, match!.Id);
    }

    [Fact]
    public void Match_NumberWithConflictingAirDate_IsUnmapped()
    {
        var episodes = new List<SonarrEpisodeResource> { Ep(1, 5, "New Year's Special", "2021-01-09") };

        Assert.Null(SpecialMatcher.Match(episodes, Special(5, "Juju Stroll 4", new DateOnly(2020, 11, 7))));
    }

    [Fact]
    public void Match_NumberFallbackWhenNoAirDates()
    {
        var episodes = new List<SonarrEpisodeResource> { Ep(1, 3, "Dies Irae ONA 1", null) };

        Assert.Equal(1, SpecialMatcher.Match(episodes, Special(3, "OVA 1", null))!.Id);
    }

    [Fact]
    public void Match_NearIdenticalTitleMatchesAcrossDateGap()
    {
        var episodes = new List<SonarrEpisodeResource> { Ep(7, 9, "Welcome to Zounen Temple Part 5", "2017-12-30") };

        Assert.Equal(7, SpecialMatcher.Match(episodes, Special(1, "Welcome to Zounen Temple: Part 5", new DateOnly(2017, 8, 24)))!.Id);
    }

    [Fact]
    public void Match_IgnoresNonSeasonZeroEpisodes()
    {
        var episodes = new List<SonarrEpisodeResource> { new(1, 1, 1, 1, "OVA 1", "2020-01-01") };

        Assert.Null(SpecialMatcher.Match(episodes, Special(1, "OVA 1", new DateOnly(2020, 1, 1))));
    }
}
