#!/usr/bin/env bash
# Build server deployment kits from staging into publish/ and upgrade/.
#
# Layout (default ARTIFACTS=<repo>/artifacts):
#   staging/server/              raw API+Web publish (via publish-server.sh)
#   publish/server/YYYYMMDD/     full install kit: binaries + setup + config + systemd
#   upgrade/server/YYYYMMDD/     upgrade kit: binaries + upgrade-server.sh (+ optional units)
#
# Usage (from repo root or from this directory):
#   ./scripts/deployment/build-server-deployment.sh
#   ./scripts/deployment/build-server-deployment.sh --self-contained
#   ./scripts/deployment/build-server-deployment.sh -o /tmp/out
#   ./scripts/deployment/build-server-deployment.sh --skip-publish   # reuse staging
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
cd "$ROOT"
PUBLISH_SH="${SCRIPT_DIR}/publish-server.sh"
UPGRADE_SH="${SCRIPT_DIR}/upgrade-server.sh"

ARTIFACTS="${ARTIFACTS:-${OUT_DIR:-$ROOT/artifacts}}"
SELF_CONTAINED="${SELF_CONTAINED:-0}"
CONFIG="${CONFIG:-Release}"
RID="${RID:-linux-x64}"
ZIP=0
SKIP_PUBLISH=0
# local calendar day for output folders (rebuild same day overwrites that day only).
STAMP="${STAMP:-$(date +%Y%m%d)}"

usage() {
  sed -n '2,13p' "$0" | sed 's/^# \{0,1\}//'
  cat <<'EOF'

Options:
  -o, --output DIR          Artifacts root (default: artifacts/)
  -c, --configuration CFG
  -r, --runtime RID         (self-contained only)
  --self-contained
  --framework-dependent     default
  --skip-publish            Do not run publish-server.sh; require existing staging
  --zip                     also write dated .zip archives under publish/ and upgrade/
  -h, --help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    -o|--output)
      [[ $# -ge 2 ]] || { echo "error: $1 needs a value" >&2; exit 1; }
      ARTIFACTS="$2"; shift 2 ;;
    -c|--configuration)
      [[ $# -ge 2 ]] || { echo "error: $1 needs a value" >&2; exit 1; }
      CONFIG="$2"; shift 2 ;;
    -r|--runtime)
      [[ $# -ge 2 ]] || { echo "error: $1 needs a value" >&2; exit 1; }
      RID="$2"; shift 2 ;;
    --self-contained) SELF_CONTAINED=1; shift ;;
    --framework-dependent) SELF_CONTAINED=0; shift ;;
    --skip-publish) SKIP_PUBLISH=1; shift ;;
    --zip) ZIP=1; shift ;;
    --no-zip) ZIP=0; shift ;; # retained for compatibility; zip is already off by default
    *) echo "unknown option: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ ! -f "$PUBLISH_SH" ]]; then
  echo "error: missing ${PUBLISH_SH}" >&2
  exit 1
fi
if [[ ! -f "$UPGRADE_SH" ]]; then
  echo "error: missing ${UPGRADE_SH}" >&2
  exit 1
fi
if [[ ! -x "$PUBLISH_SH" ]]; then
  chmod +x "$PUBLISH_SH" || true
fi
if [[ ! -x "$UPGRADE_SH" ]]; then
  chmod +x "$UPGRADE_SH" || true
fi

