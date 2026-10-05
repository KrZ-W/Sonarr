# Symlink Import Guard

> **Status:** stable · **Since:** `v4.0.19.2979+krzw.25` · **Surface:** Settings → Media Management → Importing (advanced), `/api/v3/config/mediamanagement`

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

- **Where it applies.** The spec runs in the import decision engine, so it covers completed
  download handling, the *Downloaded Episodes Scan* command, the Manual Import listing and
  the Manual Import re-evaluation that runs when you change a row's series, episodes,
  quality, language or release group. It also applies to library rescans: an *untracked*
  symlink found in a series folder is not added as an episode file while the setting is on
  (files already tracked are not re-evaluated). The *Manage Episodes* listing on a series page does not run import
  specs for untracked files, so a symlink there shows no rejection until one of the row's
  fields is changed and it is re-evaluated.
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
  imported manually. Library rescans also re-evaluate untracked files, so an untracked symlink
  in a series folder logs the Warn on every rescan or refresh until it is removed or the setting is
  turned off.
- **Manual Import does not block it.** The rejection is shown in the Manual Import listing,
  but, as with every other spec rejection, it does not stop a manual import: the dialog
  pre-selects every fully matched row whether or not it has rejections, and the final import
  step does not re-run the specs. **Untick symlink rows before pressing Import**, or the
  incident replays. The same applies to any API client posting a `ManualImport` command.

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
Tests: `NotSymlinkSpecificationFixture`, `NotSymlinkImportDecisionFixture`, and the
`GetSymbolicLinkTarget_*` cases in `NzbDrone.Mono.Test` `DiskProviderFixture` (real links on
disk: absolute, relative, chained, broken, looping, hardlink, file inside a linked folder).

All hunks in upstream files are marked `krzw(symlink-import-guard)`.
