# KrZ-W/Sonarr User Guide

Task-oriented walkthroughs for the fork's features. Each recipe is self-contained.
For the *why* and full reference, follow the links into [features/](features/).

**Contents**

- [Recipe: Make VFQ win over higher-quality audio](#recipe-make-vfq-win-over-higher-quality-audio)
- [Recipe: Detect VFQ from audio tracks](#recipe-detect-vfq-from-audio-tracks)
- [Recipe: Stop wrong-language files from importing](#recipe-stop-wrong-language-files-from-importing)
- [Recipe: Fill a partial season from a season pack](#recipe-fill-a-partial-season-from-a-season-pack)
- [Recipe: Keep CDH from blocking manual imports](#recipe-keep-cdh-from-blocking-manual-imports)
- [Recipe: Add missing French/Quebec series titles](#recipe-add-missing-frenchquebec-series-titles)
- [Recipe: Let IMDb fill in French/Quebec series titles automatically](#recipe-let-imdb-fill-in-frenchquebec-series-titles-automatically)
- [Recipe: Tune indexer cooldown](#recipe-tune-indexer-cooldown)
- [Recipe: Run the fork in Docker](#recipe-run-the-fork-in-docker)

---

## Recipe: Make VFQ win over higher-quality audio

**Goal:** Sonarr should prefer a Quebec-French release even when a higher-quality
non-VFQ one exists, but still upgrade quality *within* VFQ.

1. **Settings → Custom Formats** — create a `VFQ` custom format (title regex such as
   `VFQ|VOQ|TRUEFRENCH`). To also catch VFQ hidden in generic `FRENCH` releases, add a
   **separate** audio-based format — see
   [Detect VFQ from audio tracks](#recipe-detect-vfq-from-audio-tracks). Do **not** put
   an Audio Title condition inside this title format: the condition groups AND together
   and the format would stop matching at grab time.
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
2. **Settings → Custom Formats → Add** — create a **separate** format, e.g.
   `VFQ (Audio)` (not a condition inside your title VFQ format — see the warning in
   the recipe above).
3. Add an **Audio Title** condition (Required) with a regex built from what you saw,
   e.g. `\bVFQ\b|\bVOQ\b|Qu[eé]b|Canad`.
4. Add a **negated, Required Release Title** condition containing your title-VFQ regex,
   so the two formats are mutually exclusive and a file scores VFQ only once.
5. Save, then in your quality profile give `VFQ (Audio)` the **same score and Priority
   flag** as the title VFQ format (recipe above).
6. **Trigger a library scan** so existing files re-probe and gain audio titles.

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

## Recipe: Rescue mislabeled French audio

**Goal:** a season pack named `FRENCH`/`VFQ` whose episodes carry their French track
tagged `eng` or `und` (very common with Quebec dubs: GRIMM, X-Files, Walking Dead…)
should import as French instead of being rejected by your "Not French" custom format —
and a pack that merely *claims* French should still be refused, with a reason that says
the audio was actually checked.

1. Run a [whisper-asr-webservice](https://github.com/ahmetoner/whisper-asr-webservice)
   container reachable from Sonarr (the same one Bazarr's Whisper provider uses; the
   `base` model is enough for language identification):

   ```yaml
   whisper:
     image: onerahmet/openai-whisper-asr-webservice:latest
     environment:
       - ASR_MODEL=base
       - ASR_ENGINE=faster_whisper
   ```

2. **Settings → Media Management → show advanced → Audio Language Verification:** tick
   **Enable**, set **Whisper Endpoint** to `http://whisper:9000`, keep the defaults
   (threshold `0.85`, clip at 300 s for 30 s, *Verify Tagged Tracks* = Never). Save.

3. Make sure the quality profile of the series you care about uses a custom format with a
   **Language** condition (a positively scored *Language: French*, or the negatively
   scored *Not French* from the [wrong-language recipe](#recipe-stop-wrong-language-files-from-importing)).
   Sonarr has no profile Language field: without a language custom format, nothing is
   ever probed.

That's it. On the next import whose tags disagree with the release claim, Sonarr extracts
a clip of each suspicious track with the bundled `ffmpeg`, asks Whisper once per distinct
track layout in the pack, and:

- a confident `fr` detection makes the episodes import with `Languages = French` — the
  "Not French" custom format no longer matches and a French release is not offered as an
  upgrade;
- no French anywhere keeps the rejection, now worded
  `… Audio verified (detected en 0.97, en 0.94)`;
- Whisper down or slow → one warning in the log and the import behaves exactly as before
  you enabled the feature.

The outcome is stored per track on each episode file (`audioLanguageVerification` in
`GET /api/v3/episodefile?seriesId=…`) and is left untouched by *Rescan Series*. Files are
never modified; the planned *Audio Track Retag* feature will use that record.

> **Cost:** one ffmpeg extraction and one Whisper call per suspicious track per distinct
> track layout in a download — a 24-episode pack with identical layouts costs one probe.
> Set *Verify Tagged Tracks* to *For release groups* with a group list only if you know a
> group mislabels tracks that *look* right.

> Full reference: [Audio Language Verification](features/audio-language-verification.md).

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

1. Set the interval via the API (**there is no UI field** for this setting) — field
   `checkForFinishedDownloadInterval` on `/api/v3/config/downloadclient`, e.g. `2`–`5`
   minutes to leave a quiet gap after each run. See the
   [full reference](features/completed-download-handling.md#configuration) for a
   copy-paste `curl` command.
2. It applies immediately (no restart).
3. Watch the log: each run logs its start and duration; a run that **exceeds** the
   interval is logged at **Warn**, and a start with no completion means it's stuck.

> Full reference: [Completed Download Handling](features/completed-download-handling.md).

---

## Recipe: Add missing French/Quebec series titles

**Goal:** make releases named after a French/QC series title match their series when
TheTVDB and the scene-mapping services lack that title — permanently (user mappings
are never touched by mapping updates or refreshes).

1. Prepare a JSON file in the curated-dataset format (tvdb-keyed):

   ```json
   [
     { "tvdbId": 81189, "imdbId": "tt0903747", "seriesTitle": "Breaking Bad", "year": 2008,
       "missingFrenchTitles": [ { "title": "Le Chimiste d'Albuquerque", "region": "QC" } ] }
   ]
   ```

2. Import it:

   ```bash
   curl -X POST "http://<host>:8989/api/v3/scenemapping/user/import" \
     -H "X-Api-Key: <api-key>" -H "Content-Type: application/json" \
     -d @curated-tv.json
   ```

3. Check the summary response; entries under `seriesNotFound` aren't in your library.

Imported titles appear in the series page's **Alternate Titles** list and are safe to
re-import after adding series (already-present titles are skipped).

> Full reference: [User Scene Mappings](features/user-scene-mappings.md).

---

## Recipe: Let IMDb fill in French/Quebec series titles automatically

**Goal:** stop maintaining a curated JSON file — have the instance pull the missing
titles from IMDb's public dataset itself, for the library and for every series you add.

1. *Settings → Metadata → IMDb Title Provider*: tick **Enable**. Defaults: Regions
   `CA,FR`, Languages `fr`, Refresh Interval `7` days. Save.
2. *System → Tasks* → run **Imdb Title Dataset Refresh** once (or
   `POST /api/v3/command` with `{"name":"ImdbTitleDatasetRefresh"}`). The first run
   downloads ~300 MB; the log ends with `IMDb titles applied to library: …`.
3. Spot-check a series: its *Alternate Titles* list (or `GET /api/v3/series/<id>` →
   `alternateTitles`) shows the new titles; a French release name now parses to it.

From now on the dataset is re-downloaded on the interval (skipped when IMDb reports it
unchanged), every series you add is synced within seconds, and a health warning appears
if the index is missing or has not been rebuilt for twice the interval. Only series with
an IMDb id are covered.

> **Licence:** IMDb's datasets are for personal, non-commercial use. The fork downloads
> them on your instance only and never redistributes them.

> Full reference: [IMDb Title Provider](features/imdb-title-provider.md).

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
    image: ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.18   # pin to a release
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
