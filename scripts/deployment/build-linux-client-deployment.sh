#!/usr/bin/env bash
# Build Linux tray client kits: staging → publish + upgrade.
#
# Layout (default ARTIFACTS=<repo>/artifacts):
#   staging/linux-client/              raw dotnet publish + icons
#   publish/linux-client/YYYYMMDD/     full install kit (deploy + config notes)
#   upgrade/linux-client/YYYYMMDD/     binary upgrade kit (no first-time setup)
#
# Usage (from repo):
#   ./scripts/deployment/build-linux-client-deployment.sh
#   ./scripts/deployment/build-linux-client-deployment.sh --framework-dependent
#   ./scripts/deployment/build-linux-client-deployment.sh -o /tmp/kits
#   ./scripts/deployment/build-linux-client-deployment.sh --single-file
# Env: DOTNET, CONFIG (Release), RID (linux-x64), ARTIFACTS/OUT_DIR, SELF_CONTAINED (1|0), STAMP
#
# Kit scripts (copied into publish/upgrade): scripts/deployment/clients/
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
CLIENTS_DIR="${SCRIPT_DIR}/clients"
cd "$ROOT"

# Prefer .NET 10: /tmp/dotnet10 (dev install), then ~/.dotnet, then PATH
if [[ -x /tmp/dotnet10/dotnet ]]; then
  export DOTNET_ROOT=/tmp/dotnet10
  export PATH="/tmp/dotnet10:${PATH}"
elif [[ -x "${HOME}/.dotnet/dotnet" ]] && "${HOME}/.dotnet/dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
  export DOTNET_ROOT="${HOME}/.dotnet"
  export PATH="${DOTNET_ROOT}:${PATH}"
elif [[ -x /usr/share/dotnet/dotnet ]] && /usr/share/dotnet/dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  export DOTNET_ROOT=/usr/share/dotnet
  export PATH="/usr/share/dotnet:${PATH}"
fi

DOTNET="${DOTNET:-dotnet}"
PROJECT="${PROJECT:-src/clients/Schnapp.Client.Linux/Schnapp.Client.Linux.csproj}"
CONFIG="${CONFIG:-Release}"
RID="${RID:-linux-x64}"
ARTIFACTS="${ARTIFACTS:-${OUT_DIR:-$ROOT/artifacts}}"
SELF_CONTAINED="${SELF_CONTAINED:-1}"
SINGLE_FILE="${SINGLE_FILE:-0}"
BIN_NAME="schnapp-client-linux"
ZIP=0

usage() {
  sed -n '2,14p' "$0" | sed 's/^# \{0,1\}//'
  cat <<'EOF'

Options:
  -c, --configuration CFG   Build config (default: Release)
  -r, --runtime RID         RID (default: linux-x64)
  -o, --output DIR          Artifacts root (default: artifacts/)
  --self-contained          Bundle .NET runtime (default)
  --framework-dependent     Smaller kit; recipient needs runtime or --install-runtime
  --single-file             Single-file self-contained publish
  --project PATH            csproj path
  --zip                     also write dated .zip archives under publish/ and upgrade/
  -h, --help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help|help)
      usage
      exit 0
      ;;
    -c|--configuration)
      CONFIG="$2"
      shift 2
      ;;
    -r|--runtime)
      RID="$2"
      shift 2
      ;;
    -o|--output)
      ARTIFACTS="$2"
      shift 2
      ;;
    --self-contained)
      SELF_CONTAINED=1
      shift
      ;;
    --framework-dependent)
      SELF_CONTAINED=0
      shift
      ;;
    --single-file)
      SINGLE_FILE=1
      SELF_CONTAINED=1
      shift
      ;;
    --project)
      PROJECT="$2"
      shift 2
      ;;
    --zip)
      ZIP=1
      shift
      ;;
    --no-zip)
      ZIP=0
      shift
      ;; # retained for compatibility; zip is already off by default
    *)
      echo "unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ ! -f "$PROJECT" ]]; then
  echo "project not found: $PROJECT" >&2
  exit 1
fi

for f in deploy-linux-client.sh upgrade-linux-client.sh; do
  if [[ ! -f "${CLIENTS_DIR}/${f}" ]]; then
    echo "missing: ${CLIENTS_DIR}/${f}" >&2
    exit 1
  fi
