# Import-time Enforcement (MinFormatScore)

> **Status:** stable · **Since:** `v4.0.17.2950+krzw.1` · **Surface:** automatic (no new settings)

## What it does

Mirrors Sonarr's **grab-time** MinFormatScore check onto the **import** side, so that
what gets *kept* in your library actually satisfies the profile's minimum custom-format
score — not just what was *requested* at grab time.

If a downloaded file's custom-format score (computed from the actual file) is below the
profile's `MinFormatScore`, it is **rejected at import** with reason
`CustomFormatMinimumScore`, instead of being silently moved into the library.

## Why it exists — and why language depends on it

Upstream Sonarr enforces `profile.MinFormatScore` **only at grab time**
(`DecisionEngine/Specifications/CustomFormatAllowedByProfileSpecification`), based on the
**release-name parse**. But the parse and the **actual file content** can disagree:
content-derived custom formats can score differently once real MediaInfo is available,
pushing the score below the minimum — yet the import proceeds anyway.

**Sonarr has no quality-profile `Language` field** (unlike Radarr). That means custom
formats are the **only** lever for enforcing a language preference. So this import-time
MinFormatScore check is what actually keeps a wrong-language file out of the library: set
up your language custom formats with a negative score for unwanted languages (or a
positive score gated behind `MinFormatScore`), and a file whose real audio doesn't match
will fall below the minimum and be rejected at import.

## How it works

| Check | Mirrors grab-side spec | At import it re-checks… | New rejection reason |
|---|---|---|---|
| **MinFormatScore** | `CustomFormatAllowedByProfileSpecification` | the custom-format score of the actual file vs `profile.MinFormatScore` | `CustomFormatMinimumScore` |

This catches *any* custom-format setup that drops the score below the minimum, regardless
of which CFs are responsible.

## Configuration

None of its own. It uses your **existing** quality-profile `MinFormatScore` setting — it
simply now applies at import as well as at grab. For language enforcement to bite, make
sure your custom formats + `MinFormatScore` actually express the language you want
(see the recipe below).

## Behavior & edge cases

- A rejected import shows the reason `CustomFormatMinimumScore` in the manual-import /
  activity view, so you can tell *why* a file was refused.
- Pairs naturally with [VFQ Audio-Title Detection](vfq-audio-title-detection.md): once
  VFQ is scored from audio content, the MinFormatScore check keeps a non-VFQ file from
  importing over your VFQ copy.

## Related

- [Custom Format Priority Mode](custom-format-priority-mode.md)
- [VFQ Audio-Title Detection](vfq-audio-title-detection.md)
- User Guide: [Stop wrong-language files from importing](../user-guide.md#recipe-stop-wrong-language-files-from-importing).

## Source

Commit: `cd099a33d`. Key files:
`MediaFiles/EpisodeImport/Specifications/MinimumCustomFormatScoreSpecification.cs`,
`MediaFiles/EpisodeImport/ImportRejectionReason.cs`.
