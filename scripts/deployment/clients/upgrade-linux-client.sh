#!/usr/bin/env bash
# Upgrade Schnapp Linux tray client binaries on an existing install.
# Does not rewrite user config (~/.config/schnapp) or data (~/.local/share/schnapp).
#
# Run from the upgrade kit directory:
#   ./upgrade-linux-client.sh
#   ./upgrade-linux-client.sh -p ~/.local/opt/schnapp-client-linux
#   ./upgrade-linux-client.sh --system
set -euo pipefail

KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_DIR="${KIT}/app"
META="${KIT}/kit.env"
BIN_NAME="schnapp-client-linux"

PREFIX="${PREFIX:-${HOME}/.local/opt/schnapp-client-linux}"
SYSTEM_INSTALL=0
LINK_BIN=1
REFRESH_ICONS=0

usage() {
  sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//'
  cat <<'EOF'

Options:
  -p, --prefix DIR     Existing install directory
  --system             Use /opt/schnapp-client-linux
  --no-link            Do not refresh ~/.local/bin symlink
  --refresh-icons      Reinstall icons / desktop entry (optional)
  -h, --help
EOF
}

load_meta() {
  if [[ -f "$META" ]]; then
    # shellcheck disable=SC1090
    source "$META"
  fi
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help|help) usage; exit 0 ;;
    -p|--prefix) PREFIX="$2"; shift 2 ;;
    --system)
      SYSTEM_INSTALL=1
      if [[ "$PREFIX" == "${HOME}/.local/opt/schnapp-client-linux" ]]; then
        PREFIX=/opt/schnapp-client-linux
      fi
      shift
      ;;
    --no-link) LINK_BIN=0; shift ;;
    --refresh-icons) REFRESH_ICONS=1; shift ;;
    *) echo "unknown option: $1" >&2; usage; exit 1 ;;
  esac
done

load_meta
BIN_NAME="${KIT_BIN_NAME:-$BIN_NAME}"

if [[ ! -d "$APP_DIR" ]]; then
  echo "missing app/ next to this script (not an upgrade kit?)" >&2
  exit 1
fi

if [[ ! -d "$PREFIX" ]]; then
  echo "error: install directory does not exist: $PREFIX" >&2
  echo "  For a first-time install, use the publish kit (deploy-linux-client.sh)." >&2
  exit 1
fi

if [[ ! -x "${APP_DIR}/${BIN_NAME}" && ! -f "${APP_DIR}/${BIN_NAME}" && ! -f "${APP_DIR}/${BIN_NAME}.dll" ]]; then
  echo "app payload incomplete under ${APP_DIR}" >&2
  exit 1
fi

echo "kit:  $KIT"
echo "from: $APP_DIR"
echo "to:   $PREFIX (upgrade in place)"
echo

# Replace payload only; do not remove the install root (preserves any local extras).
cp -a "${APP_DIR}/." "$PREFIX/"
chmod +x "${PREFIX}/${BIN_NAME}" 2>/dev/null || true

if [[ "$LINK_BIN" == "1" ]]; then
  local_bin="${HOME}/.local/bin"
  mkdir -p "$local_bin"
  ln -sfn "${PREFIX}/${BIN_NAME}" "${local_bin}/${BIN_NAME}"
  echo "link: ${local_bin}/${BIN_NAME}"
fi

if [[ "$REFRESH_ICONS" == "1" ]]; then
  # Best-effort: re-copy icons from kit if present
  if [[ -d "${KIT}/icons" ]]; then
    mkdir -p "${PREFIX}/icons"
    cp -a "${KIT}/icons/." "${PREFIX}/icons/" 2>/dev/null || true
  fi
fi

echo
echo "upgraded: ${PREFIX}/${BIN_NAME}"
echo "Config untouched: ~/.config/schnapp/"
echo "Data untouched:   ~/.local/share/schnapp/"
