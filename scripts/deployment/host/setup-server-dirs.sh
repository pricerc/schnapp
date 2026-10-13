#!/usr/bin/env bash
# Create Schnapp system user and install directories on the server.
#
# Usage:
#   sudo ./scripts/deployment/host/setup-server-dirs.sh
#   sudo ./scripts/deployment/host/setup-server-dirs.sh --prefix /opt/schnapp
#
# Creates:
#   /opt/schnapp/{api,web,data/blobs,config}
#   system user: schnapp (nologin)
set -euo pipefail

PREFIX="${SCHNAPP_PREFIX:-/opt/schnapp}"
APP_USER="${SCHNAPP_USER:-schnapp}"
APP_GROUP="${SCHNAPP_GROUP:-$APP_USER}"

usage() {
  sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'
  cat <<'EOF'

Options:
  -p, --prefix DIR    Install root (default: /opt/schnapp)
  -u, --user NAME     System user (default: schnapp)
  -g, --group NAME    System group (default: same as user)
  -h, --help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    -p|--prefix) PREFIX="$2"; shift 2 ;;
    -u|--user) APP_USER="$2"; shift 2 ;;
    -g|--group) APP_GROUP="$2"; shift 2 ;;
    *) echo "unknown option: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ "$(id -u)" -ne 0 ]]; then
  echo "error: run as root (sudo)" >&2
  exit 1
fi

if ! getent group "$APP_GROUP" >/dev/null; then
  groupadd --system "$APP_GROUP"
  echo "created group ${APP_GROUP}"
fi

if ! id "$APP_USER" >/dev/null 2>&1; then
  useradd --system --gid "$APP_GROUP" --home-dir "$PREFIX" --shell /sbin/nologin \
    --comment "Schnapp service" "$APP_USER"
  echo "created user ${APP_USER}"
else
  echo "user ${APP_USER} already exists"
fi

mkdir -p \
  "${PREFIX}/api" \
  "${PREFIX}/web" \
  "${PREFIX}/data/blobs" \
  "${PREFIX}/config"

chown -R "${APP_USER}:${APP_GROUP}" "$PREFIX"
chmod 755 "$PREFIX" "${PREFIX}/api" "${PREFIX}/web" "${PREFIX}/data"
chmod 750 "${PREFIX}/config" "${PREFIX}/data/blobs"

echo
echo "Layout ready under ${PREFIX}:"
echo "  api/   web/   data/blobs/   config/"
echo "  owner: ${APP_USER}:${APP_GROUP}"
echo
echo "BlobRoot default: ${PREFIX}/data/blobs"
