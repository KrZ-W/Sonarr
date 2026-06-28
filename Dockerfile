# syntax=docker/dockerfile:1.7
#
# Custom Sonarr image built from this fork's source (v4 stable line).
# Conventions match lscr.io/linuxserver/sonarr so it's a drop-in
# image swap in existing docker-compose stacks:
#   - /config volume for app data
#   - PUID/PGID/TZ/UMASK env vars
#   - port 8989
#
# Build:
#   docker build -t krz-w/sonarr:dev .
# Run:
#   docker run -d --name sonarr -p 8989:8989 \
#       -e PUID=1000 -e PGID=1000 -e TZ=Europe/Paris \
#       -v /opt/stacks/sonarr/config:/config \
#       -v /mnt/media:/tv krz-w/sonarr:dev

# ----- frontend stage -----
FROM node:20.11.1-bookworm-slim AS frontend
WORKDIR /src

COPY package.json yarn.lock .yarnrc tsconfig.json ./
RUN yarn install --frozen-lockfile --network-timeout 600000

COPY frontend ./frontend
RUN yarn build --env production

# ----- backend stage -----
# Pin the EXACT SDK upstream Sonarr uses (global.json: 6.0.405 -> .NET 6.0.13
# runtime). This is load-bearing: newer 6.0.x SDKs resolve the out-of-band
# System.* / Microsoft.Extensions.* packages (e.g. CodePages 8.0.0) against a
# newer framework manifest, producing a self-contained bundle whose assembly
# versions don't match at runtime -> FileLoadException crash-loop at bootstrap.
# 6.0.405 reproduces the official self-contained build exactly.
FROM mcr.microsoft.com/dotnet/sdk:6.0.405 AS backend
WORKDIR /src

RUN printf '{"sdk":{"version":"6.0.405"}}\n' > global.json
COPY src ./src
COPY Logo ./Logo

RUN dotnet msbuild -restore src/Sonarr.sln \
        -p:Configuration=Release \
        -p:Platform=Posix \
        -p:RuntimeIdentifiers=linux-x64 \
        -p:NuGetAudit=false \
        -p:RunAnalyzersDuringBuild=false \
        -p:TreatWarningsAsErrors=false \
        -t:PublishAllRids

# ----- runtime stage -----
# Framework-dependent on the .NET 6 ASP.NET shared framework, matching upstream
# (src/Directory.Build.props sets <SelfContained>false</SelfContained>). The crash-loop
# we hit on 4.0.18+ (System.Text.Encoding.CodePages / Microsoft.Extensions.* FileLoad-
# Exceptions) was NOT framework-dependent vs self-contained — it was the BUILD SDK: a
# newer 6.0.x SDK resolves these out-of-band package refs to net7/net8 assets that don't
# load on the net6 runtime. Pinning the build to SDK 6.0.405 (above) bundles the correct
# net6.0 assets, exactly as the official Sonarr build does.
FROM mcr.microsoft.com/dotnet/aspnet:6.0-bookworm-slim AS runtime

ENV PUID=1000 \
    PGID=1000 \
    TZ=Etc/UTC \
    UMASK=002 \
    XDG_CONFIG_HOME=/config \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        gosu \
        tzdata \
        curl \
        ca-certificates \
        sqlite3 \
        libsqlite3-0 \
        ffmpeg \
        jq \
    && rm -rf /var/lib/apt/lists/*

COPY --from=backend /src/_output/net6.0/linux-x64/publish/ /app/
COPY --from=backend /src/_output/Sonarr.Update/net6.0/linux-x64/publish/ /app/Sonarr.Update/
COPY --from=frontend /src/_output/UI/ /app/UI/

RUN rm -f /app/ServiceInstall.* /app/ServiceUninstall.* /app/Sonarr.Windows.*

# FFMpegCore (Sonarr's media-probe wrapper) looks for ffprobe next to the binary
# before falling back to PATH. Symlink the apt-installed one into /app so behavior
# matches the upstream release distribution and lscr.io/linuxserver/sonarr's layout.
RUN ln -sf /usr/bin/ffprobe /app/ffprobe

COPY docker/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh /app/Sonarr

VOLUME ["/config"]
EXPOSE 8989

HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -fsS http://localhost:8989/ping || exit 1

ENTRYPOINT ["/entrypoint.sh"]
