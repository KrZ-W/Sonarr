# IMDb Title Provider

> **Status:** stable · **Since:** `v4.0.19.2979+krzw.16` · **Surface:** Settings → Metadata → *IMDb Title Provider* (`/api/v3/config/metadata`), scheduled task `ImdbTitleDatasetRefresh` (also `POST /api/v3/command {"name":"ImdbTitleDatasetRefresh"}`), health check

## What it does

Downloads IMDb's public *title.akas* dataset on a schedule, keeps the rows for the
regions/languages you care about in a local SQLite index, and adds the titles your
series are missing as **user scene mappings** through the
[User Scene Mappings](user-scene-mappings.md) importer. A series added while the
feature is on gets its titles within seconds; the whole library is re-checked after
every dataset refresh.

This is the Sonarr port of the Radarr fork's
[IMDb Title Provider](https://github.com/KrZ-W/Radarr/blob/personal/all-features-master/docs/features/imdb-title-provider.md)
and replaces the external `process_akas_series.py` feeder (tvdb-keyed curated JSON).

## Why it exists

TheTVDB and Sonarr's scene-mapping services lack many French/Quebec series titles;
IMDb has most of them. The import endpoint solved the *storage* side (user mappings
are refresh-proof and feed search and parsing), but producing the dataset still meant
running a script against a 2 GB dump and re-posting it after every new series. With
the provider built in, the only moving part is a setting.

## Behavior

### Dataset refresh (scheduled task)

- Runs every *Refresh Interval* days (default 7, minimum 1) while the feature is
  enabled; the task shows in *System → Tasks* and can be run manually. Disabling the
  feature sets the task interval to 0 (never scheduled) on the next settings save.
- Downloads `https://datasets.imdbws.com/title.akas.tsv.gz` to
  `<AppData>/imdb-akas.db.download` with `If-None-Match` / `If-Modified-Since` taken
  from the previous build. A `304 Not Modified` (or an unchanged `ETag`) skips the
  rebuild. Changing *Regions* or *Languages* forces a full download.
- Streams the gzip TSV line by line (the full dump is never held in memory) and writes
  only the kept rows to `<AppData>/imdb-akas.db.tmp`, then renames it over
  `<AppData>/imdb-akas.db`. A failed download or parse leaves the previous index in
  place and is logged as an error.
- A row is kept when `region ∈ Regions` **or** `language ∈ Languages`, unless its
  `attributes` contain `literal` (literal translations are never release names). No
  other attribute-based exclusion. Row counts (scanned / kept) are logged at Info.
- After the task (downloaded or not) every library series with an IMDb id is synced.

### Applying titles

For one series:

1. Read its rows from the index (keyed on the series' `imdbId`; series without one
   are skipped, there is no TVDB→IMDb lookup here).
2. Normalise every title the way the reference feeder did (NFD, strip combining marks,
   lowercase, keep only `[a-z0-9]`) and drop rows whose normalised title equals the
   series' title or any scene mapping already stored for its `tvdbId` (any provider).
   Duplicate dataset rows collapse to the first one.
3. Build one `UserSceneMappingImportRequest` (`{tvdbId, imdbId, seriesTitle, year,
   titles[{title, region}]}`) and hand it to `IUserSceneMappingImportService.Import`.
   Nothing is called when nothing is missing. All guards (library-title guard,
   conflicting-mapping guard, unsearchable folding, idempotency) stay in the importer.

`region` travels as-is into the mapping's `Comment` (Sonarr searches every scene
mapping, so unlike Radarr there is no region-tag rule to apply). The configured
*Languages* only decide which rows are kept; Sonarr mappings carry no language.

Triggers:

