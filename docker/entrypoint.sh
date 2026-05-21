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
    groupadd -g "${PGID}" app
fi
if ! id -u app >/dev/null 2>&1; then
    useradd -u "${PUID}" -g "${PGID}" -d /config -s /usr/sbin/nologin -M app
fi

mkdir -p /config
chown -R "${PUID}:${PGID}" /config

exec gosu "${PUID}:${PGID}" /app/Sonarr -nobrowser -data=/config "$@"
