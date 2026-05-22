#!/bin/bash
set -euo pipefail

PUID="${PUID:-1000}"
PGID="${PGID:-1000}"
TZ="${TZ:-Etc/UTC}"
UMASK="${UMASK:-002}"

if [ -f "/usr/share/zoneinfo/${TZ}" ]; then
    ln -snf "/usr/share/zoneinfo/${TZ}" /etc/localtime
    echo "${TZ}" > /etc/timezone
fi

umask "${UMASK}"

if ! getent group app >/dev/null 2>&1; then
    # -o lets the new "app" group reuse a GID that's already taken by another
    # group on the base image (e.g. PGID=100 collides with the default "users"
    # group on Debian, which would otherwise make groupadd refuse).
    groupadd -o -g "${PGID}" app
fi
if ! id -u app >/dev/null 2>&1; then
    # -o for the same reason on the UID side.
    useradd -o -u "${PUID}" -g "${PGID}" -d /config -s /usr/sbin/nologin -M app
fi

mkdir -p /config
chown -R "${PUID}:${PGID}" /config

exec gosu "${PUID}:${PGID}" /app/Sonarr -nobrowser -data=/config "$@"
