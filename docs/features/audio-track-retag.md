# Audio Track Retag

> **Status:** stable · **Since:** `v4.0.19.2979+krzw.19` · **Surface:** Settings → Media Management → *Audio Language Verification* (advanced) → *Retag Audio Tracks* / *Hardlinked Files*, `EpisodeFile.AudioTrackRetag` (`GET /api/v3/episodefile`), command `RetagAudioTracks`

## What it does

After a download is imported, Sonarr rewrites the **language tag** of the MKV audio tracks
that [Audio Language Verification](audio-language-verification.md) found mistagged, so the
file on disk says what the audio actually is. A French track tagged `eng` (or `und`) that
Whisper identified as French with enough confidence becomes `fre` in the Matroska header.
Nothing else in the file changes. In a season pack every episode is decided on its own record,
so only the mistagged episodes are edited.

The edit is a **header-only** `mkvpropedit` call — one `--edit track:aN --set language=<code>`
per mismatched track. **No audio or video stream is ever rewritten**, re-encoded or remuxed;
the file keeps its size, checksums of the stream data, chapters, attachments and every other
tag. It takes a fraction of a second on local disk.

The outcome is stored on the episode file as `AudioTrackRetag`, the file's `MediaInfo` and
`Languages` are refreshed from a re-probe of the edited file, and a file whose result is `done`
is never touched again.

## Why it exists

Verification fixes what *Sonarr* thinks the file is. It does not fix what **Plex, Jellyfin,
Emby, Kodi or Bazarr** think: they read the track tags, not Sonarr's opinion. A rescued
Quebec-French episode whose only track is still tagged `eng` shows up as "English" in Plex,
gets English subtitles auto-selected, and makes Bazarr look for the wrong subtitles. Fixing the
tag once, right after import, makes every consumer agree with the verified language.

This is the retag half of the external `frtag_rescue.py` script this fork replaced. The script
remuxed a staging copy with ffmpeg and re-imported it; this feature edits the header in place
on the library file, which is cheaper and does not go through the import pipeline again.

## Dependency

- [Audio Language Verification](audio-language-verification.md) must have run for the file:
  the retag **reads** `EpisodeFile.AudioLanguageVerification` (per track: `streamIndex`,
  `taggedLanguage`, `detectedLanguage`, `confidence`) and never re-probes the audio with Whisper.
- `mkvpropedit` from [MKVToolNix](https://mkvtoolnix.download/). The fork's image installs
  the `mkvtoolnix` package, **keeps only `mkvpropedit`** (`mkvmerge`, `mkvextract`,
  `mkvinfo` are removed) and symlinks it to `/app/mkvpropedit`, where it is resolved the same
  way `ffmpeg` is (bundled binary next to Sonarr first, then `PATH`). On other installs put
  `mkvpropedit` on `PATH`.

## Configuration

Settings → Media Management → show advanced → **Audio Language Verification** section:

| Setting | Default | Meaning |
|---|---|---|
| **Retag Audio Tracks** | off | Master switch for the post-import retag. Off means no process is ever started by an import. |
| **Hardlinked Files** | *Skip* | What to do when the library file shares its bytes with another path (see below): *Skip*, *Copy then retag*, *Retag in place*. |

Both are on `GET/PUT /api/v3/config/mediamanagement` as `audioTrackRetagEnabled` and
`audioTrackRetagHardlinkMode` (`skip` / `copyThenRetag` / `retagInPlace`). The confidence
threshold is the verification feature's *Confidence Threshold*; there is no second one.

## Behavior

### When it runs

`AudioTrackRetagService` handles `EpisodeImportedEvent` and retags when **all** of these hold:

1. *Retag Audio Tracks* is enabled;
2. the event is the import of a **new download** (`NewDownload`). Files discovered by
   *Rescan Series*, a refresh, or the existing-file path of a disk scan raise the same event
   with `NewDownload = false` and are ignored; `UpdateMediaInfo` raises nothing;
3. the library file is a **Matroska** file (`.mkv`). Other containers are never touched. A
   verified non-MKV file gets `result = skipped-container` so the record says why;
4. the file has a verification record with at least one track whose detected language, at or
   above the verification threshold, maps to a different language than its tag. Tracks below
   the threshold, tracks with no detection, and tracks whose tag already means the detected
   language are left as they are;
5. the file has not already been retagged (`AudioTrackRetag.result == done`).

The edit runs synchronously inside the import (like the Whisper probe), so by the time the
import is reported complete the file and its record agree.

### The edit

For each mismatched track the detected ISO 639-1 code is mapped through Sonarr's language
model to its ISO 639-2 code in the **bibliographic** form Matroska uses (`fr` → French →
`fre`, `de` → `ger`, `nl` → `dut`, `en` → `eng`). The terminology→bibliographic table is
explicit in the planner rather than derived from the file-naming map, which has several
bibliographic keys per language and no defined order. ISO 639-2 defines exactly 20 languages
with a separate bibliographic code, so the table is the complete standard, not a sample; a test
cross-checks it against the file-naming map. A track whose detected language Sonarr does not
know, or whose language has no ISO 639-2 code, is skipped and listed in
`AudioTrackRetag.skippedTracks` with the reason. Track numbers are translated from the
record's audio-relative 0-based index (`ffmpeg 0:a:N`) to `mkvpropedit`'s 1-based
`track:a{N+1}`.

`mkvpropedit` runs through Sonarr's process provider with a plain argument list (never a
shell string), a 10-minute timeout after which it is killed, and its stderr captured for the
record. Exit code 1 (warnings) is accepted, 2 or more is a failure.

