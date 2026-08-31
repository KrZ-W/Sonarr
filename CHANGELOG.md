# Changelog

All notable **fork-specific** changes to KrZ-W/Sonarr are documented here.
This changelog covers only what this fork adds on top of upstream Sonarr (v4) — it does
**not** reproduce [upstream's own changelog](https://github.com/Sonarr/Sonarr/releases).

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this fork's versioning is described in [FORK.md](FORK.md#versioning):
`v<upstream-version>+krzw.<N>`.

## [Unreleased]

_Nothing yet._

## [v4.0.19.2979+krzw.9] — based on Sonarr 4.0.19.2979

### Fixed

- **Phantom empty slots on failed upgrade imports (data loss):** an upgrade import
  deleted the existing file and its DB row *before* moving the replacement into the
  library, so any failure after that point (destination error, crash, DB write failure)
  destroyed the old file while importing nothing — with no recycle bin configured, the
  loss was permanent. Triggered at scale on 2026-08-30 when a CF score change queued
  ~1000 upgrade grabs: 242 `episodeFileDeleted`/Upgrade events, only 27 imports. The
  ordering is inherited from upstream; the fork's priority-CF upgrades made it fire
  constantly. Upgrades are now **atomic**: the existing file is parked aside (renamed),
  the replacement is imported and committed, and only then is the original recycled —
  any failure restores the original and returns a moved replacement to the download
  folder. See [docs](docs/features/atomic-upgrade-imports.md).
- **Manual Import no longer 500s on stale file rows:** a database-referenced episode
  file missing from disk made the Manual Import listing throw a fatal
  `FileNotFoundException`; it now logs a warning and lists the item with its last-known
  size.

Container image: `ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.9`.

## [v4.0.19.2979+krzw.8] — based on Sonarr 4.0.19.2979

### Fixed

- **Library rescans no longer reject existing files below the minimum CF score.**
  The fork's import-time `MinFormatScore` enforcement also ran on unmapped files
  already inside a series folder during a disk rescan; a file scoring below the
  profile minimum was rejected on every rescan and never mapped into the database
  (present on disk, invisible to Sonarr, episode still treated as missing).
  Existing files now skip the check, matching the convention of the other
  import-gatekeeping specs. Enforcement on the download/import path is unchanged.
- **Regional-language unit test updated.** `IsoLanguagesFixture` still asserted
  `fr-CA` is an invalid code, contradicting the fork's `en-CA`/`fr-CA` entries;
  the case is now a positive French/English mapping test.

Container image: `ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.8`.

## [v4.0.19.2979+krzw.7] — based on Sonarr 4.0.19.2979

### Fixed

- **Curly double quotes no longer fold to apostrophe.** `"Mon Titre"` (U+201C/201D)
  was folded to `'Mon Titre'` instead of `"Mon Titre"`, producing a search term
  indexers would never match.
- **Modifier-letter apostrophe (U+02BC) is now folded.** Titles using the
  typographically correct apostrophe `ʼ` (common in transliterated names) were
  silently rejected because the character's code point exceeds Latin-1.

Container image: `ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.7`.

## [v4.0.19.2979+krzw.6] — based on Sonarr 4.0.19.2979

### Fixed

- **User scene mapping import robustness:** the collision guard no longer calls the
  series lookup that throws `MultipleSeriesFoundException` when two library series
  share a clean title (The Office UK/US) — one such title used to abort the whole
  import with a 500 after earlier series had already been inserted. The library is
  now read once per request and compared directly, which is also fewer queries than
  the previous per-title lookup.
- **Guard now compares what is actually stored.** It cleaned the raw title while the
  row stored the folded spelling, so ligature titles (`Cœur…` vs `Coeur…`) could slip
  past and hijack an existing series' parsing.
- **Parse terms follow upstream's convention** (derived from `Title`), with a second
  row inserted for the folded spelling when it differs, so releases named either way
  resolve. Import summaries count titles, not rows.
- **Typography folding is category-based** (`\p{Zs}`, `\p{Pd}`, `\p{Pi}\p{Pf}`) plus
  explicit ligatures, covering characters the hand-written list missed (narrow
  no-break space, non-breaking hyphen). A title still unsearchable after folding is
  skipped with a warning instead of stored as a row that never contributes a query.

Container image: `ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.6`.

## [v4.0.19.2979+krzw.5] — based on Sonarr 4.0.19.2979

### Added

- **User scene mappings:** new `POST /api/v3/scenemapping/user/import` endpoint
  bulk-upserts missing French/Quebec series titles as `Type=User` scene mappings
  from the curated dataset format
  (`[{tvdbId, imdbId, seriesTitle, year, missingFrenchTitles:[{title, region}]}]`);
  idempotent, library series only, `region` stored in the mapping comment, returns
  an added/skipped/not-found summary. User rows are refresh-proof by design (mapping
  updates clear per provider type only), participate in search scene titles, release
  parsing, and import identification, and show on the series page's Alternate Titles
  list. Search terms are normalized to Latin-1 (`œ→oe`, `’→'`, `—→-`) so the
  scene-name search filter keeps them; titles colliding with another series' parse
  term are refused to protect release parsing. See
  [features/user-scene-mappings.md](docs/features/user-scene-mappings.md).

Container image: `ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.5`.

## [v4.0.19.2979+krzw.4] — based on Sonarr 4.0.19.2979

### Fixed

- **Priority CF upgrades respect Upgrades Allowed:** a profile with upgrades disabled
  no longer auto-replaces files when a release carries a higher priority CF score.
  (Import stays permissive, matching upstream: the flag is a grab-side gate.)

Container image: `ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.4`.

## [v4.0.19.2979+krzw.3] — based on Sonarr 4.0.19.2979

### Fixed

- **Season-pack partial fill:** unmonitored missing episodes no longer count as
  fillable (a pack is never grabbed just to fill episodes you opted out of), and in
  `Any`/`Threshold` mode a missing episode no longer counts for a release that was
  previously grabbed and imported without producing a file for it — ending the
  re-grab-the-same-pack-every-RSS-sync loop. Failed downloads still retry as before.
  See [docs](docs/features/season-pack-partial-fill.md).
- **CI:** a manual `docker-release.yml` dispatch now checks out the requested tag —
  previously it built the default branch HEAD but published it under the release tag,
  silently mislabeling an immutable release image. The image's `revision` label now
  records the actually-built commit.
- **Tests:** removed an unused using directive that broke the test build (`IDE0005`).

### Docs

- The VFQ audio-title guide now prescribes a **separate** `VFQ (Audio)` custom format
  with a negated title condition — combining Audio Title and Release Title conditions
  in one format matches nothing at grab time (conditions AND by type group).
- The CDH interval is documented as **API-only** (`checkForFinishedDownloadInterval`);
  it never had a Settings UI field.
- Refreshed post-rebase `Source` hashes, fixed stale tag/image version examples, and
  added a mandatory image boot-test step to the release procedure.

Container image: `ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.3`.

## [v4.0.19.2979+krzw.2] — based on Sonarr 4.0.19.2979

Docker image fix only — no source or feature changes.

### Fixed

- **Container crash-loop at startup** (`FileLoadException` for
  `System.Text.Encoding.CodePages` / `Microsoft.Extensions.*`). The Dockerfile built with
  the *latest* .NET 6 SDK, which resolved Sonarr 4.0.18+'s out-of-band package references
  to net7/net8 assets that can't load on the net6 runtime. Pinned the build to the exact
  upstream toolchain — **SDK 6.0.405**, framework-dependent (upstream's `SelfContained=false`)
  — so the correct net6.0 assets are bundled. Image now boots (`/ping` → OK).

Use `ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.2` — `4.0.19.2979-krzw.1` is broken.

## [v4.0.19.2979+krzw.1] — based on Sonarr 4.0.19.2979

Maintenance release — rebased the fork onto upstream Sonarr **4.0.19.2979** (from `4.0.17.2950`).
All fork features carry forward unchanged; the rebase was clean (no conflicts). The full feature set is unchanged
from the previous release (below).

### Changed

- Rebased onto upstream Sonarr **4.0.19.2979** (from 4.0.17.2950), picking up upstream's fixes
  between those versions. No fork feature behavior changed.

Container image: `ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.1`.

## [v4.0.17.2950+krzw.1] — based on Sonarr 4.0.17.2950

First documented fork release. Bundles every feature currently merged into
`personal/all-features-main`. Container image:
`ghcr.io/krz-w/sonarr:4.0.17.2950-krzw.1`.

### Added

- **Custom Format Priority Mode** — a per-custom-format **Priority** checkbox in the
  quality-profile editor. Priority CFs are compared *before* quality, so a language
  CF (e.g. VFQ) can win over a higher-quality release while still allowing quality
  upgrades within the same priority tier. Honored at grab, upgrade, and import.
  See [docs](docs/features/custom-format-priority-mode.md).
- **"Audio Title" custom format condition** — a new regex condition matching against
  each audio stream's title tag. Audio-track titles are now captured into
  `MediaInfoModel.AudioTitles`, enabling content-based VFQ detection for releases
  scene-named only "FRENCH". See [docs](docs/features/vfq-audio-title-detection.md).
- **Season-Pack Partial Fill** — an opt-in **Allow Season Pack Upgrades** setting
  (`All` / `Threshold` / `Any`, default `All` = unchanged) in *Settings → Media
  Management*, letting a full-season pack fill missing episodes / upgrade some
  episodes instead of being rejected all-or-nothing. Also lets a single-episode
  interactive search grab a matching full-season pack.
  See [docs](docs/features/season-pack-partial-fill.md).
- **Configurable Completed Download Handling interval** — a configurable CDH run
  interval (default 1 min; API-only, `checkForFinishedDownloadInterval` on
  `/api/v3/config/downloadclient`, no UI field), plus per-run start/duration
  logging, so a slow or hung CDH no longer silently starves manually-queued disk
  commands. See [docs](docs/features/completed-download-handling.md).
- **Configurable indexer cooldown** — `IndexerCooldownPeriods` (CSV of minutes) in
  *Settings → Indexers → Options (advanced)*, replacing the hard-coded escalation
  schedule for indexers only. See [docs](docs/features/configurable-indexer-cooldown.md).
- **Docker image + GHCR publishing** — multi-stage `Dockerfile` (.NET 6),
  LinuxServer.io-compatible entrypoint (PUID/PGID/TZ/UMASK, `/config` volume, port
  8989), and a GitHub Actions workflow that pushes to `ghcr.io/krz-w/sonarr`.
  See [docs](docs/features/docker-deployment.md).

### Changed

- Initial **grab/release selection** now ranks the priority-CF tier ahead of quality
  (`DownloadDecisionComparer`), matching the upgrade path. Backward compatible: with
  no CF flagged Priority, scores are 0 and ordering is unchanged.
- **Import-side upgrade decisions** mirror the grab-side priority-first logic, so a
  file grabbed for a priority language is no longer refused at import by a
  quality-only check.
- **Priority-upgrade rejection messages** now list the matched custom formats and
  absolute scores for both the new and existing file (previously only a delta).
- **MediaInfo schema** bumped 11 → 12 (CURRENT and MINIMUM) so existing files
  re-probe and gain `AudioTitles` on the next library scan.

### Fixed

- **MinFormatScore is now enforced at import time** (mirrors the grab-side
  `CustomFormatAllowedByProfileSpecification`). Adds the `CustomFormatMinimumScore`
  rejection reason. Because Sonarr has **no profile Language field**, custom formats
  are the only language lever — so this is what keeps a wrong-language file out of the
  library once its CF score drops below the profile minimum.
- **Regional language parsing** — added `en-CA` and `fr-CA` entries to `IsoLanguages`
  so regional tags in release filenames/subtitles (e.g. `FRENCH-CA`) parse correctly.
- **ffprobe is bundled** in the Docker image (ffmpeg apt package + symlink to
  `/app/ffprobe`); imports no longer fail at sample-detection with
  "Cannot determinate if file is a sample".
- **`groupadd`/`useradd` use `-o`** so PUID/PGID can reuse an existing GID/UID;
  fixes container start failure when `PGID=100` collides with Debian's `users` group.

[Unreleased]: https://github.com/KrZ-W/Sonarr/compare/v4.0.19.2979+krzw.9...HEAD
[v4.0.19.2979+krzw.9]: https://github.com/KrZ-W/Sonarr/releases/tag/v4.0.19.2979%2Bkrzw.9
[v4.0.19.2979+krzw.8]: https://github.com/KrZ-W/Sonarr/releases/tag/v4.0.19.2979%2Bkrzw.8
[v4.0.19.2979+krzw.7]: https://github.com/KrZ-W/Sonarr/releases/tag/v4.0.19.2979%2Bkrzw.7
[v4.0.19.2979+krzw.6]: https://github.com/KrZ-W/Sonarr/releases/tag/v4.0.19.2979%2Bkrzw.6
[v4.0.19.2979+krzw.5]: https://github.com/KrZ-W/Sonarr/releases/tag/v4.0.19.2979%2Bkrzw.5
[v4.0.19.2979+krzw.4]: https://github.com/KrZ-W/Sonarr/releases/tag/v4.0.19.2979%2Bkrzw.4
[v4.0.19.2979+krzw.3]: https://github.com/KrZ-W/Sonarr/releases/tag/v4.0.19.2979%2Bkrzw.3
[v4.0.19.2979+krzw.2]: https://github.com/KrZ-W/Sonarr/releases/tag/v4.0.19.2979%2Bkrzw.2
[v4.0.19.2979+krzw.1]: https://github.com/KrZ-W/Sonarr/releases/tag/v4.0.19.2979%2Bkrzw.1
[v4.0.17.2950+krzw.1]: https://github.com/KrZ-W/Sonarr/releases/tag/v4.0.17.2950%2Bkrzw.1
