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
- **Current fork version:** `v4.0.19.2979+krzw.2`

> The stock upstream `README.md` is preserved below this fork section. Everything
> KrZ-W-specific lives in [`docs/`](docs/) and [`CHANGELOG.md`](CHANGELOG.md).

## Features at a glance

| Feature | What it does | Docs |
|---|---|---|
| **Custom Format Priority Mode** | Per-CF "Priority" flag so a language CF (e.g. VFQ) wins over quality, with downgrade protection | [features/custom-format-priority-mode.md](docs/features/custom-format-priority-mode.md) |
| **VFQ Audio-Title Detection** | New "Audio Title" custom format condition that reads audio-track title tags, so VFQ is detected from content, not just the release name | [features/vfq-audio-title-detection.md](docs/features/vfq-audio-title-detection.md) |
| **Import-time Enforcement** | Enforces MinFormatScore at *import*, not just at grab (Sonarr has no profile Language field — CFs are the only language lever) | [features/import-time-enforcement.md](docs/features/import-time-enforcement.md) |
| **Season-Pack Partial Fill** | Let a full-season pack fill *missing* episodes / upgrade *some* episodes, instead of all-or-nothing | [features/season-pack-partial-fill.md](docs/features/season-pack-partial-fill.md) |
| **Completed Download Handling Interval** | Make the CDH run interval configurable + log per-run duration, so a slow CDH stops starving manual imports | [features/completed-download-handling.md](docs/features/completed-download-handling.md) |
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
git tag      v4.0.17.2950+krzw.1
docker image ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.2
```

See [docs/releasing.md](docs/releasing.md) for how to cut a release.

> **In-app version:** the version Sonarr shows in *System → Status* comes from
> upstream's build machinery and is **not** changed by this fork. Use the git/image
> tag above as the source of truth for "which fork build am I running".

## Pulling the image

```bash
# Pinned to a release (recommended for stability)
docker pull ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.2

# Bleeding edge — tip of personal/all-features-main
docker pull ghcr.io/krz-w/sonarr:latest
```

See [features/docker-deployment.md](docs/features/docker-deployment.md) for a full
`docker run` / compose example.

## Relationship to upstream

- `upstream` remote → `Sonarr/Sonarr` (the real project, v4 line)
- `myfork` remote → `KrZ-W/Sonarr` (this fork)
- Each feature lives on its own `feature/*` or `fix/*` branch and is merged into
  `personal/all-features-main`. See [CHANGELOG.md](CHANGELOG.md) for per-feature history.
