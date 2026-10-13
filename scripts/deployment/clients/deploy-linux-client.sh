#!/usr/bin/env bash
# Install Schnapp Linux tray client from a deployment kit (no source required).
# Run from the unpacked kit directory:
#   ./deploy-linux-client.sh
#   ./deploy-linux-client.sh -p ~/.local/opt/schnapp-client-linux
#   ./deploy-linux-client.sh --install-runtime
#   ./deploy-linux-client.sh --prefix /opt/schnapp-client-linux --system
# Env: PREFIX, INSTALL_RUNTIME (0|1), DOTNET_CHANNEL (10.0)
set -euo pipefail

KIT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_DIR="${KIT}/app"
META="${KIT}/kit.env"
BIN_NAME="schnapp-client-linux"

PREFIX="${PREFIX:-${HOME}/.local/opt/schnapp-client-linux}"
INSTALL_RUNTIME="${INSTALL_RUNTIME:-0}"
SYSTEM_INSTALL=0
LINK_BIN=1
DESKTOP_ENTRY=1
DOTNET_CHANNEL="${DOTNET_CHANNEL:-10.0}"
FORCE=0

usage() {
  sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//'
  cat <<'EOF'

Options:
  -p, --prefix DIR     Install directory (default: ~/.local/opt/schnapp-client-linux)
  --system             Use /opt/schnapp-client-linux (may need sudo for copy)
  --install-runtime    Install .NET runtime to ~/.dotnet if missing (framework-dependent kits)
  --no-link            Do not symlink binary into ~/.local/bin
  --no-desktop         Do not write a .desktop launcher
  -f, --force          Overwrite existing install directory
  -h, --help           Show this help
EOF
}

load_meta() {
  if [[ -f "$META" ]]; then
    # shellcheck disable=SC1090
    source "$META"
  fi
}

have_dotnet_runtime() {
  local d
  for d in \
    "${DOTNET_ROOT:-}" \
    "${HOME}/.dotnet" \
    /usr/share/dotnet \
    /usr/lib/dotnet
  do
    [[ -n "$d" && -x "${d}/dotnet" ]] || continue
    if "${d}/dotnet" --list-runtimes 2>/dev/null | grep -qE '^(Microsoft\.NETCore\.App|Microsoft\.AspNetCore\.App) 10\.'; then
      export DOTNET_ROOT="$d"
      export PATH="${d}:${PATH}"
      return 0
    fi
  done
  if command -v dotnet >/dev/null 2>&1; then
    if dotnet --list-runtimes 2>/dev/null | grep -qE '^(Microsoft\.NETCore\.App|Microsoft\.AspNetCore\.App) 10\.'; then
      return 0
    fi
  fi
  return 1
}

install_dotnet_runtime() {
  local install_dir="${HOME}/.dotnet"
  local script
  script="$(mktemp)"
  echo "Installing .NET ${DOTNET_CHANNEL} runtime → ${install_dir}"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$script"
  bash "$script" --channel "$DOTNET_CHANNEL" --runtime dotnet --install-dir "$install_dir"
  rm -f "$script"
  export DOTNET_ROOT="$install_dir"
  export PATH="${install_dir}:${PATH}"
  # shell profile hint
  local marker='# schnapp-client-linux dotnet'
  local line="export DOTNET_ROOT=\"${install_dir}\"; export PATH=\"\${DOTNET_ROOT}:\${PATH}\"  ${marker}"
  for rc in "${HOME}/.bashrc" "${HOME}/.profile"; do
    if [[ -f "$rc" ]] && ! grep -qF "$marker" "$rc" 2>/dev/null; then
      printf '\n%s\n' "$line" >>"$rc"
      echo "Appended PATH tip to $rc"
      break
    fi
  done
}

install_icons() {
  local share="${HOME}/.local/share"
  local icon_name="schnapp-client-linux"
  local svg="${KIT}/icons/${icon_name}.svg"
  local ico="${KIT}/icons/${icon_name}.ico"
  local hicolor="${share}/icons/hicolor"

  if [[ -f "$svg" ]]; then
    mkdir -p "${hicolor}/scalable/apps"
    cp "$svg" "${hicolor}/scalable/apps/${icon_name}.svg"
    echo "icon: ${hicolor}/scalable/apps/${icon_name}.svg"
  fi
  # Keep a copy next to the app for absolute Icon= fallbacks
  mkdir -p "${PREFIX}/icons"
  [[ -f "$svg" ]] && cp "$svg" "${PREFIX}/icons/${icon_name}.svg"
  [[ -f "$ico" ]] && cp "$ico" "${PREFIX}/icons/${icon_name}.ico"

  if command -v gtk-update-icon-cache >/dev/null 2>&1 && [[ -d "$hicolor" ]]; then
    gtk-update-icon-cache -f -t "$hicolor" 2>/dev/null || true
  fi
}

