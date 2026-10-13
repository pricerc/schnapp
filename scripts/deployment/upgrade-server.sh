#!/usr/bin/env bash
# Upgrade Schnapp server binaries on an existing install without touching
# configuration, database, or blob data.
#
# Run from the upgrade kit directory (contains api/, web/, this script):
#   sudo ./upgrade-server.sh
#   sudo ./upgrade-server.sh --prefix /opt/schnapp
#   sudo ./upgrade-server.sh --no-restart
#
# Env: SCHNAPP_PREFIX (default /opt/schnapp), SCHNAPP_USER / SCHNAPP_GROUP
set -euo pipefail

KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PREFIX="${SCHNAPP_PREFIX:-/opt/schnapp}"
APP_USER="${SCHNAPP_USER:-schnapp}"
APP_GROUP="${SCHNAPP_GROUP:-$APP_USER}"
RESTART=1
UPDATE_UNITS=0

usage() {
  sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'
  cat <<'EOF'

Options:
  -p, --prefix DIR     Install root (default: /opt/schnapp)
  -u, --user NAME      App user for chown (default: schnapp)
  -g, --group NAME     App group (default: same as user)
  --no-restart         Do not stop/start systemd units
  --update-units       Copy kit systemd/*.service into /etc/systemd/system
                       and daemon-reload (does not enable/disable units)
  -h, --help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    -p|--prefix) PREFIX="$2"; shift 2 ;;
    -u|--user) APP_USER="$2"; shift 2 ;;
    -g|--group) APP_GROUP="$2"; shift 2 ;;
    --no-restart) RESTART=0; shift ;;
    --update-units) UPDATE_UNITS=1; shift ;;
    *) echo "unknown option: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ ! -d "${KIT}/api" || ! -d "${KIT}/web" ]]; then
  echo "error: expected api/ and web/ next to this script (not an upgrade kit?)" >&2
  echo "  kit: $KIT" >&2
  exit 1
fi

if [[ ! -d "${PREFIX}/api" || ! -d "${PREFIX}/web" ]]; then
  echo "error: existing install not found under ${PREFIX} (api/ and web/ required)" >&2
  echo "  For a new install, use the publish kit (setup scripts + write-server-config)." >&2
  exit 1
fi

if [[ "$(id -u)" -ne 0 ]]; then
  echo "error: run as root (sudo) to write under ${PREFIX} and manage services" >&2
  exit 1
fi

echo "kit:    $KIT"
echo "prefix: $PREFIX"
echo "user:   ${APP_USER}:${APP_GROUP}"
echo

if [[ "$RESTART" == "1" ]] && command -v systemctl >/dev/null 2>&1; then
  echo "Stopping services (if present)…"
  systemctl stop schnapp-web 2>/dev/null || true
  systemctl stop schnapp-api 2>/dev/null || true
fi

# Replace binaries only. Never touch config/, data/, or *.env / Production appsettings
# outside the app trees. rsync --delete keeps removed deps from accumulating.
if command -v rsync >/dev/null 2>&1; then
  rsync -a --delete \
    --exclude 'appsettings.Production.json' \
    --exclude '*.env' \
    "${KIT}/api/" "${PREFIX}/api/"
  rsync -a --delete \
    --exclude 'appsettings.Production.json' \
    --exclude '*.env' \
    "${KIT}/web/" "${PREFIX}/web/"
  if [[ -d "${KIT}/utils" ]]; then
    mkdir -p "${PREFIX}/utils"
    rsync -a --delete "${KIT}/utils/" "${PREFIX}/utils/"
  fi
else
  # Portable fallback: copy tree, preserve Production config if present in target
  for part in api web; do
    for f in appsettings.Production.json; do
      if [[ -e "${PREFIX}/${part}/${f}" ]]; then
        cp -a "${PREFIX}/${part}/${f}" "/tmp/schnapp-upgrade-${part}-${f}.bak"
      fi
    done
    rm -rf "${PREFIX}/${part}"
    mkdir -p "${PREFIX}/${part}"
    cp -a "${KIT}/${part}/." "${PREFIX}/${part}/"
    if [[ -e "/tmp/schnapp-upgrade-${part}-appsettings.Production.json.bak" ]]; then
      mv "/tmp/schnapp-upgrade-${part}-appsettings.Production.json.bak" \
        "${PREFIX}/${part}/appsettings.Production.json"
    fi
  done
  if [[ -d "${KIT}/utils" ]]; then
    rm -rf "${PREFIX}/utils"
    mkdir -p "${PREFIX}/utils"
    cp -a "${KIT}/utils/." "${PREFIX}/utils/"
  fi
fi

if id "$APP_USER" >/dev/null 2>&1; then
  chown -R "${APP_USER}:${APP_GROUP}" "${PREFIX}/api" "${PREFIX}/web"
  [[ -d "${PREFIX}/utils" ]] && chown -R "${APP_USER}:${APP_GROUP}" "${PREFIX}/utils"
fi

# Ensure executables are runnable (self-contained builds).
chmod +x "${PREFIX}/api/Schnapp.Api" 2>/dev/null || true
chmod +x "${PREFIX}/web/Schnapp.Web" 2>/dev/null || true
chmod +x "${PREFIX}/utils/schnapp-utils" 2>/dev/null || true

if [[ "$UPDATE_UNITS" == "1" && -d "${KIT}/systemd" ]]; then
  echo "Updating systemd unit files…"
  cp "${KIT}/systemd/"*.service /etc/systemd/system/
  systemctl daemon-reload
fi

if [[ "$RESTART" == "1" ]] && command -v systemctl >/dev/null 2>&1; then
  echo "Starting services…"
  systemctl start schnapp-api
  systemctl start schnapp-web
  systemctl --no-pager --full status schnapp-api schnapp-web || true
fi

echo
echo "Upgrade complete."
echo "  Binaries:  ${PREFIX}/api  ${PREFIX}/web  ${PREFIX}/utils"
echo "  Untouched: ${PREFIX}/config  ${PREFIX}/data  (and database)"
echo
echo "If units were not restarted, run:"
echo "  sudo systemctl restart schnapp-api schnapp-web"
