# Shoko Arr

<img src="assets/master_shokosonarr.png" width="200" alt="Shoko Arr logo">

[![CI](https://github.com/Foowy/ShokoArr/actions/workflows/ci.yml/badge.svg)](https://github.com/Foowy/ShokoArr/actions/workflows/ci.yml)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)

> **Renamed from ShokoSonarr.** The plugin now handles Radarr as well as Sonarr, so the "Sonarr" name no longer fit. Existing installs upgrade in place: the plugin ID, stored settings and API routes are unchanged, and the old GitHub URLs redirect. The plugin folder and DLL are now `ShokoArr`, so remove any old `plugins/ShokoSonarr/` folder when installing manually.

A [ShokoServer](https://github.com/ShokoAnime/ShokoServer) plugin that scans your anime collection for missing episodes and bridges them to [Sonarr](https://sonarr.tv/) for automated download — with a related-series discovery feed and a [Radarr](https://radarr.video/) bolt-on for movie-type suggestions.

> **Targets Shoko 6.0 pre-releases.** Shoko Arr is built against pre-release versions of `Shoko.Abstractions` 6.0, and ShokoServer 6.0 itself is only available as daily/dev builds so far. Shoko's plugin API can still change between those builds, so a Shoko update may break Shoko Arr until a matching Shoko Arr release ships. It does not run on ShokoServer 5.x.

## ELI5

Shoko tracks what anime episodes you already have. Sonarr downloads episodes for you. This plugin sits between them: it scans your Shoko library for episodes you're missing, matches each series to the right one in Sonarr, and tells Sonarr "go find these specific episodes" — no manual searching, no adding whole seasons you already have. It also surfaces AniDB-related series you don't own at all yet (sequels, prequels, side stories) as one-click suggestions, routed to Sonarr or Radarr depending on whether the suggestion is a series or a movie.

## What it does

- **Scans** every already-inventoried Shoko series (on-demand from the dashboard, or on a configurable schedule) for episodes with no local file.
- **Matches** each series to Sonarr via its TMDB-linked TVDB ID, falling back to a confirmable title search when no TVDB link exists yet — the same confirmation flow is available directly on any series with no auto-resolved match.
- **Maps multi-cour series correctly**: Shoko's AniDB episode numbers are mapped onto Sonarr's season/episode layout via absolute episode numbers, and series are added to Sonarr as `anime` type. Specials are matched to Sonarr season 0 by air date and title rather than number alone, and skipped (never searched against a mismatched episode) when no confident match exists.
- **Bridges the gap**: adds the series to Sonarr if it isn't there yet (without triggering a full-series download), then triggers a targeted `EpisodeSearch` for just the missing episodes.
- **Reconciles automatically**: once Shoko confirms an episode was actually imported, the plugin unmonitors it in Sonarr so Sonarr's own RSS/automatic search stops re-fetching something you already have. A stale pending search that keeps failing (e.g. the Sonarr episode was deleted out-of-band) is dropped after 14 days instead of retrying forever.
- **Shows what Sonarr already has**: missing episodes Sonarr already has or is downloading get a badge, with a setting for whether they count as missing (held episodes are never searched).
- **Filters out noise**: optionally exclude specials (globally or per-series), and optionally hide episodes that haven't aired yet — since there's nothing for Sonarr to find until the air date passes.
- **Suggests what you're missing entirely**: surfaces AniDB-related series (sequels, prequels, side stories) you own zero episodes of, from series you already have. Movie-type suggestions route to Radarr; everything else routes to Sonarr, both via a confirmable title search.
- **Propagates Shoko's group metadata as a Sonarr tag** on add, so Sonarr-side automation can key off which franchise a series belongs to. A manual "Sync Tags" action retroactively tags series added before this existed.
- **Lets a series override the global quality profile/root folder** — useful for a group that should land somewhere different than everything else.
- **Bulk actions** — multi-select series to apply a specials override or trigger Add-to-Sonarr/Search across all of them at once.
- **Notifies** an optional Discord-compatible webhook when a search is triggered, a movie/series discovery is added, or a stale pending entry expires.
- **Registers native Shoko Actions**: "Scan for Missing Episodes" (global) and "Search Missing Episodes in Sonarr" (per series) appear in Shoko's Actions menu, alongside the dashboard.
- **Shows Sonarr's live reachability** in the dashboard header at a glance, instead of only after pressing Test.

## What it's not

The core missing-episode scan only covers series Shoko already has *some* files for — it doesn't manage Sonarr's quality/upgrade settings (bring your own quality profile), and it doesn't scan owned series for "missing Radarr movies" (Radarr only comes into play for unowned movie-type suggestions, since Shoko has no TMDB movie link data for anything it doesn't already own).

## Dashboard

Single-pane-of-glass UI embedded in Shoko's WebUI. Open it from Shoko's plugin pages list, or go directly to:

```
http://<your-shoko-host>:<port>/api/plugin/ShokoSonarr/dashboard
```

- **Connection health** — a persistent Sonarr-reachable/unreachable indicator in the header, refreshed every 60s.
- **Series list** — every series with missing episodes, expandable to see each episode and its status (`none` / `search-triggered`). Multi-select checkboxes drive the bulk-actions bar (specials override, Add to Sonarr/Search) across several series at once.
- **Specials toggle** — per-series Include/Exclude override for specials episodes; click the active button again to clear it back to the global default.
- **Profile override** — per-series Sonarr quality-profile/root-folder override, independent of the specials toggle.
- **Find Match** — for any series without an auto-resolved TVDB ID, search and confirm a Sonarr match directly from its row.
- **Suggestions** — AniDB-related series you own nothing of, with an Add to Sonarr or Add to Radarr action depending on type.
- **Sync Tags** — retroactively pushes the Shoko-group tag onto Sonarr series added before tag propagation existed.
- **Pending Searches** — view and cancel any in-flight Sonarr search the plugin has triggered but Shoko hasn't confirmed as imported yet.
- **History** — a log of past outcomes (triggered, imported, cancelled, expired) for episodes that were previously pending, since Pending Searches only shows what's still in-flight.
- **Theme picker** — System (follows OS dark/light), Ember, Deep Teal, AniBlackle, Neonglass, or Paper.
- **Settings** - Sonarr/Radarr connection, quality profile, root folder, scan interval, specials/unaired filters, whether episodes already in Sonarr count as missing, and notification webhook (see First-time setup).

### First-time setup

1. Open the ShokoArr dashboard and go to the **Settings** panel.
2. Enter your Sonarr base URL and API key (Sonarr, Settings, General).
3. Press **Test Connection** to load the quality profile and root folder dropdowns.
4. Pick a profile and folder, then **Save**.
5. Optionally repeat for Radarr if you want movie-type suggestions routed there. Scan interval (hours, 0 disables), include specials, hide unaired and the notification webhook are in the same panel.

Existing settings are migrated automatically on first start. Settings are stored in Shoko's plugin configuration (visible under `/api/v3/Configuration`), which Shoko WebUI cannot edit yet.

> Sonarr/Radarr must be directly reachable from the Shoko host. If either sits behind a reverse proxy with SSO/forward-auth, use its internal LAN address instead — API-key requests can't pass through an SSO login redirect.

## Installing

### Via Shoko's plugin manager (recommended)

This repo publishes a live [`manifest.json`](manifest.json) that ShokoServer's plugin manager can consume directly as a repository. It's kept up to date automatically: every tagged GitHub release adds its own entry (version, changelog, per-runtime archive + checksum) via CI.

1. In Shoko's WebUI, go to **Settings → Plugins** and add a new repository with:
   - **Name:** `Shoko Arr` (or anything you like)
   - **URL:** `https://raw.githubusercontent.com/Foowy/ShokoArr/master/manifest.json`
2. Shoko fetches the manifest and lists Shoko Arr as an installable package — pick a release and install it from there.
3. Once installed, enable **auto-upgrade** on the repository/package if you want future releases pulled in automatically; otherwise re-check the plugin manager after new releases.

### Manual deploy

```bash
dotnet publish ShokoArr -c Release -o <output-dir>
# copy <output-dir>/* into Shoko's plugins/ShokoArr/ directory, then restart Shoko Server
```

Plugin load only happens at ShokoServer startup — copying the DLL alone has no effect. Requires a Shoko Server build with plugin Abstractions 6.0 (currently the `dev`/`daily` channel).

## Building & testing

```bash
dotnet build -c Release
dotnet test -c Release
```

Over 100 tests cover missing-episode scanning (specials filtering, hidden/credits-type exclusion, unaired filtering, group/override propagation), Sonarr and Radarr search/reconciliation (success, failure, expiry, exception paths), the Sonarr and Radarr HTTP clients, series/movie matching, related-series discovery, notifications, and LiteDB settings persistence (including backward-compatible deserialization of pre-upgrade settings docs and cross-field override preservation).

## License

[GPL-3.0](LICENSE)
