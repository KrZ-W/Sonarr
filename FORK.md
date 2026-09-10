# KrZ-W/Sonarr — Fork Notes

This is a personal fork of [Sonarr](https://github.com/Sonarr/Sonarr) (the **v4**
line) that adds language-aware grabbing/importing (Quebec French / VFQ in particular),
smarter season-pack handling, and self-hosted Docker deployment. It is maintained by a
single person for a private *arr stack and is not affiliated with the Sonarr team.

Several features are shared with the sibling [KrZ-W/Radarr](https://github.com/KrZ-W/Radarr)
fork; this document describes the Sonarr versions specifically.

- **Upstream base:** Sonarr `4.0.19.2979`
- **Primary branch:** `personal/all-features-main` (all features merged together)
- **Container image:** `ghcr.io/krz-w/sonarr`
- **Current fork version:** `v4.0.19.2979+krzw.18`

> The stock upstream `README.md` is preserved below this fork section. Everything
> KrZ-W-specific lives in [`docs/`](docs/) and [`CHANGELOG.md`](CHANGELOG.md).

## Features at a glance

| Feature | What it does | Docs |
|---|---|---|
| **Custom Format Priority Mode** | Per-CF "Priority" flag so a language CF (e.g. VFQ) wins over quality, with downgrade protection | [features/custom-format-priority-mode.md](docs/features/custom-format-priority-mode.md) |
| **VFQ Audio-Title Detection** | New "Audio Title" custom format condition that reads audio-track title tags, so VFQ is detected from content, not just the release name | [features/vfq-audio-title-detection.md](docs/features/vfq-audio-title-detection.md) |
| **Import-time Enforcement** | Enforces MinFormatScore at *import*, not just at grab (Sonarr has no profile Language field — CFs are the only language lever) | [features/import-time-enforcement.md](docs/features/import-time-enforcement.md) |
| **Atomic Upgrade Imports** | An upgrade never deletes the existing file until the replacement is fully imported (file + DB row); failures restore the original and re-queue the download | [features/atomic-upgrade-imports.md](docs/features/atomic-upgrade-imports.md) |
| **Season-Pack Partial Fill** | Let a full-season pack fill *missing* episodes / upgrade *some* episodes, instead of all-or-nothing | [features/season-pack-partial-fill.md](docs/features/season-pack-partial-fill.md) |
| **User Scene Mappings** | Bulk-import missing FR/QC series titles as `Type=User` scene mappings (refresh-proof by design); JSON import endpoint | [features/user-scene-mappings.md](docs/features/user-scene-mappings.md) |
| **IMDb Title Provider** | Downloads IMDb's alternative-title dataset on a schedule and feeds the missing French/Quebec series titles into the user scene-mapping importer automatically (new series within seconds); no external scripts | [features/imdb-title-provider.md](docs/features/imdb-title-provider.md) |
| **Audio Language Verification** | Listens to a short clip of suspicious audio tracks with a self-hosted Whisper server at import, so a French track mistagged `eng`/`und` imports as French (and a truly non-French pack is rejected with an "Audio verified" reason); one probe per track layout per season pack; stores the per-track outcome on the file, never modifies files | [features/audio-language-verification.md](docs/features/audio-language-verification.md) |
| **Completed Download Handling** | Make the CDH run interval configurable + log per-run duration, so a slow CDH stops starving manual imports; stuck `ImportPending`/`ImportBlocked` items self-heal back to `Downloading` | [features/completed-download-handling.md](docs/features/completed-download-handling.md) |
| **Regional Language Parsing** | Adds `en-CA` / `fr-CA` parsing entries so regional tags in filenames resolve correctly | [features/regional-language-parsing.md](docs/features/regional-language-parsing.md) |
| **Configurable Indexer Cooldown** | Make the indexer back-off/escalation schedule editable | [features/configurable-indexer-cooldown.md](docs/features/configurable-indexer-cooldown.md) |
| **Docker / GHCR Deployment** | LinuxServer.io-style image (PUID/PGID/TZ/UMASK, `/config`, ffprobe bundled) published to GHCR | [features/docker-deployment.md](docs/features/docker-deployment.md) |

New here? Start with the **[User Guide](docs/user-guide.md)** for task-oriented walkthroughs.

## Versioning

This fork uses the upstream build version plus a fork counter as
[SemVer build metadata](https://semver.org/#spec-item-10):

```
v<upstream-version>+krzw.<N>
        │                │
        │                └─ fork release number on this base; resets to 1 on each rebase
        └─ the Sonarr version this fork is rebased onto (e.g. 4.0.17.2950)
```

Examples:

| Git tag | Meaning |
|---|---|
| `v4.0.17.2950+krzw.1` | First fork release, based on Sonarr 4.0.17.2950 |
| `v4.0.17.2950+krzw.2` | Second fork release, **same** upstream base |
| `v4.1.0.xxxx+krzw.1` | First release after rebasing onto a newer Sonarr |

The `+` is valid in git tags / GitHub releases / SemVer but **not** in container image
tags, so the Docker tag replaces `+` with `-`:

```
git tag      v4.0.19.2979+krzw.18
docker image ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.18
```

See [docs/releasing.md](docs/releasing.md) for how to cut a release.

> **In-app version:** the version Sonarr shows in *System → Status* comes from
> upstream's build machinery and is **not** changed by this fork. Use the git/image
> tag above as the source of truth for "which fork build am I running".

## Pulling the image

```bash
# Pinned to a release (recommended for stability)
docker pull ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.18

# Bleeding edge — tip of personal/all-features-main
docker pull ghcr.io/krz-w/sonarr:latest
```

See [features/docker-deployment.md](docs/features/docker-deployment.md) for a full
`docker run` / compose example.

## Upstream issues this fork relates to

For people arriving from an upstream issue: the table below maps each fork feature to the
Sonarr issues it addresses or was declined as. Each feature page has an **Upstream** section
with details. This fork does not submit changes upstream.

| Upstream issue | State | Fork feature | Relationship |
|---|---|---|---|
| [Sonarr#8806](https://github.com/Sonarr/Sonarr/issues/8806) user-added alternate titles for search | closed, not planned (2026-07) | [User Scene Mappings](docs/features/user-scene-mappings.md) | Implemented here |
| [Sonarr#6233](https://github.com/Sonarr/Sonarr/issues/6233), [#6166](https://github.com/Sonarr/Sonarr/issues/6166), [#8058](https://github.com/Sonarr/Sonarr/issues/8058) custom aliases / alias search control | closed, not planned | [User Scene Mappings](docs/features/user-scene-mappings.md) | Implemented here |
| [Sonarr#3394](https://github.com/Sonarr/Sonarr/issues/3394), [#6762](https://github.com/Sonarr/Sonarr/issues/6762) language before quality | closed | [Custom Format Priority Mode](docs/features/custom-format-priority-mode.md) | Declined upstream; fork-only by design |
| [Sonarr#5598](https://github.com/Sonarr/Sonarr/issues/5598) CF comparison release vs file | open, discussion | [VFQ Audio-Title Detection](docs/features/vfq-audio-title-detection.md) | Related discussion |
| [Sonarr#269](https://github.com/Sonarr/Sonarr/issues/269) TVDB data in other languages | open | [Regional Language Parsing](docs/features/regional-language-parsing.md) | Narrow fix under that umbrella |
| [Radarr#8444](https://github.com/Radarr/Radarr/issues/8444) deleted-event flood on failed imports | open (Radarr) | [Atomic Upgrade Imports](docs/features/atomic-upgrade-imports.md) | Same flaw existed in Sonarr; fixed here |
| [Sonarr#8453](https://github.com/Sonarr/Sonarr/issues/8453) external audio language provider / AI-assisted tagging | not planned (2026) | [Audio Language Verification](docs/features/audio-language-verification.md) | Implemented natively (Whisper detection at import; retag is a planned follow-up) |
| [Sonarr#7523](https://github.com/Sonarr/Sonarr/issues/7523), [#5225](https://github.com/Sonarr/Sonarr/issues/5225) reject / fail wrong-language downloads | not planned / closed | [Audio Language Verification](docs/features/audio-language-verification.md), [Import-time Enforcement](docs/features/import-time-enforcement.md) | Rejected at import here, on verified audio |

## Relationship to upstream

- The clone has one remote, `origin` → `KrZ-W/Sonarr` (this fork). Upstream
  `Sonarr/Sonarr` is fetched by URL when rebasing (see
  [docs/releasing.md](docs/releasing.md)); `origin/main`, `origin/develop` and
  `origin/v5-develop` are upstream mirrors refreshed by hand, not the base.
- Every fork change to an upstream file carries a `krzw(<feature>)` marker comment
  (`// krzw(atomic-upgrade): ...`), so fork hunks are identifiable at rebase time;
  `git grep -n 'krzw('` lists them. Files that cannot hold comments (`en.json`,
  generated `*.css.d.ts`) are the only unmarked ones.
- Each feature lives on its own `feature/*` or `fix/*` branch cut from the upstream
  release tag the fork is based on (`-main` suffix = upstream main line), and is merged into
  `personal/all-features-main`. See [CHANGELOG.md](CHANGELOG.md) for per-feature history.
