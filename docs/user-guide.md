# KrZ-W/Sonarr User Guide

Task-oriented walkthroughs for the fork's features. Each recipe is self-contained.
For the *why* and full reference, follow the links into [features/](features/).

**Contents**

- [Recipe: Make VFQ win over higher-quality audio](#recipe-make-vfq-win-over-higher-quality-audio)
- [Recipe: Detect VFQ from audio tracks](#recipe-detect-vfq-from-audio-tracks)
- [Recipe: Stop wrong-language files from importing](#recipe-stop-wrong-language-files-from-importing)
- [Recipe: Fill a partial season from a season pack](#recipe-fill-a-partial-season-from-a-season-pack)
- [Recipe: Keep CDH from blocking manual imports](#recipe-keep-cdh-from-blocking-manual-imports)
- [Recipe: Tune indexer cooldown](#recipe-tune-indexer-cooldown)
- [Recipe: Run the fork in Docker](#recipe-run-the-fork-in-docker)

---

## Recipe: Make VFQ win over higher-quality audio

**Goal:** Sonarr should prefer a Quebec-French release even when a higher-quality
non-VFQ one exists, but still upgrade quality *within* VFQ.

1. **Settings → Custom Formats** — create a `VFQ` custom format (title regex such as
   `VFQ|VOQ|TRUEFRENCH`, and ideally an [Audio Title condition](#recipe-detect-vfq-from-audio-tracks)).
2. **Settings → Profiles → Quality → (your profile)** — find the `VFQ` format, give it a
   positive **score**, and tick the **Priority** checkbox.
3. Save.

Now VFQ is compared *before* quality: a VFQ release wins the grab and is protected from
being replaced by a non-VFQ upgrade, while two VFQ releases still compare by quality.

> Full reference: [Custom Format Priority Mode](features/custom-format-priority-mode.md).

---

## Recipe: Detect VFQ from audio tracks

**Goal:** catch VFQ releases scene-named only `FRENCH` (no VFQ token), using the audio
track's title.

1. Find the real audio labels: open a known-VFQ file's **Media Info** in Sonarr, or run
   `ffprobe yourfile.mkv` and read each audio stream's `title` (e.g. `French [Canada]`).
2. **Settings → Custom Formats → (your VFQ format) → Add Condition → Audio Title.**
3. Enter a regex built from what you saw, e.g. `VFQ|VOQ|Qu[ée]b|Canad`.
4. Save, then flag the format **Priority** in your quality profile (recipe above).
5. **Trigger a library scan** so existing files re-probe and gain audio titles.

> Full reference: [VFQ Audio-Title Detection](features/vfq-audio-title-detection.md).

---

## Recipe: Stop wrong-language files from importing

**Goal:** don't let a file with the wrong audio language land in your library, even if
the release name fooled the grab-time check.

Sonarr has **no quality-profile Language field**, so language is enforced through
**custom formats + MinFormatScore**, and the fork now re-checks that score **at import**:

1. **Settings → Custom Formats** — make a format that scores your wanted language
   positively (use an [Audio Title](#recipe-detect-vfq-from-audio-tracks) condition for
   reliability) and/or unwanted languages negatively.
2. **Settings → Profiles → Quality → (your profile)** — set **Minimum Custom Format
   Score** so that a file in the wrong language falls below it.
3. Save.

A file that fails will be rejected at import with reason `CustomFormatMinimumScore`.

> Full reference: [Import-time Enforcement](features/import-time-enforcement.md).

---

## Recipe: Fill a partial season from a season pack

**Goal:** grab a full-season pack to fill the episodes you're missing, instead of having
the grab rejected because some episodes are already present.

1. **Settings → Media Management → Allow Season Pack Upgrades** — choose:
   - `Any` — grab a pack if it helps with **even one** episode, or
   - `Threshold` — grab only if it fills/upgrades at least *N%* of the season, then set
     the percentage (e.g. `50`).
   - (`All` is the default and keeps stock all-or-nothing behavior.)
2. Save. Only the missing/upgradable episodes are imported from the pack.

> Full reference: [Season-Pack Partial Fill](features/season-pack-partial-fill.md).

---

## Recipe: Keep CDH from blocking manual imports

**Goal:** stop a slow Completed Download Handling run from starving your manual imports
and renames (they share a single disk-access slot).

1. **Settings → Download Clients** — set the **Completed Download Handling interval** to
   leave a quiet gap after each run (e.g. `2`–`5` minutes).
2. Save (applies immediately).
3. Watch the log: each run logs its start and duration; a run that **exceeds** the
   interval is logged at **Warn**, and a start with no completion means it's stuck.

> Full reference: [Completed Download Handling Interval](features/completed-download-handling.md).

---

## Recipe: Tune indexer cooldown

**Goal:** change how long a failing indexer is backed off.

1. **Settings → Indexers** — enable *Advanced Settings* (toggle, top-right).
2. **Options → Indexer Cooldown Periods** — enter a CSV of minutes starting with `0`,
   e.g. `0,2,10,30,120`. Leave empty to keep the upstream default.
3. Save.

> Full reference: [Configurable Indexer Cooldown](features/configurable-indexer-cooldown.md).

---

## Recipe: Run the fork in Docker

**Goal:** run this fork as a container, pinned to a known version.

```yaml
services:
  sonarr:
    image: ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.1   # pin to a release
    container_name: sonarr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=Europe/Paris
      - UMASK=002
    volumes:
      - ./config:/config
      - /srv/tv:/tv
      - /srv/downloads:/downloads
    ports:
      - 8989:8989
    restart: unless-stopped
```

- Use `:latest` instead of the pinned tag for the bleeding-edge primary branch.
- `PGID=100` is safe on this image (the entrypoint uses `groupadd -o`).

> Full reference: [Docker / GHCR Deployment](features/docker-deployment.md).