| When | Scope |
|---|---|
| `ImdbTitleDatasetRefresh` task finished | all library series with an IMDb id |
| `SeriesAddedEvent` | that series (the first metadata refresh runs afterwards; user mappings are provider-less rows and survive it) |
| `SeriesUpdatedEvent` (end of a series' metadata refresh) | that series |

Every trigger is a no-op while the feature is disabled or the index does not exist.

### Health check

While enabled: **warning** when `imdb-akas.db` is missing/unreadable ("run the task"),
or when it was built more than *2 × Refresh Interval* days ago (a scheduled refresh has
been failing). Re-evaluated on settings save, after every task run and on the regular
health check schedule.

## Usage

1. *Settings → Metadata → IMDb Title Provider*: tick **Enable**, keep `CA,FR` / `fr` or
   adjust, save. (Sonarr had no saveable options on that page before; the page now has
   a Save button.)
2. *System → Tasks → Imdb Title Dataset Refresh → run*. The first run downloads
   ~300 MB and takes a few minutes; watch the log for `IMDb akas dataset indexed` and
   `IMDb titles applied to library`.
3. Verify on a series: its *Alternate Titles* list (or `GET /api/v3/series/<id>` →
   `alternateTitles`) shows the new titles, and a French release name now parses to it.

To stop: untick **Enable** — the task stops being scheduled, the event hooks become
no-ops, and existing user mappings stay (delete rows from `SceneMappings` if you want
them gone).

## Edge cases

- **Rows in a language you did not configure** (e.g. `region=CA, language=en`) are
  still kept because of their region and imported like any other title. Narrow
  *Regions* if you do not want English-Canadian titles as mappings.
- **Which duplicate wins:** two dataset rows normalising to the same text keep the
  first in dataset order (usually the lowest `ordering`).
- Titles the importer refuses (another series' title, a conflicting mapping, an
  unsearchable spelling) are counted in the import summary and logged as warnings;
  they are retried on every sync and refused again until the conflict goes away.
- The index is a cache, not part of `sonarr.db`: it is not backed up, not migrated and
  can be deleted at any time (the next task run rebuilds it, the health check flags it
  meanwhile).
- Nothing is ever *removed* by this feature.

## Dataset licence

The IMDb datasets are made available by IMDb for **personal and non-commercial use**
only (see <https://developer.imdb.com/non-commercial-datasets/>). This fork downloads
the dataset at runtime on the operator's own instance, stores only a filtered index
locally and **never redistributes** any part of it; the container image contains no
IMDb data. Using this feature means accepting IMDb's terms yourself.

## Architecture

| Piece | Responsibility |
|---|---|
| `Tv/ImdbTitles/ImdbAkasFilter` | which rows to keep (regions ∪ languages, minus `literal`), list parsing, filter signature |
| `Tv/ImdbTitles/ImdbAkasTsvParser` | streaming gzip/TSV reader → `ImdbAkasRow`, `\N` → null, row counts |
| `Tv/ImdbTitles/ImdbAkasDatabase` | the SQLite index (`titles` + `meta` tables), atomic `.tmp` → live rename, per-series lookup |
| `Tv/ImdbTitles/ImdbTitleDatasetRefreshService` | the task: conditional download, parse, build, event, library sync |
| `Tv/ImdbTitles/ImdbTitleSyncService` | per-series request building (normalisation, dedup against existing mappings), event handlers, calls into `IUserSceneMappingImportService` |
| `Tv/ImdbTitles/ImdbTitleNormalizer` | the "already has" key |
| `HealthCheck/Checks/ImdbTitleDatasetCheck` | missing / stale index warning |
| `Jobs/TaskManager` (marked hunk) | scheduled-task registration and interval update on settings save |
| `Configuration/ConfigService`, `Sonarr.Api.V3/Config/MetadataConfig*` (new endpoint), `frontend/src/Settings/Metadata/Options/MetadataOptions.tsx` + `Store/Actions/Settings/metadataOptions.js` | the four settings |

Nothing in TVDB/skyhook fetching, the scene-mapping providers, the import endpoint or
release matching is touched; the feature is purely a producer of
`UserSceneMappingImportRequest`s.

## Upstream

Same position as [User Scene Mappings](user-scene-mappings.md#upstream): upstream wants
aliases requested through the scene-mapping form
([Sonarr#8806](https://github.com/Sonarr/Sonarr/issues/8806),
[Sonarr#6233](https://github.com/Sonarr/Sonarr/issues/6233)). Not submitted upstream.

## Source

Branch `feature/imdb-title-provider-main`. Key files: `src/NzbDrone.Core/Tv/ImdbTitles/*`,
`src/NzbDrone.Core/HealthCheck/Checks/ImdbTitleDatasetCheck.cs`,
`src/NzbDrone.Core/Jobs/TaskManager.cs`, `src/Sonarr.Api.V3/Config/MetadataConfigController.cs`,
`frontend/src/Settings/Metadata/`. Tests: `NzbDrone.Core.Test/TvTests/ImdbTitleTests/*`,
`NzbDrone.Core.Test/HealthCheck/Checks/ImdbTitleDatasetCheckFixture.cs`.
