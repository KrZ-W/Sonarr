# Audio Language Verification

> **Status:** stable · **Since:** `v4.0.19.2979+krzw.17` · **Surface:** Settings → Media Management → *Audio Language Verification* (advanced), `EpisodeFile.AudioLanguageVerification` (`GET /api/v3/episodefile`)

## What it does

When a download's audio-track language **tags** are not trustworthy, Sonarr listens to a
short clip of each suspicious track with a self-hosted **Whisper** speech-recognition
server and uses the detected language instead of the tag. An episode whose release name
says `FRENCH` but whose only track is tagged `eng` — and really is French — imports with
`Languages = French`, keeps its VFQ/French custom formats, and is not offered a "French"
upgrade it does not need. A file that has no French audio at all is still rejected by the
import-time MinFormatScore check, and the rejection reason now says the audio was
**verified**, not merely tagged.

The per-track outcome (stream index, tag, detected language, confidence, source, time) is
stored on the episode file as `AudioLanguageVerification` so a later feature can rewrite
the tags. **This feature never modifies files.** Season packs are the main case here: all
episodes of a pack that share a track layout reuse one probe.

This replaces the detection half of the external `frtag_rescue.py` script (queue scan →
ffprobe → Whisper per track layout). The retag/re-import half stays a follow-up
(*Audio Track Retag*, not implemented).

## Why it exists