write_desktop() {
  local dest_dir="${HOME}/.local/share/applications"
  local desktop="${dest_dir}/schnapp-client-linux.desktop"
  local exec_path="${PREFIX}/${BIN_NAME}"
  local icon_name="schnapp-client-linux"
  local icon_path="${PREFIX}/icons/${icon_name}.svg"
  [[ -f "$icon_path" ]] || icon_path="${PREFIX}/icons/${icon_name}.ico"
  # Prefer themed name when installed under hicolor; else absolute path
  local icon_value="$icon_name"
  if [[ ! -f "${HOME}/.local/share/icons/hicolor/scalable/apps/${icon_name}.svg" && -f "$icon_path" ]]; then
    icon_value="$icon_path"
  elif [[ ! -f "${HOME}/.local/share/icons/hicolor/scalable/apps/${icon_name}.svg" ]]; then
    icon_value="$exec_path"
  fi
  mkdir -p "$dest_dir"
  cat >"$desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Schnapp
Comment=Schnapp Linux tray client
Exec=${exec_path}
Icon=${icon_value}
Terminal=false
Categories=Utility;
StartupNotify=false
EOF
  echo "desktop: $desktop"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help|help)
      usage
      exit 0
      ;;
    -p|--prefix)
      PREFIX="$2"
      shift 2
      ;;
    --system)
      SYSTEM_INSTALL=1
      PREFIX="${PREFIX:-/opt/schnapp-client-linux}"
      if [[ "$PREFIX" == "${HOME}/.local/opt/schnapp-client-linux" ]]; then
        PREFIX=/opt/schnapp-client-linux
      fi
      shift
      ;;
    --install-runtime)
      INSTALL_RUNTIME=1
      shift
      ;;
    --no-link)
      LINK_BIN=0
      shift
      ;;
    --no-desktop)
      DESKTOP_ENTRY=0
      shift
      ;;
    -f|--force)
      FORCE=1
      shift
      ;;
    *)
      echo "unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

load_meta

if [[ ! -d "$APP_DIR" ]]; then
  echo "missing app/ next to this script (not a deployment kit?)" >&2
  echo "kit expected at: $KIT" >&2
  exit 1
fi

if [[ ! -x "${APP_DIR}/${BIN_NAME}" && ! -f "${APP_DIR}/${BIN_NAME}.dll" ]]; then
  echo "app payload incomplete under ${APP_DIR}" >&2
  exit 1
fi

SELF_CONTAINED="${KIT_SELF_CONTAINED:-1}"

if [[ "$SELF_CONTAINED" != "1" ]]; then
  if ! have_dotnet_runtime; then
    if [[ "$INSTALL_RUNTIME" == "1" ]]; then
      install_dotnet_runtime
      have_dotnet_runtime || { echo "runtime install finished but .NET 10 still not found" >&2; exit 1; }
    else
      echo "This kit is framework-dependent and needs .NET ${DOTNET_CHANNEL} runtime." >&2
      echo "Re-run with --install-runtime, or install a package (e.g. aspnet-runtime / dotnet-runtime)." >&2
      exit 1
    fi
  fi
elif [[ "$INSTALL_RUNTIME" == "1" ]]; then
  echo "note: kit is self-contained; --install-runtime ignored"
fi

if [[ -e "$PREFIX" && "$FORCE" != "1" ]]; then
  if [[ -d "$PREFIX" ]]; then
    echo "install dir exists: $PREFIX (use --force to overwrite)"
  else
    echo "prefix exists and is not a directory: $PREFIX" >&2
    exit 1
  fi
fi

echo "kit: $KIT"
echo "from: $APP_DIR"
echo "to:   $PREFIX"
echo "self-contained: $SELF_CONTAINED"
echo

mkdir -p "$(dirname "$PREFIX")"
if [[ -d "$PREFIX" && "$FORCE" == "1" ]]; then
  rm -rf "$PREFIX"
fi
mkdir -p "$PREFIX"
# Preserve mode; copy tree
cp -a "${APP_DIR}/." "$PREFIX/"
chmod +x "${PREFIX}/${BIN_NAME}" 2>/dev/null || true
install_icons

if [[ "$LINK_BIN" == "1" ]]; then
  local_bin="${HOME}/.local/bin"
  mkdir -p "$local_bin"
  ln -sfn "${PREFIX}/${BIN_NAME}" "${local_bin}/${BIN_NAME}"
  echo "link: ${local_bin}/${BIN_NAME}"
  case ":${PATH}:" in
    *":${local_bin}:"*) ;;
    *) echo "tip: add ${local_bin} to PATH if '${BIN_NAME}' is not found" ;;
  esac
fi

if [[ "$DESKTOP_ENTRY" == "1" && "$SYSTEM_INSTALL" != "1" ]]; then
  write_desktop || true
fi

echo
echo "installed: ${PREFIX}/${BIN_NAME}"
echo "run: ${PREFIX}/${BIN_NAME}"
if [[ "$LINK_BIN" == "1" ]]; then
  echo "or:  ${BIN_NAME}"
fi
echo
echo "Config: ~/.config/schnapp/   Data: ~/.local/share/schnapp/"
echo "X11 session recommended for native capture; optional: ImageMagick import / scrot"
