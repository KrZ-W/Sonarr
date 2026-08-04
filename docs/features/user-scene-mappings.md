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

and returns `{seriesProcessed, titlesAdded, titlesSkipped, seriesNotFound}`.

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

## Source

Commits: `7c8d9d636` (feature), plus the review fixes on `feat/user-alt-titles`
(non-throwing library guard, parse terms per upstream convention with both
spellings, category-based folding). Key files:
`DataAugmentation/Scene/SceneMappingService.cs` (`UpsertUserMappings` + guards +
normalization), `Sonarr.Api.V3/SceneMappings/UserSceneMappingController.cs`
(endpoint), `Sonarr.Api.V3/SceneMappings/UserSceneMappingImportResource.cs` (DTOs).