French/Quebec series packs (GRIMM, X-Files, Walking Dead dubs…) are routinely muxed with
the French track tagged `eng` or `und` (the mux tool's default), and some groups label the
*subtitle* language as the audio language in the release name. The "Language: Not French"
custom format then fires, [Import-time Enforcement](import-time-enforcement.md) rejects the
whole pack as below MinFormatScore — correctly, on the evidence it has — and the release
lands in the blocklist. Rescuing those downloads meant running a script that probed the
audio with Whisper per track layout, retagged a staging copy and re-imported. Doing the
detection inside the import pipeline removes the script and, more importantly, makes the
decision *before* the pack is rejected.

## Dependency: whisper-asr-webservice

The detector is [`onerahmet/openai-whisper-asr-webservice`](https://github.com/ahmetoner/whisper-asr-webservice),
the same container Bazarr's Whisper provider uses. Sonarr calls only
`POST {endpoint}/detect-language` with a WAV clip and reads
`{"language_code": "fr", "confidence": 0.97}`. Any model size works; `small` or `base` is
plenty for language identification. The server must be reachable from the Sonarr
container; it is **not** bundled.

```yaml
whisper:
  image: onerahmet/openai-whisper-asr-webservice:latest
  environment:
    - ASR_MODEL=base
    - ASR_ENGINE=faster_whisper
  ports:
    - "9000:9000"
```

`ffmpeg` is required next to `ffprobe` for the clip extraction. The fork's image installs
it and symlinks both into `/app`; on other installs `ffmpeg` on `PATH` is enough.

## Configuration

Settings → Media Management → **Audio Language Verification** (show advanced):

| Setting | Default | Meaning |
|---|---|---|
| **Enable** | off | Master switch. Off, or an empty endpoint, means no probe is ever made. |
| **Whisper Endpoint** | empty | Base URL of the whisper-asr-webservice, e.g. `http://whisper:9000`. Validated as a URL when enabled. |
| **Confidence Threshold** | `0.85` | A detection at or above this overrides the tag; below it the tags are kept (but the outcome is still recorded). |
| **Clip Offset** | `300` s | Where the sample starts. |
| **Clip Length** | `30` s | Sample length. Files shorter than offset + length are sampled from the middle. |
| **Verify Tagged Tracks** | Never | Also probe tracks already tagged with an expected language: *Never*, *For release groups* (list below), *Always*. |
| **Verify Tagged Tracks For Groups** | empty | Comma-separated release groups for the *For release groups* mode (case-insensitive). |
| **Timeout** | `120` s | Per-track limit for clip extraction and for the detector reply. |

All fields are on `GET/PUT /api/v3/config/mediamanagement`
(`audioLanguageVerification*`). "Expected languages" are never hard-coded: Sonarr has no
quality-profile Language field, so they are the languages asked for by positively scored
**Language** conditions in the custom formats the profile uses (*Original* resolved
through the series). French is the dataset this was built for, not a constant.

## Behavior

### When a probe runs (trigger policy)

`MediaFiles/AudioLanguage/AudioLanguageTriggerPolicy` is a pure class evaluated once per
file inside the language aggregation, after MediaInfo has been read. Inputs:

- **claim** — union of the languages parsed from the file name, the folder, the download
  client item title and the *grab* history row of this download (that is where indexer
  language flags travel);
- **evidence** — the language tag of every audio stream (from the ffprobe JSON MediaInfo
  already holds; nothing is re-probed);
- **expectation** — whether any custom format used by the profile has a Language
  condition, and which languages the positively scored ones want.

Nothing is probed when the feature is off, the endpoint is empty, the file is already in
the library (rescan / existing file), MediaInfo is missing, **or no custom format in the
profile has a Language condition** (fast path — the profile cannot care about language).
Otherwise the first rule that applies decides which tracks are sent:

| # | Trigger | Probed tracks |
|---|---|---|
| a | **Contradiction** — the claim contains a language no track is tagged with | every track (none carries the claimed language) |
| b | **Unknown** — a track is tagged `und` or has no language tag | those tracks |
| c | **Impending rejection** — with the tags as they are, the file's custom-format score would fall below the profile's *MinFormatScore* | every track not tagged with an expected language |
| d | **Positive verification** — a track is tagged with an expected language | those tracks; only per the *Verify Tagged Tracks* setting |

### The probe

`WhisperAsrAudioLanguageProbe` extracts one audio stream (`ffmpeg -ss <offset> -t <length>
-map 0:a:N -ac 1 -ar 16000 pcm_s16le`, argument list, no shell) to a temp WAV, POSTs it as
`audio_file` to `/detect-language`, deletes the temp file and maps the ISO 639-1 code back to
a Sonarr language. **It never throws into the import**: ffmpeg failure, timeout, connection
refused, HTTP error or an unparsable reply log one warning and return "no result", and the
import proceeds on the existing evidence exactly as before the feature.

### Cache: one probe per track layout per pack

Results are cached in memory per *(pack, track layout, stream)*, where the pack is the
download client item id (or the parent folder for a manual/folder import) and the layout is
the ordered list of `(codec, channels, language tag, title)` of the audio streams. All
episodes of a season pack that share a layout reuse the first episode's answer, so a
24-episode pack with one layout costs one probe per triggered track, not twenty-four. Failed probes are cached too (a down
server costs one attempt per layout). Entries expire after 12 hours.

### Result

- If at least one probed track is detected **at or above the threshold**, the augmenter
  returns the per-track languages (detected for verified tracks, tag for the others) with
  confidence `AudioProbe`, which ranks **above MediaInfo**. The file's `Languages`, custom
  formats and import-time checks then see the verified languages.
- Below the threshold, or when every probe failed, the augmenter returns nothing and the
  languages come from MediaInfo as today.
- Either way the per-track outcome is stored on the `LocalEpisode` and copied to
  `EpisodeFile.AudioLanguageVerification` when the file is imported (migration 218 adds
  the column). It is written **at import only**: `RescanSeries`, `UpdateMediaInfo` and
  the existing-file path of a disk scan neither re-probe nor rewrite it.

### Rejection reason

When triggers a–c fired, the probe ran, and the file is still rejected by the import-time
MinFormatScore check, the rejection message ends with e.g.:

```
Custom Formats [Language: Not French] have score -10000 below profile minimum 0. Audio verified (detected en 0.97, en 0.94)
```

What is rejected does not change — only the text, so the blocklist and history say the
audio was actually checked.

### Manual import

Manual import runs the same aggregation, so a file listed in *Manual Import* is probed by
the same rules and the record follows the file into the library. The languages you pick in
the manual-import dialog still win over the detected ones (as they win over MediaInfo).

## Cost

Per triggered import: one ffmpeg extraction of *Clip Length* seconds per probed track
(sub-second on local disk) and one Whisper call per *(layout, track)* — typically 1–3 s
with `faster_whisper`/`base` on CPU, longer for large models. Nothing runs for files whose
tags already satisfy the profile unless *Verify Tagged Tracks* asks for it. Nothing runs
at grab time, at rescan, or for libraries whose profiles do not care about language.

## Not covered / follow-up

- **Audio Track Retag** (planned, separate feature): post-import `mkvpropedit` rewrite of
  the track language tags from `AudioLanguageVerification`, with a setting
  *Never / Only when the import was a copy / Always* (default *Only when copied*). The stored
  record already carries everything it needs (audio-relative stream index, detected
  language, confidence).
- Grab-time decisions, custom-format calculation and the [Audio Title](vfq-audio-title-detection.md)
  condition are untouched.

## Related

- [Import-time Enforcement](import-time-enforcement.md) — the check whose rejection this
  feature prevents (or makes truthful).
- [VFQ Audio-Title Detection](vfq-audio-title-detection.md) — the other content-based
  language signal.
- User Guide: [Rescue mislabeled French audio](../user-guide.md#recipe-rescue-mislabeled-french-audio).

## Upstream

No upstream Sonarr issue asks for speech-based language detection at import. Adjacent:
[Sonarr#5598](https://github.com/Sonarr/Sonarr/issues/5598) — *Improve CF Comparison
Between Release and File* (open). The same design ships in the Radarr fork against
`MovieFile.AudioLanguageVerification`, where it also feeds the profile Language check.

## Source

Branch `feature/audio-language-verification-main` (cut from `v4.0.19.2979`), merged into
`personal/all-features-main`. Key files:
`MediaFiles/AudioLanguage/*` (`AudioLanguageTriggerPolicy`, `WhisperAsrAudioLanguageProbe`,
`FfmpegAudioClipExtractor`, `AudioLanguageProbeCache`, `FfprobeAudioTrackLayoutReader`,
`AudioLanguageVerification`, `AudioLanguageVerificationMessage`),
`MediaFiles/EpisodeImport/Aggregation/Aggregators/Augmenters/Language/AugmentLanguageFromAudioProbe.cs`,
`Datastore/Migration/218_add_audio_language_verification_to_episode_files.cs`,
`Configuration/ConfigService.cs`, `Sonarr.Api.V3/Config/MediaManagementConfig*`,
`frontend/src/Settings/MediaManagement/MediaManagement.js`. The rejection-text hunk lives
in the import-enforcement spec (`EpisodeImport/Specifications/MinimumCustomFormatScoreSpecification.cs`)
on the aggregate and on `fix/import-min-format-score-main`.
`git grep -n 'krzw(audio-language-verification)'` lists every touch of an upstream file.
