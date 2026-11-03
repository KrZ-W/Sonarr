# Symlink Import Guard

> **Status:** stable · **Since:** `v4.0.19.2979+krzw.25` (import-path hardening and the already-imported rule: `v4.0.20.3014+krzw.2`) · **Surface:** Settings → Media Management → Importing (advanced), `/api/v3/config/mediamanagement`

## What it does

Refuses to import a file that is a **symbolic link**. Every import candidate whose path is
itself a symlink is rejected by a new import-decision specification,
`NotSymlinkSpecification`, with the reason `SourceIsSymlink` and the message

```
Source is a symbolic link → /resolved/target/of/the/link.mkv
```

The rejection is a normal, permanent spec rejection: completed download handling leaves the
download *Import Blocked* with that message in the queue, and Manual Import lists the file with
the rejection like any other. Each rejection is also logged at **Warn** with the link path and
its resolved target, so the log alone tells you what the link pointed at. In the Manual Import
API, `rejections[].reason` carries the message text, with `type: permanent`.

Since `v4.0.20.3014+krzw.2` the guard also says **what the link points at** when the target is a file
Sonarr already tracks, with its own reason:

| Resolved target | Reason | Message suffix |
|---|---|---|
| not a library file | `SourceIsSymlink` | *(none)* |
| a tracked file of another episode (or series) | `SourceIsSymlinkToLibraryFile` | `, which is already the library file of <Series> SxxEyy` |
| the **current file of the very episode(s) the candidate maps to** | `SourceIsSymlinkToCurrentFile` | `, which is already the current file of this episode` |

The third case is the normal outcome of this stack's seed pipeline (a `/seed` payload replaced
by a link to the library copy it was imported as, or deduplicated against it): there is nothing
left to import, so **completed download handling treats such a download as imported** (state
*Imported*, `DownloadCompletedEvent` with the episodes' current files, download history row, removal
from the client per the client's *Remove Completed* setting) instead of leaving it *Import
Blocked* and re-evaluating it every pass. The rule needs every episode the grab covers to be either imported or linked to its own current file (samples and extras do not count against it); with no episodes parsed from the grab, every file must be such a link.
That case is logged at **Debug** by the spec and at **Info** once when the download is marked
imported; the other two cases keep the **Warn**.

**Hardlinks are not affected.** A hardlink is a regular directory entry; only symbolic links
(including relative, chained and looping ones) are rejected by this check. A *broken* link
never reaches it: see the edge cases below.

## Why it exists

Incident 2026-10-04: completed download handling scanned a download folder shared by two
torrents, found the *other* series' seed symlinks (created by seed-deduplication tooling and
pointing back into the library), took them for real files, imported them as Bluray-1080p
upgrades and deleted the real episodes.

Two upstream behaviours combine into that data loss:

- `DiskProviderBase.GetFileSize` follows symlinks, so a link looks like a full-size video
  and passes every size, quality and sample check.
- `NzbDrone.Mono.Disk.DiskProvider` `CopyFileInternal` / `MoveFileInternal` **recreate the
  link** at the destination instead of copying bytes. The "imported" episode is therefore a
  link to some other file. When it is judged an upgrade, the upgrade deletes (or recycles)
  the existing episode file, which can be the very file the link points at, leaving a
  dangling link in the library.

On this stack an import must always copy real bytes, so a symlink in a download folder is
never a legitimate import source.

## Configuration

**UI.** Settings → Media Management → *Importing* (shown with **Show Advanced**):
**Reject Symbolic Links as Import Sources**, on by default in this fork.

**API.** `rejectSymlinkImportSources` (boolean, default `true`) on
`GET/PUT /api/v3/config/mediamanagement`.

Turn it off only if your downloads legitimately arrive as symlinks, e.g. rclone or debrid
setups whose download client hands Sonarr links into a mount. With it off, upstream
behaviour returns, including the data-loss case above.

## Behavior & edge cases