### Verification and record update

After the edit the file is re-probed with **ffprobe** (MediaInfo, not Whisper). The new tags
are compared with the requested ones (`fra`/`fre` are the same language); a mismatch records
`failed`. The comparison reads one entry per audio stream (untagged streams included), the same
per-stream view the verification feature uses, so the record's audio-relative stream index
always lands on the right track. Then `EpisodeFile.MediaInfo` is replaced with the fresh probe and
`EpisodeFile.Languages` is reconciled with the verification result rather than rebuilt from raw
tags: it starts from the languages the import stored, every rewritten track now counts as its
detected language, a rewritten track's old tag language is dropped only when no track still
carries it, `Unknown` is never added (an `und` track stays out of the list), and a track that
could not be written (no ISO 639-2 code) keeps the language the import decided — the record
says why it was not written. The record only covers the tracks the trigger probed (the *Unknown*
trigger probes just the `und` tracks, *positive verification* just the expected-language ones),
so streams absent from it count as their post-edit tag language: an unprobed English track
keeps English in the list. In the common case `Languages` after the retag equals `Languages`
after the import. The verification record itself is kept.

### Hardlinked files

A library file imported as a **hardlink** of a seeding torrent (or moved out of a folder the
client still links to) shares its bytes with the download. Editing those bytes changes the
seed too. The service checks the file's link count through the disk provider (`st_nlink` on
Linux/macOS) and falls back to the transfer mode the import actually used (`HardLink`) when
the platform cannot report a count. When neither says anything (link count unavailable and no
hardlink transfer recorded, which is also the case for the manual command) the file is treated
as *possibly* hardlinked: **Skip** records `skipped-hardlinked` with the reason
`link count unavailable`, **Copy then retag** copies, **Retag in place** edits in place. Then:

