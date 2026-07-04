# Season-Pack Partial Fill

> **Status:** stable · **Since:** `v4.0.17.2950+krzw.1` · **Surface:** Settings → Media Management

## What it does

Lets a **full-season pack** be grabbed when it would **fill missing episodes** and/or
**upgrade some** (but not necessarily all) episodes of a season — instead of the upstream
all-or-nothing behavior. Adds an opt-in **Allow Season Pack Upgrades** setting.

Also lets a **single-episode search** (interactive, automatic, and anime searches alike)
grab a matching full-season pack, which upstream rejects outright.

## Why it exists

In stock Sonarr a partially-complete season could never be filled from a season pack:

- `UpgradeDiskSpecification` rejected the whole grab as soon as **one** already-present
  episode wasn't an upgrade — even if other episodes were missing.
- A single-episode search (interactive or automatic, standard or anime) was independently
  rejected by `SingleEpisodeSearchMatchSpecification` as a "Full season pack".

So if you had 8 of 10 episodes, a season pack containing the 2 missing ones was refused.

This back-ports upstream **v5's** "season pack upgrade" setting to the **v4** line and
extends it to single-episode searches (no v5 equivalent for that part).

## Settings

**Settings → Media Management** gains **Allow Season Pack Upgrades**:

| Mode | Value | Behavior |
|---|---|---|
| **All** | `All` (default) | A season pack must be a quality/CF upgrade (or fill) for **all** episodes — equivalent to upstream behavior. |
| **Threshold** | `Threshold` | Accept when the share of episodes the pack would **fill or upgrade** meets the configured **threshold percentage**. |
| **Any** | `Any` | Accept if the pack would fill or upgrade **at least one** episode. |

When **Threshold** is selected, a companion **threshold percentage**
(`SeasonPackUpgradeThreshold`, default `100`) controls the cutoff.

> Also settable via the API: fields `seasonPackUpgrade` and `seasonPackUpgradeThreshold`
> on `/api/v3/config/mediamanagement`.

## How it works

- For full-season releases, `UpgradeDiskSpecification` now counts **missing episodes as
  fillable** plus any genuinely-upgradable existing files, and accepts per the configured
  mode/threshold. With **All** (the default) the effective threshold is 100% and behavior
  is unchanged.
- **Only monitored missing episodes count.** Episodes you deliberately unmonitored are
  excluded from both sides of the ratio — a pack is never grabbed just to fill episodes
  you opted out of (and a pack that helps nothing else is rejected outright).
- **No same-release refill loops (Any/Threshold).** If the identical release was grabbed
  before and its download imported *without* producing a file for a still-missing
  episode, the pack demonstrably doesn't contain that episode, so it no longer counts as
  fillable — otherwise the same pack would be re-grabbed on every RSS sync once the 12 h
  history grace expired. A previous grab that never imported (failed/stalled download)
  still counts, so failed-download recovery is unaffected.
- Per-episode evaluation still routes through the priority-aware `IsUpgradable`, so
  [Custom Format Priority Mode](custom-format-priority-mode.md) behavior is preserved when
  grabbing packs.
- When the setting is enabled, `SingleEpisodeSearchMatchSpecification` lets a matching
  full-season pack through so a single-episode search can grab it.
- **Import is unchanged:** only the missing/upgradable episodes from the pack are
  imported — you don't get duplicate/downgrade imports of episodes you already have.

## Configuration

1. **Settings → Media Management → Allow Season Pack Upgrades** — choose:
   - `All` to keep stock behavior,
   - `Any` to grab a pack that helps with even one episode, or
   - `Threshold` and set a percentage (e.g. `50`) for a middle ground.
2. Save.

## Behavior & edge cases

- **Default is safe.** `All` reproduces upstream behavior, so upgrading the fork doesn't
  change grabbing until you opt in.
- **`Any` is aggressive.** It will grab a whole season pack to fill a single episode —
  fine if bandwidth/seeding isn't a concern, wasteful otherwise. `Threshold` is the
  middle ground.

## Source

Commit: `06fcf377e`. Key files:
`MediaFiles/SeasonPackUpgradeType.cs` (enum `All`/`Threshold`/`Any`),
`DecisionEngine/Specifications/UpgradeDiskSpecification.cs`,
`DecisionEngine/Specifications/Search/SingleEpisodeSearchMatchSpecification.cs`,
`Configuration/ConfigService.cs`, `Sonarr.Api.V3/Config/MediaManagementConfigResource.cs`.
