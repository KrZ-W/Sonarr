# Grabbed Release Title

> **Status:** stable · **Since:** unreleased (next fork release on `v4.0.19.2979`) · **Surface:** Settings → Media Management → *File Management* (advanced) → *Score Files by Grabbed Release Title*, `EpisodeFile.GrabbedReleaseTitle` (`GET /api/v3/episodefile?seriesId=N`), command `BackfillGrabbedReleaseTitles`

## What it does

Sonarr stores the release title a file was **grabbed** under on the episode file
(`GrabbedReleaseTitle`), and — when the advanced setting is on — scores the existing file's
custom formats against that title as well as the ones it already used.

Custom formats for an existing file are evaluated against a single release title, picked by a
ladder: `SceneName`, else the file name of `OriginalFilePath`, else the file name of
`RelativePath`. After a rename, a `Copy`-mode import, or an import where the download client
reported a folder name instead of the release name, that title can be a lot poorer than the
title the release was actually grabbed under — a `TRUEFRENCH ... VFQ` grab can end up scored as
a nameless `Series - S01E01.mkv`. The file then scores lower than it should, the release looks
like an upgrade over itself, and Sonarr re-grabs the same release.

Enabling the setting scores the file under **every release title it could carry** and keeps the
best one.

## Why it exists

A file that under-scores itself is indistinguishable, at grab time, from a file that genuinely
lacks the custom formats. `UpgradeAllowedSpecification` and `UpgradeDiskSpecification` compare
the *file's* score with the *release's* score, so an under-scored file accepts a "better"
release that is in fact the same release, and the loop repeats on every RSS sync.

The grabbed title is the one piece of evidence that says what the release really was, and
history already carries it. This feature just keeps it on the file.

## Configuration

| Setting | Key | Default |
|---|---|---|
| *Score Files by Grabbed Release Title* (advanced) | `scoreFilesByGrabbedReleaseTitle` on `/api/v3/config/mediamanagement` | **off** |

The column is filled at import **whether or not the setting is on**; the setting only controls
whether scoring uses it. That means you can turn it on, look at the scores, and turn it back off
without losing anything.

## Behavior

### The selection rule

With the setting on, an existing file is scored under each of these release titles:

- the **incumbent** — whatever the legacy ladder above would have picked;
- the **grabbed release title**;
- the **scene name**;
- the file name of the **original file path**.

`RelativePath` is deliberately not in the list: `ReleaseTitleSpecification` already ORs the
file's name into every evaluation (`CustomFormatInput.Filename`), so it is always in play.

Everything except the release title is **frozen** across candidates — release group, languages,
quality, size, indexer flags, release type and the audio titles the
[Audio Title](vfq-audio-title-detection.md) condition reads all stay exactly what the file says.
Only the title varies.

A candidate is **eligible** only if it lowers neither score:

```
eligible  = candidates where  priorityScore >= incumbent.priorityScore
                        AND   totalScore    >= incumbent.totalScore
winner    = max over eligible by (totalScore, then priorityScore); ties keep the incumbent
```

The incumbent is always eligible, so the eligible set is never empty and **neither the total
custom format score nor the priority score can ever go down** when the setting is turned on.

The priority score is checked because [Custom Format Priority Mode](custom-format-priority-mode.md)
compares it **before** quality. A plain "highest total score wins" rule could pick a title that
scores higher overall but drops a priority custom format — which would make the file weaker on
the axis the fork reads first and *cause* the very re-grab this feature exists to prevent.

### Where the rule applies

The scoring ladder is a **separate method** (`ParseCustomFormatForScoring`). The existing
`ParseCustomFormat` overloads are untouched, and only these call sites moved onto the new one:

| Call site | What it decides |
|---|---|
| `DecisionEngine/Specifications/UpgradeAllowedSpecification` | grab-time upgrade — the re-grab fix |
| `DecisionEngine/Specifications/UpgradeDiskSpecification` | grab-time cutoff + upgrade |
| `MediaFiles/EpisodeImport/Specifications/UpgradeSpecification` | import-time upgrade |
| `MediaFiles/EpisodeImport/Manual/ManualImportService` | manual import listing |
| `Sonarr.Api.V3/EpisodeFiles/EpisodeFileResource` | `customFormats` / `customFormatScore` on the API |

**Naming is explicitly not one of them.** `Organizer/FileNameBuilder` renders the
`{Custom Formats}` token through the old overload and keeps doing so.

### Import capture

When an import has a tracked download with grab history behind it, the grab's `SourceTitle` is
sanitised and stored on the new episode file. A manual import that carries no download id has no
grab, so the column stays `null`.

