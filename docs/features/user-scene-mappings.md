# User Scene Mappings (User Alternative Titles)

> **Status:** stable · **Since:** unreleased (next release on `4.0.19.2979` base) · **Surface:** API (`POST /api/v3/scenemapping/user/import`)

## What it does

Adds a bulk import endpoint that upserts missing French/Quebec series titles as
**user scene mappings** (`Type=User` rows in the SceneMappings table). Once present,
user titles behave like any other scene mapping everywhere titles are matched:
indexer search scene titles, release parsing, file import identification — and they
appear in the series page's *Alternate Titles* list automatically.

This is the Sonarr port of the Radarr fork's
[User Alternative Titles](https://github.com/KrZ-W/Radarr/blob/personal/all-features-master/docs/features/user-alternative-titles.md)
feature, adapted to Sonarr's very different title architecture.

## Why it exists

TheTVDB and Sonarr's scene-mapping services lack many French/Quebec series titles,
so French release names often fail to match their series. Sonarr has no per-series
alternative-titles table to add them to — alternate titles come exclusively from the
scene-mapping providers (services.sonarr.tv and XEM), with no user-facing way to add
one.

## Why no "survive refreshes" patch (unlike Radarr)

Sonarr's `SceneMappingService.UpdateMappings()` clears rows **per provider type**
(`repository.Clear(providerTypeName)`) before re-inserting that provider's fresh
data. Rows with `Type=User` match no provider and are therefore never deleted by
mapping updates or series refreshes — survival is architectural. (This is exactly
the property the Radarr patch had to build by hand.)

## Behavior

### Import endpoint

`POST /api/v3/scenemapping/user/import` accepts the curated dataset format
(tvdb-keyed):

```json
[
  {
    "tvdbId": 81189,
    "imdbId": "tt0903747",
    "seriesTitle": "Breaking Bad",
    "year": 2008,
    "missingFrenchTitles": [
      { "title": "Le Chimiste d'Albuquerque", "region": "QC" }
    ]
  }
]
```

and returns:

```json
{
  "seriesProcessed": 1,
  "titlesAdded": 1,
  "titlesSkipped": 0,
  "titlesGuardedLibrary": 0,
  "titlesConflictingMapping": 0,
  "titlesUnsearchable": 0,
  "titlesAlreadyPresent": 0,
  "seriesNotFound": [],
  "seriesFailed": []
}
```

`titlesSkipped` is the sum of the four breakdown counters: `titlesGuardedLibrary` (a
parse term of the title is another library series' title), `titlesConflictingMapping`
(a parse term already maps to a different tvdbId), `titlesUnsearchable` (folds to
something the scene-name search filter would discard) and `titlesAlreadyPresent`
(the series' own title, or every parse term already mapped for it). `seriesFailed`
lists rows whose import threw (`"<title> (<year>) [tvdb:<id>]: <error>"`); the rest of
the request still completes. The request is validated before any work starts: at most
5000 series per request, 100 titles per series and 500 characters per title, otherwise
HTTP 400. `titles` is accepted as a neutral alias of `missingFrenchTitles`.

Rules:

- Series are resolved by `tvdbId`, falling back to `imdbId`, against library series
  only. Unmatched entries are listed in `seriesNotFound` as `Title (Year) [tvdb:ID]`.
- **Idempotent:** titles whose parse term already exists for the series (any
  provider) are skipped; the full dataset can be re-imported safely.
- A title equal to the series' own title is skipped.
- A title whose parse term is already mapped to a **different** series is refused
  (logged as a warning). This guard is stricter than Radarr's because in Sonarr a
  duplicated parse term makes `FindSceneMapping` throw
  `InvalidSceneMappingException` for every matching release. Titles equal to another
  library series' title are refused for the same reason — checked against every
  spelling the title would be stored under, and a title is taken whole or not at
  all, never half-inserted.
- `region` is stored in the mapping's `Comment` field.
- Mappings carry no season constraints (`SeasonNumber`/`SceneSeasonNumber` null), so
  they apply to searches for every season.

### Search-term normalization and parse terms

`GetSceneNames` silently excludes search terms containing any character above
U+00FF (its `IsEnglish` check). Accented French letters pass, but ligatures, curly
quotes, and typographic spaces/dashes don't — so the stored **search term** is
folded to Latin-1: ligatures (`œ→oe`, `æ→ae`, `ß→ss`) and `…→...` explicitly,
spaces/dashes/quotes by Unicode category (`\p{Zs}`, `\p{Pd}`, `\p{Pi}\p{Pf}`), so
characters real French typography uses — narrow no-break space, non-breaking
hyphen — are handled too. A title that still contains an unsearchable character
after folding is **skipped with a warning** rather than stored as a row that would
never contribute a query. The display title keeps the original typography.

**Parse terms** follow upstream's convention of deriving from `Title`, so a release
named with the original spelling resolves. When the folded spelling produces a
*different* parse term, a **second row** is inserted for it — releases returned by
an indexer are named after the folded title we searched with, so both spellings
must resolve. One imported title therefore yields one or two rows; the summary
counts titles, not rows.

## Usage

```bash
curl -X POST "http://<host>:8989/api/v3/scenemapping/user/import" \
  -H "X-Api-Key: <api-key>" \
  -H "Content-Type: application/json" \
  -d @curated-tv.json
```

Verify: the series page's *Alternate Titles* list, or
`GET /api/v3/series/<id>` → `alternateTitles`.

## Edge cases

- Scene mappings are global, not per-series rows: user mappings survive series
  deletion, and re-adding a series needs no re-import.
- The endpoint does not update or delete existing user rows; to correct a bad
  title, delete the row from the `SceneMappings` table and re-import.
- Episode numbering differences (TVDB vs scene order) remain XEM's job — user
  mappings only affect series-title matching.
- The collision guards see the library and mapping table **as they are at import
  time**. If a provider update later publishes the same parse term for a different
  series, or you add a series whose title collides with an imported mapping,
  `FindSceneMapping` can throw `InvalidSceneMappingException` for affected
  releases. Upstream has the same exposure between its own providers; the fix is to
  delete the offending user row.

## Architecture

The endpoint is thin: it validates the request shape, maps the resource onto a neutral
`UserSceneMappingImportRequest`, calls `IUserSceneMappingImportService` and maps the
result back. Everything that decides *what happens* lives in Core:

| Piece | Responsibility |
|---|---|
| `UserSceneMappingImportService` | per-row pipeline: resolve series (tvdb → imdb), drop blank titles, library-title guard, upsert, count; a row that throws lands in `seriesFailed` and the next row still runs |
| `SceneMappingService.UpsertUserMappingsDetailed` | normalisation, parse terms, the mapping-table guard, idempotency and insertion, reporting why each title was skipped (`UpsertUserMappings` still returns the added rows) |
| `SceneMappingService.GetParseTerms` / `NormalizeSearchTerm` | shared by the library guard and the upsert, so both compare what is actually stored |

The library guard builds one `GetAllSeries` lookup per request rather than a lookup per
title, because `FindByTitle` throws when two library series share a clean title (The
Office UK/US). The `missingFrenchTitles` field name is a property of the curated
dataset and exists only in the API layer (`UserSceneMappingImportResourceMapper`).

## Upstream

Requested upstream and closed as **not planned** (state as of 2026-09-09; this fork is not
affiliated with the Sonarr team and does not submit upstream):

- [Sonarr#8806](https://github.com/Sonarr/Sonarr/issues/8806) — *Allow user-added alternate
  titles to be used for search* (closed not planned, July 2026): "likely something we'll come
  back to, but at the moment it's not something we're looking to add." This feature is that
  request.
- [Sonarr#6233](https://github.com/Sonarr/Sonarr/issues/6233) — *Custom title search or
  Alternative title name* and [Sonarr#6166](https://github.com/Sonarr/Sonarr/issues/6166) —
  *Sonarr Custom Search* (both closed not planned 2023): upstream's position is that aliases
  should be requested through the scene-mapping form so everyone benefits.
- [Sonarr#8058](https://github.com/Sonarr/Sonarr/issues/8058) — *Allow disabling alias search
  per show* (closed not planned 2025): "no plans to make aliases configurable locally".
- [Sonarr#8100](https://github.com/Sonarr/Sonarr/issues/8100) — original-language series names
  (closed not planned 2025), pointing at the open
  [Sonarr#269 TVDB Data in other Languages](https://github.com/Sonarr/Sonarr/issues/269) as the
  sanctioned track.

## Source

Commits: `7c8d9d636` (feature), plus the review fixes on `feat/user-alt-titles`
(non-throwing library guard, parse terms per upstream convention with both
spellings, category-based folding). Key files:
`DataAugmentation/Scene/SceneMappingService.cs` (`UpsertUserMappings` + guards +
normalization), `Sonarr.Api.V3/SceneMappings/UserSceneMappingController.cs`
(endpoint), `Sonarr.Api.V3/SceneMappings/UserSceneMappingImportResource.cs` (DTOs).

Refactor: `212a9dea2` moved the pipeline into Core. Key files:
`DataAugmentation/Scene/UserSceneMappingImportService.cs`,
`DataAugmentation/Scene/UserSceneMappingImportRequest.cs`,
`DataAugmentation/Scene/UserSceneMappingImportResult.cs` (incl. `UserSceneMappingUpsertResult`),
`Sonarr.Api.V3/SceneMappings/UserSceneMappingImportResourceMapper.cs` (validation + mapping).
Tests: `NzbDrone.Core.Test/DataAugmentation/Scene/UserSceneMappingImportServiceFixture.cs`,
`.../SceneMappingServiceUpsertReasonsFixture.cs`,
`NzbDrone.Api.Test/v3/SceneMappings/UserSceneMappingImportResourceMapperTests.cs`.
