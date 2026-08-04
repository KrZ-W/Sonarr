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
  library series' title are refused for the same reason.
- `region` is stored in the mapping's `Comment` field.
- Mappings carry no season constraints (`SeasonNumber`/`SceneSeasonNumber` null), so
  they apply to searches for every season.

### Search-term normalization

`GetSceneNames` silently excludes search terms containing any character above
U+00FF (its `IsEnglish` check). Accented French letters pass, but `œ`, curly
quotes, and em-dashes don't — so the import normalizes the stored search term to
Latin-1 (`œ→oe`, `’→'`, `—→-`, `…→...`) while keeping the original typography in
the display title.

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

## Source

Commit: `7c8d9d636`. Key files:
`DataAugmentation/Scene/SceneMappingService.cs` (`UpsertUserMappings` + guards +
normalization), `Sonarr.Api.V3/SceneMappings/UserSceneMappingController.cs`
(endpoint), `Sonarr.Api.V3/SceneMappings/UserSceneMappingImportResource.cs` (DTOs).