done

if [[ "${ARTIFACTS}" != /* ]]; then
  ARTIFACTS="${ROOT}/${ARTIFACTS}"
fi
mkdir -p "$ARTIFACTS"
ARTIFACTS="$(cd "$ARTIFACTS" && pwd)"

# UTC calendar day for output folders (rebuild same day overwrites that day only).
STAMP="${STAMP:-$(date -u +%Y%m%d)}"
STAGING="${ARTIFACTS}/staging/linux-client"
PUBLISH_DIR="${ARTIFACTS}/publish/linux-client/${STAMP}"
UPGRADE_DIR="${ARTIFACTS}/upgrade/linux-client/${STAMP}"
APP_STAGE="${STAGING}/app"

MODE_TAG="sc"
[[ "$SELF_CONTAINED" == "1" ]] || MODE_TAG="fd"
[[ "$SINGLE_FILE" == "1" ]] && MODE_TAG="sf"
KIT_BASENAME="schnapp-client-linux-${RID}-${MODE_TAG}-${STAMP}"

rm -rf "$STAGING"
mkdir -p "$APP_STAGE"

ARGS=(
  publish "$PROJECT"
  -c "$CONFIG"
  -r "$RID"
  -o "$APP_STAGE"
  --nologo
)

if [[ "$SELF_CONTAINED" == "1" ]]; then
  ARGS+=(--self-contained true)
else
  ARGS+=(--self-contained false)
fi

if [[ "$SINGLE_FILE" == "1" ]]; then
  ARGS+=(-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true)
fi

echo "dotnet: $($DOTNET --version 2>/dev/null || echo '?')"
echo "project: $PROJECT"
echo "config: $CONFIG"
echo "rid: $RID"
echo "self-contained: $SELF_CONTAINED"
echo "single-file: $SINGLE_FILE"
echo "staging: $STAGING"
echo

"$DOTNET" "${ARGS[@]}"

if [[ ! -x "${APP_STAGE}/${BIN_NAME}" && ! -f "${APP_STAGE}/${BIN_NAME}" ]]; then
  echo "publish did not produce ${BIN_NAME} under ${APP_STAGE}" >&2
  ls -la "$APP_STAGE" | head -30 >&2
  exit 1
fi
chmod +x "${APP_STAGE}/${BIN_NAME}" 2>/dev/null || true

find "$APP_STAGE" -name '*.pdb' -delete 2>/dev/null || true

ICON_SRC="${ROOT}/src/clients/Schnapp.Client.Linux/Assets"
mkdir -p "${STAGING}/icons"
if [[ -f "${ICON_SRC}/app-icon.svg" ]]; then
  cp "${ICON_SRC}/app-icon.svg" "${STAGING}/icons/schnapp-client-linux.svg"
fi
if [[ -f "${ICON_SRC}/app-icon.ico" ]]; then
  cp "${ICON_SRC}/app-icon.ico" "${STAGING}/icons/schnapp-client-linux.ico"
fi

write_kit_env() {
  local dest="$1"
  {
    echo "# Generated by build-linux-client-deployment.sh — do not edit by hand"
    echo "KIT_SELF_CONTAINED=${SELF_CONTAINED}"
    echo "KIT_SINGLE_FILE=${SINGLE_FILE}"
    echo "KIT_RID=${RID}"
    echo "KIT_CONFIG=${CONFIG}"
    echo "KIT_BIN_NAME=${BIN_NAME}"
    echo "KIT_TFM=net10.0"
    echo "KIT_BUILT_UTC=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  } >"${dest}/kit.env"
}

# --- publish kit (full install) ---
echo "Assembling publish kit → ${PUBLISH_DIR}"
rm -rf "$PUBLISH_DIR"
mkdir -p "${PUBLISH_DIR}/app" "${PUBLISH_DIR}/icons"
cp -a "${APP_STAGE}/." "${PUBLISH_DIR}/app/"
if [[ -d "${STAGING}/icons" ]]; then
  cp -a "${STAGING}/icons/." "${PUBLISH_DIR}/icons/" 2>/dev/null || true
fi
cp "${CLIENTS_DIR}/deploy-linux-client.sh" "${PUBLISH_DIR}/deploy-linux-client.sh"
chmod +x "${PUBLISH_DIR}/deploy-linux-client.sh"
write_kit_env "$PUBLISH_DIR"

cat >"${PUBLISH_DIR}/README.txt" <<EOF
Schnapp Linux client — publish (install) kit
============================================

Contents:
  deploy-linux-client.sh   install onto this machine
  app/                     published binaries (no source)
  icons/                   app icon for desktop entry
  kit.env                  build metadata
  README.txt               this file

Install (user-local, default):
  chmod +x deploy-linux-client.sh
  ./deploy-linux-client.sh

Options:
  ./deploy-linux-client.sh -p ~/apps/schnapp
  ./deploy-linux-client.sh --force
  ./deploy-linux-client.sh --system          # /opt/... (may need sudo for write)
  ./deploy-linux-client.sh --install-runtime # framework-dependent kits only

Self-contained: ${SELF_CONTAINED}   RID: ${RID}   Built (UTC): $(date -u +%Y-%m-%dT%H:%M:%SZ)

If self-contained is 0, install .NET 10 runtime first, or pass
--install-runtime (downloads into ~/.dotnet via Microsoft install script).

After install:
  schnapp-client-linux
  # or the path printed by the deploy script

Shared config/data (same as CLI):
  ~/.config/schnapp/
  ~/.local/share/schnapp/

Needs an X11 session for native screen capture. Optional fallbacks on PATH:
  import (ImageMagick), scrot, gnome-screenshot

For an existing install, use the upgrade kit (artifacts/upgrade/linux-client/<YYYYMMDD>).
EOF

# --- upgrade kit ---
echo "Assembling upgrade kit → ${UPGRADE_DIR}"
rm -rf "$UPGRADE_DIR"
mkdir -p "${UPGRADE_DIR}/app" "${UPGRADE_DIR}/icons"
cp -a "${APP_STAGE}/." "${UPGRADE_DIR}/app/"
if [[ -d "${STAGING}/icons" ]]; then
  cp -a "${STAGING}/icons/." "${UPGRADE_DIR}/icons/" 2>/dev/null || true
fi
cp "${CLIENTS_DIR}/upgrade-linux-client.sh" "${UPGRADE_DIR}/upgrade-linux-client.sh"
chmod +x "${UPGRADE_DIR}/upgrade-linux-client.sh"
write_kit_env "$UPGRADE_DIR"

cat >"${UPGRADE_DIR}/UPGRADE.txt" <<EOF
Schnapp Linux client — upgrade kit
==================================

Copy this folder onto a machine that already has the client installed.
Updates binaries only; does not change ~/.config/schnapp or data dirs.

  chmod +x upgrade-linux-client.sh
  ./upgrade-linux-client.sh
  ./upgrade-linux-client.sh -p ~/.local/opt/schnapp-client-linux
  ./upgrade-linux-client.sh --system

Self-contained: ${SELF_CONTAINED}   RID: ${RID}   Built (UTC): $(date -u +%Y-%m-%dT%H:%M:%SZ)
EOF

maybe_zip() {
  local dir="$1"
  local kind="$2"
  if [[ "$ZIP" != "1" ]]; then
    return 0
  fi
  # Place archive beside the dated kit folder (publish/linux-client/, upgrade/linux-client/).
  local zip_path="$(dirname "$dir")/${KIT_BASENAME}-${kind}.zip"
  mkdir -p "$(dirname "$zip_path")"
  rm -f "$zip_path"
  if command -v zip >/dev/null 2>&1; then
    (cd "$(dirname "$dir")" && zip -qr "$zip_path" "$(basename "$dir")")
  else
    tar -czf "${zip_path%.zip}.tar.gz" -C "$(dirname "$dir")" "$(basename "$dir")"
    zip_path="${zip_path%.zip}.tar.gz"
  fi
  echo "${kind} kit: $zip_path"
  ls -lh "$zip_path"
}

maybe_zip "$PUBLISH_DIR" "publish"
maybe_zip "$UPGRADE_DIR" "upgrade"

echo
echo "staging: ${STAGING}"
echo "publish: ${PUBLISH_DIR}"
echo "upgrade: ${UPGRADE_DIR}"
echo
echo "First-time install: unpack publish kit and run ./deploy-linux-client.sh"
echo "Existing install:   unpack upgrade kit and run ./upgrade-linux-client.sh"
