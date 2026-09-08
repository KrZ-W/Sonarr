# Completed Download Handling (CDH) Interval

> **Status:** stable · **Since:** `v4.0.17.2950+krzw.1` · **Surface:** API only (`/api/v3/config/downloadclient`) — no UI field

## What it does

Two related changes to **Completed Download Handling** (the periodic job that processes
finished downloads, internally `RefreshMonitoredDownloads` / `ProcessMonitoredDownloads`):

1. **Configurable interval** — the run interval (hard-coded to 1 minute upstream) is now
   a setting, default 1 minute.
2. **Per-run logging** — each run logs a start marker and, on completion, its elapsed
   duration and tracked-download count, so a slow or hung run is visible.

## Why it exists

All `RequiresDiskAccess` commands serialize through a **single slot** in Sonarr's command
queue. A long or hung CDH run holds that slot and **starves manually-queued disk
commands** — manual imports and renames never get a turn while CDH is busy.

Because the next CDH run is scheduled from the **completion** of the previous one
(`LastExecution`), making the interval configurable creates a guaranteed **quiet gap**
after each run during which your manual disk commands can execute. The logging makes a
wedged run obvious: a "run starting" line with no matching completion.

## Settings

The interval is **API-only** — there is no field in the web UI. It lives in the
download-client config, field `checkForFinishedDownloadInterval` (minutes):

| Setting (API field) | Default | Notes |
|---|---|---|
| `checkForFinishedDownloadInterval` | `1` | Clamped to **≥ 1** minute; re-applied live when the config is saved (no restart needed). |

## Logging behavior

| Event | Level |
|---|---|
| CDH run starting | Debug |
| CDH run completed (duration + tracked-download count) | Debug |
| CDH run completed but exceeded the configured interval | **Warn** (escalated) |

A start line with no completion = a run that is still going or has hung.

## Stuck "Import Pending" self-heal

> **Since:** `v4.0.19.2979+krzw.10` · **Surface:** automatic — nothing to configure

A tracked download whose client item is no longer reported as `Completed` and that is
sitting in `ImportPending` or `ImportBlocked` is reverted to `Downloading`, and its
status messages are cleared.

**Why:** CDH returns early for any item the client does not report as `Completed`. So if
the client misreads a still-running download as complete even once — this fork has seen
it during external recheck/relocate operations — the item is promoted to `ImportPending`
/ `ImportBlocked` with an import warning attached, and the early return then means
nothing ever re-evaluates it. The queue entry stays stuck with a stale warning for the
rest of the download's life, and external queue cleaners that treat a warning as a failed
import will mark the grab failed **mid-download**.

**Scope:** only `ImportPending` and `ImportBlocked` are reverted. Settled states
(`Imported`, `Failed`, `Ignored`) are never touched, so the self-heal cannot resurrect a
download that has already concluded. Genuine failures are unaffected — a client item
reporting `Failed` is still handled by the failed-download path, which keys off the
client status rather than the tracked state.

Recovery is automatic on the next CDH run; no restart or queue intervention is needed.

**Where it runs:** the guard lives in both `CompletedDownloadService.Check` (reached by
`ImportBlocked` items via the download monitor) and `CompletedDownloadService.Import`
(reached by `ImportPending` items via download processing). `krzw.10` only had the
`Check` half, so `ImportPending` items — the originally reported case — never
self-healed; `krzw.11` added the `Import` half.

## Configuration

Set it via the API (there is no UI field). Read the current config, then PUT it back
with the new interval — e.g. `2`–`5` minutes if you do frequent manual imports:

```bash
BASE="http://localhost:8989"; KEY="<your-api-key>"

# read current value
curl -s -H "X-Api-Key: $KEY" "$BASE/api/v3/config/downloadclient" | jq

# set the interval to 5 minutes (id is always 1)
curl -s -X PUT -H "X-Api-Key: $KEY" -H "Content-Type: application/json" \
  -d "$(curl -s -H "X-Api-Key: $KEY" "$BASE/api/v3/config/downloadclient" \
        | jq '.checkForFinishedDownloadInterval = 5')" \
  "$BASE/api/v3/config/downloadclient/1"
```

It takes effect immediately — no restart needed.

## Source

Commits: `e14b4a027` (configurable interval), `a72681500` (run logging),
`e65ca9710` (ImportPending self-heal in `Check`), `9928525f5` (its regression tests),
`68fad3665` (same guard in `Import`, the path `ImportPending` items actually take). Key files:
`Configuration/ConfigService.cs`, `Jobs/TaskManager.cs`
(`GetRefreshMonitoredInterval()`), `Download/DownloadProcessingService.cs`,
`Sonarr.Api.V3/Config/DownloadClientConfigResource.cs`,
`Download/CompletedDownloadService.cs` (`Check()` early-return branch),
`Download/TrackedDownloads/TrackedDownload.cs` (`ResetStatus()`).
