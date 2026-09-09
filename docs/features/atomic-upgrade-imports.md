# Atomic Upgrade Imports

> **Status:** stable · **Since:** `v4.0.19.2979+krzw.9` · **Surface:** automatic (no settings)

## What it does

Makes an **upgrade import transactional**: the existing library file is never removed
until the replacement has been **fully imported** — copied/moved into the library *and*
committed to the database. If the import fails, is rejected, or dies at any point after
the transfer starts, the original file (and its database row) survive untouched, and a
replacement that had already been *moved* into the library is returned to the download
folder so the download stays importable.

In history terms: `episodeFileDeleted` (reason *Upgrade*) now only ever appears for an
upgrade that actually succeeded — in the same breath as its `downloadFolderImported`
event — never for one that failed.

## Why it exists

Upstream Sonarr deletes first and imports second. `UpgradeMediaFileService` sends the
existing file to the recycle bin (or **permanently deletes it** when no recycle bin is
configured) and removes its DB row *before* the replacement is transferred; the
replacement's DB row is only written after the transfer. Every failure in between —
destination out of space, permission error, dying mount, crash — leaves a **phantom
empty slot**: old file destroyed, new file absent, episode "missing".

This is not theoretical. On 2026-08-30 a custom-format score change on a live instance
made thousands of existing files upgradable at once; within two hours the history showed
**242** `episodeFileDeleted`/*Upgrade* events against only **27** successful imports —
some 216 episodes lost their files and had to be re-grabbed.

Upstream orders it this way because the destination path can collide with the existing
file on same-name upgrades (the transfer refuses to overwrite). The fork keeps the slot
free **without** destroying anything by parking the file instead.

## How it works

An upgrade import now runs in four steps:

| Step | Action | On failure |
|---|---|---|
| 1. Park | Existing file is renamed in place to `<name>.krzw-upgrade-bak` (atomic, same folder) — nothing is deleted | — |
| 2. Transfer | Replacement is moved / copied / hardlinked into the library slot | Parked original renamed back; import recorded as failed |
| 3. Commit | Replacement's database row is written | Replacement returned to the download folder (moved) or removed (copied); original restored |
| 4. Finalize | Only now: parked original goes to the recycle bin, its DB row is deleted, `episodeFileDeleted`/*Upgrade* fires | Logged and cleaned up best-effort — the committed import is never rolled back by a recycle-bin hiccup |

Details worth knowing:

- **Rollback preserves the download.** If the replacement was *moved* in (source no
  longer exists), rollback moves it back to the download location so Completed Download
  Handling can retry later; if it was *copied or hardlinked*, the stray library copy is
  simply removed. Either way nothing is lost.
- **The parked name is invisible to scans.** `.krzw-upgrade-bak` is not a video
  extension, so a library rescan never imports a parked file.
- **Import scripts still see the outgoing files.** `Sonarr_DeletedRelativePaths`,
  `Sonarr_DeletedPaths` and `Sonarr_DeletedDateAdded` are populated at park time, so a
  script-import hook run during the transfer gets them exactly as before. At that moment
  the files are still on disk under their parked names; they are recycled only at finalize.
- **Event ordering is safe.** The new file is linked to its episodes at commit time;
  the deferred delete event then finds no episodes still pointing at the old file and
  detaches nothing. This also closes two small upstream races (episodes briefly
  unlinked mid-import; empty-folder cleanup firing between delete and move).
- **Stale DB rows can't break manual import anymore.** The Manual Import listing used
  to throw a fatal `FileNotFoundException` (HTTP 500) if a database-referenced file was
  missing on disk; it now logs a warning and lists the item with its last-known size.

## Crash recovery

The park → finalize window is the only exposure: if the **process dies** mid-import
(power loss, kill), a `*.krzw-upgrade-bak` file can be left behind. Nothing is lost —
the original's bytes are intact under the parked name:

- The next library rescan reconciles the DB row (file "missing from disk") and the
  episode is re-grabbed as usual — self-healing, at the cost of a re-download.
- If a later upgrade of the same slot finds a stale `*.krzw-upgrade-bak` in its way, it
  sends it to the recycle bin (permanent delete only if recycling fails) before parking
  the current file — nothing is silently destroyed.
- To recover the file instead, just strip the suffix before rescanning:

```sh
find /path/to/library -name '*.krzw-upgrade-bak' \
  -exec sh -c 'mv "$1" "${1%.krzw-upgrade-bak}"' _ {} \;
```

Compare with upstream, where the same crash loses the file outright (already recycled or
permanently deleted before the transfer even started).

## Relationship to other fork features

[Custom Format Priority Mode](custom-format-priority-mode.md) and
[Import-time Enforcement](import-time-enforcement.md) make upgrade imports far more
frequent (language-first swaps of files that are otherwise fine) — which is exactly what
turned upstream's latent ordering flaw into a mass-loss event. Those features decide
*whether* to upgrade; this one guarantees the swap itself can't destroy anything.

## Upstream

No upstream Sonarr report matches (searched 2026-09-09). The sister Radarr issue is
[Radarr#8444](https://github.com/Radarr/Radarr/issues/8444) — *Import failures can create
excessive deleted events* (open, Confirmed); Sonarr has the same delete-before-transfer
ordering, so the same class of loss applied and is what this feature removes.

## Source

Commit: `9326bd91d`. Key files:
`MediaFiles/UpgradeMediaFileService.cs` (park / finalize / rollback),
`MediaFiles/PendingUpgradeFile.cs`, `MediaFiles/EpisodeFileMoveResult.cs`,
`MediaFiles/EpisodeImport/ImportApprovedEpisodes.cs` (commit orchestration),
`MediaFiles/EpisodeImport/Manual/ManualImportService.cs` (missing-file hardening).
