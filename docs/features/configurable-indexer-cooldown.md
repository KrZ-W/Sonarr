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

## Behavior & edge cases

- **Indexers only.** Download clients, notifications, and import lists keep their existing
  cap-at-5 escalation behavior (enforced via a `_maximumEscalationLevelOverride` backing
  field on the shared `ProviderStatusServiceBase`).
- Once the failure count exceeds the number of entries, the last (longest) period
  continues to apply.

## Configuration

1. **Settings → Indexers** — make sure *Advanced Settings* (top-right toggle) is shown.
2. **Options → Indexer Cooldown Periods** — enter your CSV, e.g. `0,2,10,30,120`.
3. Save.

## Source

Commit: `94de4c188`. Key files: `Configuration/ConfigService.cs`,
`Indexers/IndexerStatusService.cs`, `ThingiProvider/Status/ProviderStatusServiceBase.cs`,
`Sonarr.Api.V3/Config/IndexerConfigResource.cs`,
`frontend/src/Settings/Indexers/Options/IndexerOptions.js`.

> This feature also exists in the KrZ-W forks of **Radarr** and **Prowlarr**.
