# Docker / GHCR Deployment

> **Status:** stable · **Since:** `v4.0.17.2950+krzw.1` · **Image:** `ghcr.io/krz-w/sonarr`

## What it does

Adds a multi-stage **`Dockerfile`** that builds Sonarr v4 from source against **.NET 6**
(Sonarr v4's target framework) and produces a runtime image following
**LinuxServer.io-compatible** conventions, plus a **GitHub Actions** workflow that
publishes to **GitHub Container Registry (GHCR)** at `ghcr.io/krz-w/sonarr`. It is a
drop-in replacement for `lscr.io/linuxserver/sonarr` — swap the image line in your compose
file and keep the same env/volumes.

## Image conventions

| Aspect | Value |
|---|---|
| Registry / image | `ghcr.io/krz-w/sonarr` |
| Web UI port | `8989` |
| Config volume | `/config` |
| User mapping | `PUID` / `PGID` (default `1000` / `1000`) |
| Timezone | `TZ` (default `Etc/UTC`) |
| File mode | `UMASK` (default `002`) |
| Healthcheck | `GET http://localhost:8989/ping` |

## Image tags

| Tag | Points at | Use for |
|---|---|---|
| `4.0.19.2979-krzw.21` | a tagged release (immutable) | **production — pin to this** |
| `latest` | tip of `personal/all-features-main` | bleeding edge |
| `personal-all-features-main` | same branch (ref tag) | bleeding edge |
| `sha-<short>` | a specific commit | debugging / rollback |

> Release tags use `-krzw.N` because container registries don't allow `+` in tags; the
> matching git tag / GitHub release uses `+krzw.N`. See [../../FORK.md](../../FORK.md#versioning).

## Quick start

### docker run

```bash
docker run -d --name sonarr \
  -p 8989:8989 \
  -e PUID=1000 -e PGID=1000 -e TZ=Europe/Paris -e UMASK=002 \
  -v /path/to/config:/config \
  -v /path/to/tv:/tv \
  -v /path/to/downloads:/downloads \
  ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.21
```

### docker-compose

```yaml
services:
  sonarr:
    image: ghcr.io/krz-w/sonarr:4.0.19.2979-krzw.21
    container_name: sonarr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=Europe/Paris
      - UMASK=002
    volumes:
      - /path/to/config:/config
      - /path/to/tv:/tv
      - /path/to/downloads:/downloads
    ports:
      - 8989:8989
    restart: unless-stopped
```

## Notes & gotchas

- **ffprobe is bundled.** Recent Sonarr probes media with ffprobe (via `FFMpegCore`).
  The image installs `ffmpeg` and symlinks ffprobe to `/app/ffprobe` so `FFMpegCore`
  finds it locally; `jq` is included for LSIO-style script compatibility. Without this,
  imports fail with *"Cannot determinate if file is a sample"*.
- **mkvpropedit is bundled** (for [Audio Track Retag](audio-track-retag.md)): the `mkvtoolnix`
  package is installed, only `/usr/bin/mkvpropedit` is kept (`mkvmerge`/`mkvextract`/`mkvinfo`
  are removed) and it is symlinked to `/app/mkvpropedit`, resolved the same way `ffmpeg` is.
- **PUID/PGID can reuse existing IDs.** The entrypoint runs `groupadd -o` / `useradd -o`,
  so a `PGID=100` (a common Proxmox/LXC default that collides with Debian's `users`
  group) no longer crashes container start.
- **`:latest` is bleeding edge, not stable.** It tracks the primary branch tip. Pin a
  `…-krzw.N` tag for anything you care about.
- **Platform:** images are built for `linux/amd64`.

## Building locally

```bash
docker build -t sonarr-fork .
```

## Source

Commits: `889946b78` (Dockerfile + workflow + entrypoint, ffprobe), `5f226e659`
(`-o` GID/UID reuse), `5bb406f4f` (SDK pinned to 6.0.405 — newer 6.0.4xx SDKs produced
an image that crash-looped at runtime; do not unpin without boot-testing). Key files: `Dockerfile`, `docker/entrypoint.sh`, `.dockerignore`,
`.github/workflows/docker-image.yml`.
