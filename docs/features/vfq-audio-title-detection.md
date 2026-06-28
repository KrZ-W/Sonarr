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

1. **Settings → Custom Formats → (your VFQ format) → Add Condition → Audio Title.**
2. Enter a regex matching the audio-track labels you see in your VFQ files, e.g.:

   ```
   VFQ|VOQ|Qu[ée]b|Canad
   ```

   (`Canad` also catches `French [Canada]`.)
3. Save the custom format.
4. **Recommended:** in your quality profile, give the VFQ format a score **and tick
   Priority** (see [Custom Format Priority Mode](custom-format-priority-mode.md)) so a
   content-detected VFQ release wins over a higher-quality non-VFQ release.

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
- **Works alongside Release Title.** Combine an Audio Title condition with title-based
  conditions in the same custom format if you want either signal to fire.

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
