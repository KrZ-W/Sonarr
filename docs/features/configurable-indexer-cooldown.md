# Configurable Indexer Cooldown

> **Status:** stable · **Since:** `v4.0.17.2950+krzw.1` · **Surface:** Settings → Indexers → Options (advanced)

## What it does

Makes the **indexer back-off / escalation schedule** editable. When an indexer fails,
Sonarr disables it for an increasing amount of time (the "cooldown"). Upstream hard-codes
this schedule; this fork exposes it as a setting so you can make a flaky indexer back off
faster or slower.

## Why it exists

The default escalation is `[0, 1, 5, 15, 30, 60, 180, 360, 720, 1440]` minutes and is not
user-adjustable. Depending on your indexers you may want shorter cooldowns (retry sooner)
or longer ones (stop hammering a rate-limited indexer).

## Settings

**Settings → Indexers → Options** (an *advanced* setting) gains:

| Setting | Format |
|---|---|
| **Indexer Cooldown Periods** | CSV of minutes, e.g. `0,2,10,30,120` |

Rules:

- The **first value must be `0`** (auto-prepended if you omit it).
- **Empty** falls back to the upstream default `0,1,5,15,30,60,180,360,720,1440`.
- Values are the successive cooldown durations after each consecutive failure.
- Values must be **whole, non-negative minutes**. Saving anything else (a negative
  number, a decimal, text, or a value above `35791394`) is rejected with a validation
  error instead of being silently ignored.

## Behavior & edge cases

- **Indexers only.** Download clients, notifications, and import lists keep their existing
  cap-at-5 escalation behavior (enforced via a `_maximumEscalationLevelOverride` backing
  field on the shared `ProviderStatusServiceBase`).
- Once the failure count exceeds the number of entries, the last (longest) period
  continues to apply.
- The daily housekeeping task that clamps "too far in the future" indexer status times
  bounds them with **the configured schedule**, not the default table, so a long custom
  cooldown is never cut short by housekeeping (or by a restart, which runs it too). A
  persisted escalation level past the end of the table is clamped instead of making the
  housekeeper throw.

## Configuration

1. **Settings → Indexers** — make sure *Advanced Settings* (top-right toggle) is shown.
2. **Options → Indexer Cooldown Periods** — enter your CSV, e.g. `0,2,10,30,120`.
3. Save.

## Upstream

No open or declined upstream Sonarr request for an editable back-off schedule was found
(searched 2026-09-09). [Sonarr#3132](https://github.com/Sonarr/Sonarr/issues/3132) (closed
2023) was about aggregator error handling, not the schedule.

## Source

Commit: `94de4c188`. Key files: `Configuration/ConfigService.cs`,
`Indexers/IndexerCooldownPeriods.cs` (parser shared by service, housekeeper and validator),
`Housekeeping/Housekeepers/FixFutureIndexerStatusTimes.cs`,
`Indexers/IndexerStatusService.cs`, `ThingiProvider/Status/ProviderStatusServiceBase.cs`,
`Sonarr.Api.V3/Config/IndexerConfigResource.cs`,
`frontend/src/Settings/Indexers/Options/IndexerOptions.js`.

> This feature also exists in the KrZ-W forks of **Radarr** and **Prowlarr**.