| Mode | What happens | Consequence for a seeding torrent |
|---|---|---|
| **Skip** (default) | Nothing is edited. One `Info` log line, record `skipped-hardlinked`. The record keeps the tracks it *would* have changed, so a later manual `RetagAudioTracks` (see below) can apply them once seeding is over. | None. The seed and the library file stay identical. |
| **Copy then retag** | The file is copied next to itself (`<name>.mkv.krzw-retag.tmp`, same directory, permissions copied), the **copy** is retagged, and the copy is atomically renamed over the library path (where `rename(2)` is unavailable it is swapped in through a `<name>.mkv.krzw-retag.bak` of the original, which is restored if the swap fails). Free space in the season folder is checked first (the file's size); on any failure — not enough space, copy error, `mkvpropedit` error — the temp file is deleted and the original is left untouched (`failed`). | None. The seed keeps its original bytes; the library file becomes an independent copy (link count 1), so you lose the disk-space saving of the hardlink for that file. |
| **Retag in place** | `mkvpropedit` edits the shared bytes directly. | **The seed is modified.** Its Matroska header no longer matches the torrent's piece hashes: the client's next recheck fails on those pieces, the torrent stops seeding (or is flagged as errored/missing pieces) and re-downloads them on a forced recheck. Use only if you do not seed from the library, or accept that outcome. |

A file that is not hardlinked (a plain copy or move) is retagged directly in all three modes;
the mode only decides the hardlinked case.

### Record

Migration 219 adds `EpisodeFiles.AudioTrackRetag` (JSON), exposed read-only on
`GET /api/v3/episodefile` as `audioTrackRetag`:

```json
{
  "mode": "copyThenRetag",
  "result": "done",
  "tracks": [ { "streamIndex": 0, "from": "eng", "to": "fre" } ],
  "skippedTracks": [],
  "at": "2026-09-10T14:03:11Z",
  "error": null
}
```

`result` is one of `done`, `skipped-hardlinked`, `skipped-container`, `failed`. Only `done`
is final: a `skipped-*` or `failed` file is retried by the manual command (and nothing else —
imports happen once). `tracks` always lists the planned edits, written only when `result` is `done` (so a
`skipped-hardlinked` record says what a later manual retag would change). `skippedTracks` is a
fork addition to the record shape: mismatched tracks that were left alone, with the reason (a
detected language Sonarr does not know, or one without an ISO 639-2 code). `error` carries the
`mkvpropedit` output, the free-space refusal, `link count unavailable`, or "file not found".

### Manual retag

```
POST /api/v3/command
{ "name": "RetagAudioTracks", "episodeFileId": 123 }
```

Applies exactly the same rules to one file: container check, threshold, hardlink mode,
idempotence. It is meant for files imported before the feature existed, files skipped because
the mode was *Skip* while they were seeding, and files whose last attempt failed. It runs even
when *Retag Audio Tracks* is off (it is an explicit request), but it still respects
*Hardlinked Files*. There is deliberately **no library-wide task**: retagging is a per-file
decision tied to a verification record, and a bulk rewrite of seeding files is exactly what
the hardlink modes exist to prevent.

## Guarantees and limits

- **Never rewrites streams.** Header-only edits; the audio/video data is byte-for-byte what was
  imported. If the header has no room for the new element `mkvpropedit` relocates it within the
  header area; it never touches clusters.
- **MKV only.** MP4/AVI/TS files are never modified; a verified one is recorded
  `skipped-container` so the UI/API shows why the tag was left alone.
- **Never on rescan.** Only the import of a download triggers it; *Rescan Series*, *Refresh*
  and media-info refreshes neither retag nor overwrite the record.
- **Once per file.** `done` is final; there is no drift correction if something retags the file
  back later.
- **Does not change what was imported.** The import decision, the verification, custom-format
  scoring and the [Audio Title](vfq-audio-title-detection.md) condition are untouched; the
  retag runs after the file is in the library and only realigns tags with what verification
  already decided.
- **Blocking.** The edit runs inside the import; for *Copy then retag* on a large hardlinked file
  the copy takes as long as a copy import would.

## Related

- [Audio Language Verification](audio-language-verification.md) — produces the record this
  feature consumes.
- [Import-time Enforcement](import-time-enforcement.md) — why a mistagged file was a problem in
  the first place.
- [Docker / GHCR Deployment](docker-deployment.md) — where `mkvpropedit` comes from in the image.
- User Guide: [Fix the language tag of rescued files](../user-guide.md#recipe-fix-the-language-tag-of-rescued-files).

## Upstream

State as of 2026-09-10 (this fork does not submit changes upstream):

- [Sonarr#8453](https://github.com/Sonarr/Sonarr/issues/8453) — *Support for External
  Audio Language Provider / AI-Assisted Tagging* (closed **not planned**, 2026-03). The
  request covers detecting the language *and* using it; the maintainers' answer was a
  post-import custom script calling ffmpeg/whisper. The detection half is
  [Audio Language Verification](audio-language-verification.md), this feature is the part
  that writes the result back into the file so other applications see it. **Implemented
  natively here**; upstream has never modified media files and declined the request.
- [Radarr#11385](https://github.com/Radarr/Radarr/issues/11385) — the same request on
  Radarr (open, Needs Triage). Same feature in the Radarr fork.
- [Sonarr#3366](https://github.com/Sonarr/Sonarr/issues/3366) — *Reanalyze media files for
  proper media management rename* (closed, 2019): files whose language tags were missing were
  named wrong, and the reporter fixed the tags by hand; the workaround given was rename +
  rescan. Adjacent: this fork fixes the tags automatically and refreshes the record, so the
  renamer sees the right languages without a rescan.
- [Radarr#7584](https://github.com/Radarr/Radarr/issues/7584) — *When using "Refresh & Scan",
  the contents of the existing media file isn't scanned (audio tracks and their languages)*
  (closed, support; Radarr, same code in Sonarr). Adjacent, not fixed: upstream only re-reads track languages when the
  MediaInfo schema changes, so a file retagged by an external tool keeps stale languages in
  Sonarr (same code). This feature avoids the problem for its own edits by re-probing the file and
  updating `MediaInfo`/`Languages` in the same step; externally retagged files still need a
  rename or a schema bump to be re-read.
- [Radarr#11189](https://github.com/Radarr/Radarr/issues/11189) — *Movies showing as
  Multi-Language but is actually not* after an external tool (Tdarr) removed audio tracks
  (closed, logs needed). Same stale-MediaInfo shape as #7584; adjacent only.

## Source

Branch `feature/audio-track-retag-main`, stacked on
`feature/audio-language-verification-main` (it needs the verification record and the
`ReadAllBytes` disk-provider member; the bare `v4.0.19.2979` tag cannot compile it), merged
into `personal/all-features-main`. Key files: `MediaFiles/AudioTags/*`
(`AudioTrackRetagService` — event handler + command executor, `AudioTrackRetagPlanner` — pure
trigger/mapping logic, `MkvPropEditTrackTagger`, `AudioTrackRetag` record,
`RetagAudioTracksCommand`), `Datastore/Migration/219_add_audio_track_retag_to_episode_files.cs`,
`IDiskProvider.GetHardLinkCount` (+ Mono `stat` implementation), `LocalEpisode.TransferMode`
(set by `EpisodeFileMovingService`), `Configuration/ConfigService.cs`,
`Sonarr.Api.V3/Config/MediaManagementConfig*`, `EpisodeFileResource`,
`frontend/src/Settings/MediaManagement/MediaManagement.js`, `Dockerfile` (on the aggregate:
the image lives on its own branch). `git grep -n 'krzw(audio-track-retag)'` lists every touch
of an upstream file.
