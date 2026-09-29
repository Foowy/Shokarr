using Shokarr.Models;

namespace Shokarr.Services;

/// <summary>Maps an AniDB episode to its Sonarr episode.</summary>
public static class SonarrEpisodeMatcher
{
    // AniDB numbers a single-entry run the same as Sonarr's AbsoluteEpisodeNumber, but gives each later cour its own
    // entry numbered from 1 while TheTVDB keeps counting, so the number alone can point at an earlier cour. The air
    // date settles which episode it is; the number is only trusted when nothing contradicts it. Series added before
    // Shokarr set seriesType=anime stay Standard and expose no absolute numbers -- fall back to the old
    // (season 1, N) match for those so an upgrade doesn't silently unmap every legacy series.
    public static SonarrEpisodeResource? Match(List<SonarrEpisodeResource> sonarrEpisodes, bool anySonarrAbsolute, MissingEpisodeInfo ep)
    {
        if (ep.IsSpecial)
            return SpecialMatcher.Match(sonarrEpisodes, ep);

        var byNumber = anySonarrAbsolute
            ? sonarrEpisodes.Find(se => se.AbsoluteEpisodeNumber == ep.EpisodeNumber)
            : sonarrEpisodes.Find(se => se.SeasonNumber == 1 && se.EpisodeNumber == ep.EpisodeNumber);
        if (ep.AirDate is not { } aired)
            return byNumber;

        var byDate = sonarrEpisodes
            .Where(se => se.SeasonNumber > 0 && SpecialMatcher.AirDateOf(se) is { } d && Math.Abs(d.DayNumber - aired.DayNumber) <= SpecialMatcher.MaxAirDateDriftDays)
            .ToList();
        if (byDate.Count == 1)
            return byDate[0];
        if (byDate.Count > 1)
            return byNumber is not null && byDate.Contains(byNumber)
                ? byNumber
                : byDate
                    .Select(se => (Episode: se, Score: SpecialMatcher.Similarity(ep.Title, se.Title)))
                    .Where(x => x.Score >= 0.3)
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault().Episode;

        return byNumber is not null && SpecialMatcher.AirDateOf(byNumber) is null ? byNumber : null;
    }
}