- **Where it applies.** Three layers, all driven by one shared check (`SymlinkImportGuard`,
  which reads the setting, `lstat`s the candidate and resolves the target against the library):
  1. the import-decision spec `NotSymlinkSpecification`, so completed download handling, the
     *Downloaded Episodes Scan* command, the Manual Import listing and re-evaluation, and library rescans
     all show the rejection;
  2. the import funnel `ImportApprovedEpisodes.Import`, which every path that turns a source file into
     an episode file goes through, including the **final Manual Import step** (the `ManualImport`
     command builds approved decisions without running the specs, so before `v4.0.20.3014+krzw.2` a
     symlinked row that was left ticked, or posted by an API client, was imported as a link);
  3. the file mover (`EpisodeFileMovingService.MoveEpisodeFile` / `CopyEpisodeFile` for imports only),
     evaluated immediately before the transfer, so a payload that becomes a link *between* the
     decision and the copy (seed relinking racing an import) is refused as well, with the result
     "Failed to import" and the link message.

  Renaming or moving a file that already belongs to the library is not an import and is not
  checked. The *Manage Episodes* listing on a series page does not run import specs for untracked
  files, so a symlink there shows no rejection until one of the row's fields is changed and
  it is re-evaluated; it is still refused by layer 2 if imported.
- **Only matched candidates.** Like every import spec, it runs once the file has been matched
  to episodes. A link that cannot be matched is still rejected, by the usual
  "Unknown Series" / "Invalid season or episode" reasons.
- **Only the file itself.** The check is an `lstat` of the candidate path. A regular file
  reached *through* a symlinked folder is not a link and is not rejected, and there is no
  "real path must be inside a root folder" rule: in-folder Manual Import of real files keeps
  working.
- **Link target in the message.** The target is resolved through chains and relative links;
  a looping chain reports its first hop.
- **Broken links.** A link whose target is missing cannot be sized, so the import pipeline
  rejects it before this check as "Unexpected error processing file" (logged at Error, with no
  Warn naming the target). It is still rejected and never imported.
- **Repeated warnings.** Completed download handling re-checks an *Import Blocked* download
  on every pass, so the Warn line repeats until the download is removed from the client or
  imported manually (links to the download's own current file no longer get here: they are
  treated as imported, see above). Library rescans also re-evaluate untracked files, so an untracked symlink
  in a series folder logs the Warn on every rescan or refresh until it is removed or the setting is
  turned off.
- **Manual Import cannot bypass it.** The rejection is shown in the Manual Import listing and
  the dialog still pre-selects every fully matched row, but since `v4.0.20.3014+krzw.2` the final
  import step re-checks each row: a symlinked source is rejected with the same reason and
  never copied as a link, whether ticked in the UI or posted as a `ManualImport` command by an
  API client. The command itself only logs at Trace; the refusal is a **Warn**
  (`Refusing to import <path>: Source is a symbolic link → …`).

## Upstream

No upstream Sonarr setting refuses symlinked import sources. The closest upstream thread,
[Sonarr#7505](https://github.com/Sonarr/Sonarr/issues/7505), asks for the opposite (importing
by symlink). Searched 2026-10-05.

## Source

Commit on `personal/all-features-main` (release `v4.0.19.2979+krzw.25`). Key files:
`MediaFiles/EpisodeImport/Specifications/NotSymlinkSpecification`,
`MediaFiles/EpisodeImport/ImportRejectionReason` (`SourceIsSymlink`),
`NzbDrone.Common/Disk/IDiskProvider` + `DiskProviderBase.GetSymbolicLinkTarget`,
`Configuration/ConfigService` (`RejectSymlinkImportSources`),
`Sonarr.Api.V3/Config/MediaManagementConfigResource`,
`frontend/src/Settings/MediaManagement/MediaManagement.js`.
`v4.0.20.3014+krzw.2` adds `MediaFiles/EpisodeImport/SymlinkImportGuard` (`ISymlinkImportGuard`, `SymlinkImportCheck`),
`MediaFiles/SymlinkImportSourceException`, `ImportDecision.Reject`, the gate in
`ImportApprovedEpisodes.Import`, the transfer-time check in `EpisodeFileMovingService`, the reasons
`SourceIsSymlinkToLibraryFile` / `SourceIsSymlinkToCurrentFile` and the already-imported rule in
`Download/CompletedDownloadService.VerifyImport`.
Tests: `NotSymlinkSpecificationFixture`, `NotSymlinkImportDecisionFixture`, `SymlinkImportGuardFixture`,
the symlink cases in `ImportApprovedEpisodesFixture` (funnel), `MoveEpisodeFileFixture` (mover),
`ManualImportServiceFixture` (final Manual Import step, untracked and queue), `ScanFixture`
(rescan), `CompletedDownloadServiceTests.ImportFixture` (already-imported rule), and the
`GetSymbolicLinkTarget_*` cases in `NzbDrone.Mono.Test` `DiskProviderFixture` (real links on
disk: absolute, relative, chained, broken, looping, hardlink, file inside a linked folder).

All hunks in upstream files are marked `krzw(symlink-import-guard)`.