### Sanitisation

Some trackers put a whole description blob in the release title: the real title on the first
line, then LF/TAB padded lines of `Taille: 4 GB Seeders: 27 ...`. Only the first non-empty line
is a release title, and custom format regexes are written against single-spaced titles, so the
stored value is the **first non-empty line, trimmed, with internal whitespace runs collapsed to
one space**. An empty result is stored as `null`. This applies at import and at backfill.

### Backfill

Files imported before the feature existed have no stored title. The manual command fills them:

```
POST /api/v3/command  {"name": "BackfillGrabbedReleaseTitles"}
```

It is never scheduled, and it is idempotent — re-running it changes nothing that is already
correct.

Matching is **oracle-first**:

1. A `downloadFolderImported` history row carries the imported file's id in `Data["fileId"]`.
   That is an exact link, not a heuristic. If such a row exists for the file:
   - it has a download id → use it;
   - it has **no** download id → the file's real import was a *manual* import with no grab
     behind it, so the file is **skipped**. It is not matched by time.
2. Only if no `fileId`-bearing row exists at all, fall back to the `downloadFolderImported` row
   for one of the file's episodes, with a non-empty download id, whose date is **closest** to the
   file's `DateAdded` and within **6 hours**.
3. The latest `grabbed` row with that download id (compared case-insensitively) supplies the
   title.

The skip in 1b is the point of the oracle. Measured against it on a real library, pure time
proximity made 39 wrong attributions out of 2,322 files, and 38 of the 39 were manual imports
where the matcher reached for a neighbouring torrent's title.

The run logs `scanned / set / no-import-event / no-download-id / no-grab / unchanged`. History is
read once per event type and indexed in memory rather than queried per file.

## Guarantees and limits

- **Scores never go down.** The eligibility rule is structural, not empirical: the incumbent is
  always a candidate.
- **Naming is unchanged.** `{Custom Formats}`, `{Scene Name}` and `{Original Title}` render
  exactly as before, so turning the setting on never proposes a rename. Webhooks and
  notifications are unaffected.
- **Enabling it can block upgrades that were previously allowed.** This is the intended effect
  and the reverse of the same coin: a *higher* file score is a rejection reason
  (`UpgradableSpecification` → `CustomFormatScore` / `CustomFormatCutoff` /
  `MinCustomFormatScore`, and `MediaFiles/EpisodeImport/Specifications/UpgradeSpecification` →
  `NotCustomFormatUpgrade`).
  A file that was under-scoring itself will stop accepting releases that only looked like
  upgrades — including, if your custom formats say so, releases you would have wanted. Turn the
  setting off to get the old behaviour back; nothing is written or deleted either way.
- **The backfill skips manual imports by design.** A file whose real import carried no download
  id keeps a `null` title rather than an attractive but wrong one.
- **Only one title is chosen, not a union.** The winner's formats are used as-is; formats are
  never merged across titles. Merging would create score combinations no real release could have.
- **The column is not editable.** `grabbedReleaseTitle` is read-only on the API; it is written by
  the import and by the backfill command only.

## Related

- [Custom Format Priority Mode](custom-format-priority-mode.md) — the priority score this rule
  protects.
- [Import-time Enforcement](import-time-enforcement.md) — the other half of the score-at-import
  story.
- [VFQ Audio-Title Detection](vfq-audio-title-detection.md) — the audio titles that stay frozen
  across candidates.

## Source

Branch `feature/grabbed-release-title-main`, merged into `personal/all-features-main`. Key
files: `Datastore/Migration/220_add_grabbed_release_title_to_episode_files.cs`,
`MediaFiles/EpisodeFile.GrabbedReleaseTitle`, `Parser/Model/LocalEpisode`
(`GrabbedReleaseTitle`, `ScoringCustomFormats`),
`CustomFormats/CustomFormatCalculationService.ParseCustomFormatForScoring`,
`MediaFiles/GrabbedReleaseTitles/*` (`GrabbedReleaseTitleSanitizer`,
`BackfillGrabbedReleaseTitlesCommand`, `BackfillGrabbedReleaseTitlesService`),
`MediaFiles/EpisodeImport/Aggregation/Aggregators/AggregateReleaseInfo`,
`MediaFiles/EpisodeImport/ImportApprovedEpisodes`, `Configuration/ConfigService`,
`Sonarr.Api.V3/Config/MediaManagementConfigResource`,
`Sonarr.Api.V3/EpisodeFiles/EpisodeFileResource`, and the Media Management settings screen.
