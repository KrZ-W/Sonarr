# Per-Profile Size Limits

> **Status:** stable · **Since:** `v4.0.19.2979+krzw.24` · **Surface:** Settings → Profiles → Quality Profile editor, `/api/v3/qualityprofile`

## What it does

Lets a **quality profile override the size limits** (minimum / preferred / maximum, in
**MB per minute** of runtime) of any quality it contains. Upstream keeps those limits only
in the global **Quality Definitions** (Settings → Quality), so every profile shares one
size window per quality.

With this feature a "1080p Light" profile can cap `HDTV-1080p` / `WEBDL-1080p` at
8 MB/min while the normal 1080p profile keeps the global 60 MB/min, without touching the
global definitions and without absolute-GB custom formats (which do not scale with runtime).

## Why it exists

Qualities are hard-coded, so there is no way to create a "smaller 1080p" quality. The
only upstream lever is a custom format with a *Size* condition, but that is an absolute
size in GB: it cannot express "8 MB per minute" across 22-minute sitcoms and 60-minute
dramas. The size limits already scale with runtime; they just were not per profile.

## Configuration

**UI.** In the quality profile editor each quality row (and each group row) shows three
small inputs to the left of the drag handle: **Min · Preferred · Max**, in MB/min. Leave a
field empty to inherit the global Quality Definition value. Set **Max to `0`** for
"unlimited" in this profile even when the global definition has a maximum.
The inputs are hidden while *Edit Groups* mode is on.

**API.** The three fields live on each item of `items` (and on group items) of
`GET/PUT/POST /api/v3/qualityprofile`:

```json
{
  "quality": { "id": 9, "name": "HDTV-1080p" },
  "items": [],
  "allowed": true,
  "minSize": null,
  "preferredSize": 6,
  "maxSize": 8
}
```

- `null` (or absent) = not overridden.
- `maxSize: 0` = unlimited (same meaning as the global definition), distinct from "not overridden".
- Values are validated to `0 … 1000` MB/min (the same bounds as the global definitions).

## Resolution order

For a release of quality *Q* evaluated against a series whose profile is *P*, each of the
three limits is resolved independently:

```
member item override  ??  group item override  ??  global Quality Definition
```

- A **member** is the quality's own row (top-level, or inside a group).
- A **group** override applies to every member of the group that has no override of its own.
  Groups in Sonarr mean "these qualities are equal", so a group-level size window is the
  natural way to say "1080p, any source, at most 8 MB/min"; a member override is more
  specific and therefore wins.
- The UI edits top-level items and groups. Member overrides inside a group are API-only.

## Where it applies

- **`AcceptableSizeSpecification`** (grab-time size check) uses the effective min / max.
  It keeps upstream's per-episode runtime sum, so season packs are checked against
  `limit × Σ episode runtime`.
- **`DownloadDecisionComparer.CompareSize`** (release ordering) uses the effective
  preferred size. As upstream, it uses the series runtime, not the per-episode sum.
- Manual import and library rescans do **not** check size (upstream behaviour, unchanged).
  Limits act at grab time only: a "Light" profile will not shrink existing oversized files,
  and upgrade logic ignores size.

## Validation

`PUT`/`POST` are rejected with a 400 when:

- any override is outside `0 … 1000`, or
- the **effective** triple of any quality that has an override is not ordered
  `min ≤ preferred ≤ max` (a `max` of `0` / unlimited is skipped). This is checked on the
  resolved values, so an override of `preferredSize: 70` with an inherited global
  `maxSize: 60` is refused instead of letting the comparer prefer releases the size
  specification then rejects.

## Storage

Profile items are an embedded JSON document in the `QualityProfiles.Items` column, so there
is **no migration**. Nulls are not written, so a profile without overrides serialises
byte-for-byte as before; rows written before the feature deserialise with all three fields
`null`. Covered by `QualityProfileItemsConverterFixture`.

## Behavior & edge cases

- A series whose profile does not contain the release's quality (should not happen; every
  profile lists every quality) falls back to the global definition.
- Grouping a quality in the editor keeps its own override; ungrouping drops the group's
  override (the group ceases to exist).
- The global **Reset Definitions** action does not touch profile overrides.
- Saving a profile from the editor preserves overrides set through the API: the editor
  round-trips the item objects it received.

## Code map

- `NzbDrone.Core/Profiles/Qualities/QualityProfileQualityItem.cs` — `MinSize` / `MaxSize` / `PreferredSize`.
- `NzbDrone.Core/Profiles/Qualities/QualityProfileSizeLimits.cs` — `Resolve` / `FindItems` / `EffectiveSizeLimits`.
- `NzbDrone.Core/DecisionEngine/Specifications/AcceptableSizeSpecification.cs`,
  `NzbDrone.Core/DecisionEngine/DownloadDecisionComparer.cs` — consumers.
- `Sonarr.Api.V3/Profiles/Quality/QualityProfileResource.cs`, `QualityItemSizeLimitsValidator.cs`, `QualityProfileController.cs`.
- `frontend/src/Settings/Profiles/Quality/QualityProfileItemSizeLimits.js` and the item / group / drag-source plumbing.
- Tests: `AcceptableSizeSpecificationFixture`, `PrioritizeDownloadDecisionFixture`,
  `QualityProfileSizeLimitsFixture`, `QualityProfileItemsConverterFixture`.

All hunks in upstream files are marked `krzw(profile-size-limits)`.
