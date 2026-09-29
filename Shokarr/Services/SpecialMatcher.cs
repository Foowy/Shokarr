using System.Text.RegularExpressions;
using Shokarr.Models;

namespace Shokarr.Services;

/// <summary>Matches an AniDB special to a Sonarr season-0 episode. AniDB and TheTVDB number specials independently, so the number alone often points at a different episode.</summary>
public static partial class SpecialMatcher
{
    internal const int MaxAirDateDriftDays = 3;

    public static SonarrEpisodeResource? Match(List<SonarrEpisodeResource> sonarrEpisodes, MissingEpisodeInfo special)
    {
        var season0 = sonarrEpisodes.Where(se => se.SeasonNumber == 0).ToList();

        if (special.AirDate is { } aired)
        {
            var byDate = season0
                .Where(se => AirDateOf(se) is { } d && Math.Abs(d.DayNumber - aired.DayNumber) <= MaxAirDateDriftDays)
                .Select(se => (Episode: se, Score: Similarity(special.Title, se.Title)))
                .Where(x => x.Score >= 0.3)
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();
            if (byDate.Episode is not null)
                return byDate.Episode;
        }

        var byTitle = season0
            .Select(se => (Episode: se, Score: Similarity(special.Title, se.Title)))
            .Where(x => x.Score >= 0.85)
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();
        if (byTitle.Episode is not null)
            return byTitle.Episode;

        // The number is only trustworthy when nothing can contradict it: either side lacking an air date.
        var byNumber = season0.Find(se => se.EpisodeNumber == special.EpisodeNumber);
        return byNumber is not null && (special.AirDate is null || AirDateOf(byNumber) is null) ? byNumber : null;
    }

    internal static DateOnly? AirDateOf(SonarrEpisodeResource se) =>
        DateOnly.TryParse(se.AirDate, out var d) ? d : null;

    internal static double Similarity(string? a, string? b)
    {
        var ta = Tokens(a);
        var tb = Tokens(b);
        if (ta.Count == 0 || tb.Count == 0)
            return 0;
        return (double)ta.Intersect(tb).Count() / ta.Union(tb).Count();
    }

    private static HashSet<string> Tokens(string? s) =>
        s is null ? [] : [.. NonAlphanumeric().Split(s.ToLowerInvariant()).Where(t => t.Length > 0)];

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
