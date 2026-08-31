# KrZ-W/Sonarr Documentation

Documentation for the KrZ-W fork of Sonarr (v4). For the high-level overview and
versioning scheme, see [`../FORK.md`](../FORK.md). For the release history, see
[`../CHANGELOG.md`](../CHANGELOG.md).

## User Guide

- **[User Guide](user-guide.md)** — task-oriented walkthroughs. Start here if you just
  want to *use* a feature ("make VFQ win over higher-quality audio", "fill a partial
  season from a pack", "run the fork in Docker").

## Feature Reference

| Feature | Summary |
|---|---|
| [Custom Format Priority Mode](features/custom-format-priority-mode.md) | Per-CF "Priority" flag: language beats quality, with downgrade protection |
| [VFQ Audio-Title Detection](features/vfq-audio-title-detection.md) | "Audio Title" CF condition reads audio-track titles for content-based VFQ grading |
| [Import-time Enforcement](features/import-time-enforcement.md) | MinFormatScore enforced at import, not just grab |
| [Atomic Upgrade Imports](features/atomic-upgrade-imports.md) | Existing file survives unless the replacement import fully commits; failed upgrades restore everything |
| [Season-Pack Partial Fill](features/season-pack-partial-fill.md) | Full-season packs can fill missing / upgrade some episodes |
| [Completed Download Handling Interval](features/completed-download-handling.md) | Configurable CDH interval + per-run logging |
| [Regional Language Parsing](features/regional-language-parsing.md) | `en-CA` / `fr-CA` parsing entries |
| [Configurable Indexer Cooldown](features/configurable-indexer-cooldown.md) | Editable indexer back-off/escalation schedule |
| [Docker / GHCR Deployment](features/docker-deployment.md) | LinuxServer.io-style image published to GHCR |

## Maintainer

- **[Releasing](releasing.md)** — how to cut a versioned release (tag → image → GitHub release).

## Conventions used in these docs

- **"Grab time"** = when Sonarr decides which release to download.
- **"Import time"** = when a downloaded file is evaluated and moved into the library.
- **CF** = Custom Format.
- This fork targets the Sonarr **v4** line.