if [[ "${ARTIFACTS}" != /* ]]; then
  ARTIFACTS="${ROOT}/${ARTIFACTS}"
fi
mkdir -p "$ARTIFACTS"
ARTIFACTS="$(cd "$ARTIFACTS" && pwd)"

STAGING="${ARTIFACTS}/staging/server"
PUBLISH_DIR="${ARTIFACTS}/publish/server/${STAMP}"
UPGRADE_DIR="${ARTIFACTS}/upgrade/server/${STAMP}"

if [[ "$SKIP_PUBLISH" != "1" ]]; then
  set -- -o "$ARTIFACTS" -c "$CONFIG"
  if [[ "$SELF_CONTAINED" == "1" ]]; then
    set -- "$@" --self-contained -r "$RID"
  else
    set -- "$@" --framework-dependent
  fi
  echo "Running: ${PUBLISH_SH} $*"
  if ! bash "$PUBLISH_SH" "$@"; then
    echo "error: publish-server.sh failed (exit $?)" >&2
    exit 1
  fi
fi

if [[ ! -d "${STAGING}/api" || ! -d "${STAGING}/web" || ! -d "${STAGING}/utils" ]]; then
  echo "error: staging incomplete under ${STAGING}" >&2
  echo "  expected api/, web/ and utils/ (run without --skip-publish, or publish-server.sh first)" >&2
  exit 1
fi

stamp_tag() {
  local mode
  if [[ "$SELF_CONTAINED" == "1" ]]; then
    mode="sc-${RID}"
  else
    mode="fd"
  fi
  # Prefer staging MANIFEST self_contained if skip-publish
  if [[ -f "${STAGING}/MANIFEST.txt" ]]; then
    if grep -q 'self_contained: 1' "${STAGING}/MANIFEST.txt" 2>/dev/null; then
      local rid_line
      rid_line="$(grep '^rid:' "${STAGING}/MANIFEST.txt" 2>/dev/null || true)"
      if [[ -n "$rid_line" ]]; then
        mode="sc-${rid_line#rid:}"
        mode="$(echo "$mode" | tr -d '[:space:]')"
      else
        mode="sc"
      fi
    elif grep -q 'self_contained: 0' "${STAGING}/MANIFEST.txt" 2>/dev/null; then
      mode="fd"
    fi
  fi
  echo "schnapp-server-${mode}-${STAMP}"
}

KIT_BASENAME="$(stamp_tag)"

assemble_common_binaries() {
  local dest="$1"
  rm -rf "$dest"
  mkdir -p "${dest}/api" "${dest}/web" "${dest}/utils"
  cp -a "${STAGING}/api/." "${dest}/api/"
  cp -a "${STAGING}/web/." "${dest}/web/"
  cp -a "${STAGING}/utils/." "${dest}/utils/"
  if [[ -f "${STAGING}/MANIFEST.txt" ]]; then
    cp "${STAGING}/MANIFEST.txt" "${dest}/MANIFEST.txt"
  fi
}

echo "Assembling publish kit → ${PUBLISH_DIR}"
assemble_common_binaries "$PUBLISH_DIR"
mkdir -p "${PUBLISH_DIR}/host" "${PUBLISH_DIR}/systemd"

HOST_SRC="${SCRIPT_DIR}/host"
for f in setup-database.sh setup-database-firebird.sh setup-server-dirs.sh write-server-config.sh; do
  if [[ ! -f "${HOST_SRC}/${f}" ]]; then
    echo "error: missing host install script: ${HOST_SRC}/${f}" >&2
    exit 1
  fi
  cp "${HOST_SRC}/${f}" "${PUBLISH_DIR}/host/"
done
chmod +x "${PUBLISH_DIR}/host/"*.sh

cp "${SCRIPT_DIR}/systemd/schnapp-api.service" "${PUBLISH_DIR}/systemd/"
cp "${SCRIPT_DIR}/systemd/schnapp-web.service" "${PUBLISH_DIR}/systemd/"

if [[ -f "${ROOT}/docs/deployment-fedora.md" ]]; then
  cp "${ROOT}/docs/deployment-fedora.md" "${PUBLISH_DIR}/README-DEPLOY.md"
fi

cat > "${PUBLISH_DIR}/INSTALL.txt" <<'EOF'
Schnapp server — publish (full install) kit
===========================================

This folder is ready to copy to a new host. It includes binaries, host install
helpers (dirs, database, config), and systemd units.

1. On the server, install packages (see README-DEPLOY.md §1).
2. Copy this kit to the host (e.g. rsync the whole directory).
3. Run as root:
     ./host/setup-server-dirs.sh
     SCHNAPP_DB_PASSWORD='…' ./host/setup-database.sh
     # Firebird instead of PostgreSQL:
     #   SCHNAPP_DB_PASSWORD='…' ./host/setup-database-firebird.sh
     SCHNAPP_DB_PASSWORD='…' SCHNAPP_ADMIN_EMAIL='…' SCHNAPP_ADMIN_PASSWORD='…' \
       ./host/write-server-config.sh --link-into-apps --force \
       --api-bind 127.0.0.1 --api-port 5080 \
       --web-bind 127.0.0.1 --web-port 5081
     # Firebird: add --provider firebird (and use setup-database-firebird.sh above)
     # Optional: --web-bind 0.0.0.0 --web-port 8080
     # Optional: --api-urls 'http://127.0.0.1:5080;http://0.0.0.0:5080'
4. Install binaries under /opt/schnapp:
     rsync -a api/ /opt/schnapp/api/
     rsync -a web/ /opt/schnapp/web/
     rsync -a utils/ /opt/schnapp/utils/
     chown -R schnapp:schnapp /opt/schnapp
5. Install systemd units (documented in README-DEPLOY.md §6):
     cp systemd/*.service /etc/systemd/system/
     systemctl daemon-reload
     systemctl enable --now schnapp-api schnapp-web
6. Reverse proxy, firewall, clients: README-DEPLOY.md §7–8.

For an existing install, use the matching upgrade kit instead
(artifacts/upgrade/server/<YYYYMMDD>) so configuration is not overwritten.
EOF

echo "Assembling upgrade kit → ${UPGRADE_DIR}"
assemble_common_binaries "$UPGRADE_DIR"
mkdir -p "${UPGRADE_DIR}/systemd"
cp "$UPGRADE_SH" "${UPGRADE_DIR}/upgrade-server.sh"
chmod +x "${UPGRADE_DIR}/upgrade-server.sh"
cp "${SCRIPT_DIR}/systemd/schnapp-api.service" "${UPGRADE_DIR}/systemd/"
cp "${SCRIPT_DIR}/systemd/schnapp-web.service" "${UPGRADE_DIR}/systemd/"

cat > "${UPGRADE_DIR}/UPGRADE.txt" <<'EOF'
Schnapp server — upgrade kit
============================

Copy this folder to a host that already runs Schnapp. It updates API, Web and
Utils (schnapp-utils) binaries only. It does not write database roles, Production config, or blob data.

  sudo ./upgrade-server.sh
  sudo ./upgrade-server.sh --prefix /opt/schnapp
  sudo ./upgrade-server.sh --update-units   # also refresh systemd unit files
  sudo ./upgrade-server.sh --no-restart     # copy only; restart services yourself

Existing install required: {prefix}/api and {prefix}/web must already exist.
Config lives under {prefix}/config and is never modified by this kit.
EOF

maybe_zip() {
  local dir="$1"
  local kind="$2" # publish | upgrade
  if [[ "$ZIP" != "1" ]]; then
    return 0
  fi
  if ! command -v zip >/dev/null 2>&1; then
    echo "warning: zip not installed; skipping ${kind} archive" >&2
    return 0
  fi
  # Place archive beside the dated kit folder (publish/server/, upgrade/server/).
  local zip_path="$(dirname "$dir")/${KIT_BASENAME}-${kind}.zip"
  mkdir -p "$(dirname "$zip_path")"
  rm -f "$zip_path"
  (cd "$(dirname "$dir")" && zip -qr "$zip_path" "$(basename "$dir")")
  echo "${kind} zip: $zip_path"
}

maybe_zip "$PUBLISH_DIR" "publish"
maybe_zip "$UPGRADE_DIR" "upgrade"

echo
echo "staging: ${STAGING}"
echo "publish: ${PUBLISH_DIR}"
echo "upgrade: ${UPGRADE_DIR}"
