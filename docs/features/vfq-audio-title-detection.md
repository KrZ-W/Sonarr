# VFQ Audio-Title Detection ("Audio Title" custom format condition)

> **Status:** stable · **Since:** `v4.0.17.2950+krzw.1` · **Surface:** Settings → Custom Formats

## What it does

Adds a new **"Audio Title"** custom format condition (a regex condition, like the
existing "Release Title" condition) that matches against the **title tag of each audio
stream** in a file.

To make that possible, the fork now captures every audio stream's title tag into
`MediaInfoModel.AudioTitles` during media probing, and carries those titles through both
the import path (`LocalEpisode`) and the file-rescan path (`EpisodeFile`).

The result: you can detect **VFQ (Quebec French)** from the *content* of a file, even
when the release was scene-named only `FRENCH` with no VFQ token.

## Why it exists

VFQ releases are frequently named only "FRENCH" with no `VFQ`/`VOQ` marker, so a
title-regex VFQ custom format never fires and the file is scored as generic French. It
then loses upgrade comparisons to non-VFQ French releases — and, because the incumbent's
score can be higher, can be **blocked from importing entirely**.

Sonarr previously captured **no per-audio-track metadata** and had **no** condition able
to read it, so VFQ couldn't be detected from file content. Distributors very often label
the audio track itself (e.g. `VFQ`, `Français (Québec)`, `French [Canada]`), which
survives even a generic release name. This is a port of the same Radarr fix.

## Configuration

> **Important — use a separate custom format.** Do **not** add the Audio Title
> condition to your existing title-based VFQ format. Conditions are grouped by type
> and the groups are **ANDed**, and audio titles only exist for *files* (import /
> rescan) — remote releases being evaluated for grab never have them. A format that
> combines Release Title and Audio Title conditions therefore matches **nothing at
> grab time**, silently disabling your VFQ preference.

1. **Settings → Custom Formats → Add** — create a new format, e.g. **`VFQ (Audio)`**,
   alongside (not inside) your title-based VFQ format.
2. Add an **Audio Title** condition (Required) with a regex matching the audio-track
   labels you see in your VFQ files, e.g.:

   ```
   \bVFQ\b|\bVOQ\b|Qu[eé]b|Canad
   ```

   (`Canad` also catches `French [Canada]`.)
3. Add a **negated, Required "Release Title"** condition containing your title-VFQ
   regex ("title not already VFQ"). This makes the two formats mutually exclusive, so
   a file scores VFQ exactly once — via the title format *or* the audio format, never
   both. If you have other exclusions on the title format (e.g. "Not VF2"), mirror
   them here too.
4. Save.
5. In your quality profile, give `VFQ (Audio)` the **same score and the same Priority
   flag** as the title VFQ format (see
   [Custom Format Priority Mode](custom-format-priority-mode.md)). Equal scores matter:
   a candidate release can only ever match the *title* format at grab, so if the audio
   format scored higher, a file detected via audio could never be quality-upgraded
   (every candidate would look like a priority downgrade).

> **Tip — find the actual labels:** open an episode file's **Media Info** in Sonarr, or
> run `ffprobe yourfile.mkv` and look at each audio stream's `title`. Build your regex
> from what's really there.

## Behavior & edge cases

- **Existing library re-probes automatically.** The MediaInfo schema revision was
  bumped **11 → 12** (both *current* and *minimum*), so existing files are re-probed and
  gain `AudioTitles` on the **next library scan / refresh**. Until that scan runs, older
  files have no audio titles to match.
- **Empty titles match nothing.** Files whose audio streams have no title tag simply
  don't satisfy the condition.
- **Grab time is title-only.** Audio Title conditions can never match a remote
  release (there is no file to probe yet), so this feature cannot *cause* a grab —
  it corrects the score **after import**, protecting a content-detected VFQ file from
  being replaced. That is also why the condition must live in its own format (see
  Configuration above), never combined with Release Title conditions.
- **Quality upgrades still flow.** With the mutual-exclusion setup above, a file
  detected via audio scores the same priority as a title-tagged VFQ release, so a
  better-quality title-VFQ candidate compares as an equal-priority quality upgrade
  rather than a priority downgrade.

## Related

- [Custom Format Priority Mode](custom-format-priority-mode.md) — flag the VFQ format
  **Priority** so content-detected VFQ wins.
- [Import-time Enforcement](import-time-enforcement.md) — keeps a wrong-language file
  from importing over a correct one once VFQ is scored properly.
- User Guide: [Detect VFQ from audio tracks](../user-guide.md#recipe-detect-vfq-from-audio-tracks).

## Source

Commit: `c54424f66`. Key files:
`CustomFormats/Specifications/AudioTitleSpecification.cs` (`ImplementationName = "Audio Title"`),
`CustomFormats/CustomFormatInput.cs`, `CustomFormats/CustomFormatCalculationService.cs`,
`MediaFiles/MediaInfo/MediaInfoModel.cs`, `MediaFiles/MediaInfo/VideoFileInfoReader.cs`.
