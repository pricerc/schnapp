#!/usr/bin/env bash
# Build Schnapp Alpine appliance disk images for QNAP (create VM + attach disks).
#
# Outputs (default under /opt/schnapp/appliance/ — not the worktree; /home is tight):
#   /opt/schnapp/appliance/YYYYMMDD/
#     schnapp-system.img     — qcow2 content, .img name (QNAP VS quirk)
#     schnapp-data.img       — qcow2 Postgres data volume (LABEL=schnapp-data)
#     MANIFEST.txt           — versions and knobs used for this build
#     README-IMPORT.txt      — short QNAP attach notes
#   /opt/schnapp/appliance/.build/   — intermediate dotnet publish (large; not in git)
#
# Disk format note:
#   QNAP Virtualization Station often wants the *filename* .img but boots qcow2
#   content. We create qemu format=qcow2 and name the files *.img.
#
# Requirements (build host, as root for disk image steps):
#   qemu-img, qemu-nbd, rsync, sfdisk, mkfs.ext4, wget/curl, dotnet SDK 10
#   KVM optional (not required for alpine-make-vm-image)
#   writable /opt/schnapp/appliance (created if missing)
#
# Usage (from repo root):
#   sudo ./scripts/appliance/build-appliance.sh
#   sudo ./scripts/appliance/build-appliance.sh --defaults ./scripts/appliance/defaults.env
#   sudo ./scripts/appliance/build-appliance.sh --skip-publish   # reuse .build/publish
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
cd "$ROOT"

STAMP="${STAMP:-$(date +%Y%m%d)}"
# Final disks + manifest (keep off /home) — resolved after CLI + defaults.env
APPLIANCE_ROOT="${APPLIANCE_ROOT:-/opt/schnapp/appliance}"
ARTIFACTS=""
BUILD_DIR=""
PUBLISH_DIR=""
# qcow2 data, .img extension (QNAP): override with DISK_FORMAT=raw if you truly need raw
DISK_FORMAT="${DISK_FORMAT:-qcow2}"
DISK_EXT="${DISK_EXT:-img}"
# Small vendored script can stay in-tree; override with VENDOR_DIR if desired
VENDOR_DIR="${VENDOR_DIR:-$SCRIPT_DIR/vendor}"
AMVI="${VENDOR_DIR}/alpine-make-vm-image"
AMVI_VERSION="${AMVI_VERSION:-v0.13.3}"
AMVI_URL="https://raw.githubusercontent.com/alpinelinux/alpine-make-vm-image/${AMVI_VERSION}/alpine-make-vm-image"

DEFAULTS_FILE="${SCRIPT_DIR}/defaults.env"
SKIP_PUBLISH=0
SKIP_CLIENTS=0
SKIP_SYSTEM=0
SKIP_DATA=0
CONFIG="${CONFIG:-Release}"
CLI_ARTIFACTS=""
CLI_APPLIANCE_ROOT=""

# Defaults (overridden by defaults.env)
SYSTEM_DISK_SIZE=8G
DATA_DISK_SIZE=8G
ALPINE_BRANCH=latest-stable
DOTNET_RID=linux-musl-x64
# Tray clients run on workstations (glibc Linux / Windows), not on Alpine musl
CLIENT_LINUX_RID=linux-x64
# Optional: path to a pre-built Windows publish kit (from build-windows-client-deployment.ps1)
WINDOWS_CLIENT_KIT="${WINDOWS_CLIENT_KIT:-}"
HOSTNAME=schnapp
ROOT_PASSWORD=schnapp
BLOB_NFS=
BLOB_MOUNTPOINT=/opt/schnapp/data/blobs
DATA_MOUNTPOINT=/opt/schnapp/data
DATA_DISK_LABEL=schnapp-data
PGDATA=/opt/schnapp/data/postgresql
DB_NAME=schnapp
DB_USER=schnapp
DB_PASSWORD=schnapp
API_PORT=5080
WEB_PORT=80
HTTPS_PORT=443
HTTPS_ENABLE=1
API_BIND=0.0.0.0
WEB_BIND=0.0.0.0
ADMIN_EMAIL=admin@example.com
ADMIN_PASSWORD=change-me
BACKUP_DIR=/opt/schnapp/data/blobs/_appliance/db
SMB_SHARE_NAME=clients

usage() {
  sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'
  cat <<'EOF'

Options:
  --defaults FILE       env file (default: scripts/appliance/defaults.env if present)
  --artifacts DIR       dated output dir (default: /opt/schnapp/appliance/YYYYMMDD)
  --appliance-root DIR  parent for dated builds + .build (default: /opt/schnapp/appliance)
  --skip-publish        reuse existing API/Web publish under .build/publish
  --skip-clients        do not build/copy tray client kits into the image
  --windows-client DIR  pre-built Windows client kit to include (SMB share)
  --skip-system         do not rebuild system disk
  --skip-data           do not rebuild data disk
  --system-size SIZE    e.g. 8G
  --data-size SIZE      e.g. 8G
  -h, --help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    --defaults) DEFAULTS_FILE="$2"; shift 2 ;;
    --artifacts) CLI_ARTIFACTS="$2"; shift 2 ;;
    --appliance-root) CLI_APPLIANCE_ROOT="$2"; shift 2 ;;
    --skip-publish) SKIP_PUBLISH=1; shift ;;
    --skip-clients) SKIP_CLIENTS=1; shift ;;
    --windows-client) WINDOWS_CLIENT_KIT="$2"; shift 2 ;;
    --skip-system) SKIP_SYSTEM=1; shift ;;
    --skip-data) SKIP_DATA=1; shift ;;
    --system-size) SYSTEM_DISK_SIZE="$2"; shift 2 ;;
    --data-size) DATA_DISK_SIZE="$2"; shift 2 ;;
    *) echo "unknown option: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ -f "$DEFAULTS_FILE" ]]; then
  echo "loading $DEFAULTS_FILE"
  # shellcheck disable=SC1090
  set -a
  # shellcheck disable=SC1091
  source "$DEFAULTS_FILE"
  set +a
elif [[ -f "${SCRIPT_DIR}/defaults.env.example" && ! -f "${SCRIPT_DIR}/defaults.env" ]]; then
  echo "note: no defaults.env — using script defaults (copy defaults.env.example to customize)"
fi

# CLI wins over defaults.env; defaults.env wins over built-in paths
APPLIANCE_ROOT="${CLI_APPLIANCE_ROOT:-${APPLIANCE_ROOT:-/opt/schnapp/appliance}}"
ARTIFACTS="${CLI_ARTIFACTS:-${ARTIFACTS:-$APPLIANCE_ROOT/$STAMP}}"
BUILD_DIR="${BUILD_DIR:-$APPLIANCE_ROOT/.build}"
PUBLISH_DIR="${PUBLISH_DIR:-$BUILD_DIR/publish}"
echo "appliance images → ${ARTIFACTS}"
echo "intermediate build → ${BUILD_DIR}"

if [[ "$(id -u)" -ne 0 ]]; then
  echo "error: run as root (alpine-make-vm-image needs qemu-nbd / mount)" >&2
  exit 1
fi

# dotnet publish runs as the invoking user (SUDO_USER) so NuGet caches and
# worktree bin/ stay owned correctly. Image assembly stays root for nbd/mount.
BUILD_USER="${SUDO_USER:-}"
BUILD_GROUP=""
if [[ -n "$BUILD_USER" && "$BUILD_USER" != root ]]; then
  BUILD_GROUP="$(id -gn "$BUILD_USER" 2>/dev/null || echo "$BUILD_USER")"
fi

run_as_build_user() {
  if [[ -n "$BUILD_USER" && "$BUILD_USER" != root ]]; then
    # -H: correct HOME for NuGet; preserve PATH so /usr/share/dotnet is found
    sudo -u "$BUILD_USER" -H -- "$@"
  else
    "$@"
  fi
}

# mkdir as root then hand the tree to BUILD_USER before any non-root write.
own_for_build_user() {
  local path
  for path in "$@"; do
    mkdir -p "$path"
    if [[ -n "$BUILD_USER" && "$BUILD_USER" != root ]]; then
      chown -R "${BUILD_USER}:${BUILD_GROUP}" "$path"
    fi
  done
}

need_cmd() {
  command -v "$1" >/dev/null 2>&1 || { echo "error: missing command: $1" >&2; exit 1; }
}

# Download a URL with curl (preferred) or wget; retries for flaky GitLab/mirrors.
fetch_url() {
  local url="$1" dest="$2"
  local i
  for i in 1 2 3 4 5; do
    if command -v curl >/dev/null 2>&1; then
      if curl -fsSL --connect-timeout 20 --max-time 120 --retry 3 --retry-delay 2 \
          -o "$dest" "$url"; then
        return 0
      fi
    elif command -v wget >/dev/null 2>&1; then
      if wget -T 60 -t 3 -q -O "$dest" "$url"; then
        return 0
      fi
    else
      echo "error: need curl or wget" >&2
      return 1
    fi
    echo "fetch retry $i failed: $url" >&2
    sleep $((i * 2))
  done
  return 1
}

# alpine-make-vm-image needs `apk` on the host. Non-Alpine hosts get static
# apk.static. Prefer Alpine CDN (apk-tools-static .apk) — GitLab package API
# often times out.
ensure_host_apk() {
  if command -v apk >/dev/null 2>&1; then
    export APK
    APK="$(command -v apk)"
    echo "using host apk: $APK"
    return 0
  fi

  local arch
  arch="$(uname -m)"
  case "$arch" in
    x86_64|amd64) arch=x86_64 ;;
    aarch64|arm64) arch=aarch64 ;;
  esac

  local apk_static="${VENDOR_DIR}/apk.static-${arch}"
  mkdir -p "$VENDOR_DIR"

  if [[ -x "$apk_static" ]] && "$apk_static" --version >/dev/null 2>&1; then
    export APK="$apk_static"
    echo "using cached static apk: $APK ($("$apk_static" --version 2>/dev/null | head -1))"
    return 0
  fi

  # Prefer extracting sbin/apk.static from apk-tools-static on dl-cdn (reliable).
  # Branch can track image branch (v3.21 packages work fine for host-side apk).
  local branch="${APK_TOOLS_ALPINE_BRANCH:-v3.21}"
  local mirror="${ALPINE_MIRROR:-https://dl-cdn.alpinelinux.org/alpine}"
  local index_url="${mirror}/${branch}/main/${arch}/"
  echo "resolving apk-tools-static from ${index_url}"

  local pkg_name=""
  if command -v curl >/dev/null 2>&1; then
    pkg_name=$(curl -fsSL --connect-timeout 15 --max-time 60 "$index_url" \
      | grep -oE 'apk-tools-static-[0-9][^"<> ]+\.apk' | sort -u | tail -1 || true)
  fi
  if [[ -z "$pkg_name" ]] && command -v wget >/dev/null 2>&1; then
    pkg_name=$(wget -T 30 -q -O - "$index_url" \
      | grep -oE 'apk-tools-static-[0-9][^"<> ]+\.apk' | sort -u | tail -1 || true)
  fi
  if [[ -z "$pkg_name" ]]; then
    # Known good fallback names (CDN still serves older builds)
    pkg_name="apk-tools-static-2.14.6-r3.apk"
  fi

  local pkg_url="${mirror}/${branch}/main/${arch}/${pkg_name}"
  local pkg_tmp
  pkg_tmp=$(mktemp /tmp/apk-tools-static.XXXXXX.apk)
  echo "downloading ${pkg_url}"
  if ! fetch_url "$pkg_url" "$pkg_tmp"; then
    rm -f "$pkg_tmp"
    # Last resort: GitLab generic package (often blocked / slow)
    local ver="${APK_TOOLS_VERSION:-v2.14.9}"
    local gl="${APK_TOOLS_URI:-https://gitlab.alpinelinux.org/api/v4/projects/5/packages/generic/${ver}/${arch}/apk.static}"
    echo "CDN failed; trying GitLab: $gl"
    if ! fetch_url "$gl" "$apk_static"; then
      echo "error: could not obtain apk.static" >&2
      echo "  Manual fix:" >&2
      echo "    curl -fL -o $apk_static \\" >&2
      echo "      '${mirror}/${branch}/main/${arch}/apk-tools-static-*.apk'" >&2
      echo "    tar -xzf apk-tools-static-*.apk sbin/apk.static" >&2
      echo "    mv sbin/apk.static $apk_static && chmod +x $apk_static" >&2
      exit 1
    fi
    chmod +x "$apk_static"
    export APK="$apk_static"
    echo "using static apk: $APK"
    return 0
  fi

  local extract_dir
  extract_dir=$(mktemp -d /tmp/apk-static-extract.XXXXXX)
  if ! tar -xzf "$pkg_tmp" -C "$extract_dir" sbin/apk.static 2>/dev/null; then
    tar -xzf "$pkg_tmp" -C "$extract_dir" 2>/dev/null || true
  fi
  rm -f "$pkg_tmp"
  if [[ ! -f "$extract_dir/sbin/apk.static" ]]; then
    echo "error: apk.static not found inside ${pkg_name}" >&2
    rm -rf "$extract_dir"
    exit 1
  fi
  cp -a "$extract_dir/sbin/apk.static" "$apk_static"
  rm -rf "$extract_dir"
  chmod +x "$apk_static"
  if ! "$apk_static" --version >/dev/null 2>&1; then
    echo "error: downloaded apk.static does not run on this host" >&2
    rm -f "$apk_static"
    exit 1
  fi
  export APK="$apk_static"
  echo "using static apk: $APK ($("$apk_static" --version 2>/dev/null | head -1))"
}

need_cmd qemu-img
need_cmd qemu-nbd
need_cmd rsync
need_cmd mkfs.ext4
need_cmd sha256sum
# curl preferred for Alpine package fetch (wget often hangs on GitLab)
if ! command -v curl >/dev/null 2>&1 && ! command -v wget >/dev/null 2>&1; then
  echo "error: need curl or wget" >&2
  exit 1
fi

mkdir -p "$ARTIFACTS" "$BUILD_DIR" "$PUBLISH_DIR" "$VENDOR_DIR" "$APPLIANCE_ROOT"
# Entire intermediate tree must be writable by BUILD_USER (re-runs may leave root-owned files)
own_for_build_user "$BUILD_DIR" "$PUBLISH_DIR"

# --- alpine-make-vm-image ---
if [[ ! -x "$AMVI" ]]; then
  echo "fetching alpine-make-vm-image ${AMVI_VERSION}"
  if ! fetch_url "$AMVI_URL" "$AMVI"; then
    echo "error: failed to download alpine-make-vm-image from $AMVI_URL" >&2
    exit 1
  fi
  chmod +x "$AMVI"
fi

ensure_host_apk

# --- publish self-contained musl ---
if [[ "$SKIP_PUBLISH" -eq 0 ]]; then
  need_cmd dotnet || need_cmd /usr/share/dotnet/dotnet || true
  DOTNET_BIN="$(command -v dotnet || true)"
  if [[ -z "$DOTNET_BIN" && -x /usr/share/dotnet/dotnet ]]; then
    DOTNET_BIN=/usr/share/dotnet/dotnet
  fi
  [[ -n "$DOTNET_BIN" ]] || { echo "error: dotnet SDK not found" >&2; exit 1; }

  echo "publishing API+Web self-contained (${DOTNET_RID}, ${CONFIG}) as ${BUILD_USER:-root}"
  rm -rf "$PUBLISH_DIR/api" "$PUBLISH_DIR/web"
  own_for_build_user "$PUBLISH_DIR/api" "$PUBLISH_DIR/web"

  pub() {
    local project="$1" out="$2"
    run_as_build_user "$DOTNET_BIN" publish "$project" \
      -c "$CONFIG" \
      -r "$DOTNET_RID" \
      --self-contained true \
      -p:PublishSingleFile=false \
      -o "$out"
  }
  pub "$ROOT/src/servers/Schnapp.Api/Schnapp.Api.csproj" "$PUBLISH_DIR/api"
  pub "$ROOT/src/servers/Schnapp.Web/Schnapp.Web.csproj" "$PUBLISH_DIR/web"
else
  [[ -d "$PUBLISH_DIR/api" && -d "$PUBLISH_DIR/web" ]] \
    || { echo "error: --skip-publish but $PUBLISH_DIR incomplete" >&2; exit 1; }
fi

# --- tray client kits (served from the VM via SMB share "clients") ---
CLIENTS_STAGE="${BUILD_DIR}/clients"
CLIENT_ART="${BUILD_DIR}/client-artifacts"
own_for_build_user "$CLIENTS_STAGE" "$CLIENT_ART"
if [[ "$SKIP_CLIENTS" -eq 0 ]]; then
  echo "building Linux tray client kit (${CLIENT_LINUX_RID}) for SMB share"
  # Prefer self-contained glibc linux-x64 for desktop workstations (not musl)
  run_as_build_user env \
    ARTIFACTS="$CLIENT_ART" \
    CONFIG="$CONFIG" \
    RID="$CLIENT_LINUX_RID" \
    SELF_CONTAINED=1 \
    STAMP="$STAMP" \
    "$ROOT/scripts/deployment/build-linux-client-deployment.sh"

  LINUX_KIT=""
  if [[ -d "$CLIENT_ART/publish/linux-client/$STAMP" ]]; then
    LINUX_KIT="$CLIENT_ART/publish/linux-client/$STAMP"
  else
    # fallback: newest dated folder
    LINUX_KIT=$(find "$CLIENT_ART/publish/linux-client" -mindepth 1 -maxdepth 1 -type d 2>/dev/null | sort | tail -1 || true)
  fi
  [[ -n "$LINUX_KIT" && -d "$LINUX_KIT" ]] \
    || { echo "error: Linux client kit not found under $CLIENT_ART/publish/linux-client" >&2; exit 1; }

  rm -rf "$CLIENTS_STAGE/linux"
  mkdir -p "$CLIENTS_STAGE/linux"
  rsync -a "$LINUX_KIT/" "$CLIENTS_STAGE/linux/"

  # Windows kit cannot be built on Linux; include if provided
  if [[ -n "$WINDOWS_CLIENT_KIT" ]]; then
    [[ -d "$WINDOWS_CLIENT_KIT" ]] \
      || { echo "error: --windows-client not a directory: $WINDOWS_CLIENT_KIT" >&2; exit 1; }
    echo "including Windows client kit from $WINDOWS_CLIENT_KIT"
    rm -rf "$CLIENTS_STAGE/windows"
    mkdir -p "$CLIENTS_STAGE/windows"
    rsync -a "$WINDOWS_CLIENT_KIT/" "$CLIENTS_STAGE/windows/"
  elif [[ -d "$ROOT/artifacts/publish/windows-client" ]]; then
    WIN_KIT=$(find "$ROOT/artifacts/publish/windows-client" -mindepth 1 -maxdepth 1 -type d 2>/dev/null | sort | tail -1 || true)
    if [[ -n "$WIN_KIT" ]]; then
      echo "including Windows client kit from $WIN_KIT"
      rm -rf "$CLIENTS_STAGE/windows"
      mkdir -p "$CLIENTS_STAGE/windows"
      rsync -a "$WIN_KIT/" "$CLIENTS_STAGE/windows/"
    else
      echo "note: no Windows client kit found — share will have linux/ only"
      echo "      build on Windows and pass --windows-client DIR or set WINDOWS_CLIENT_KIT"
    fi
  else
    echo "note: no Windows client kit — pass --windows-client DIR to include one"
  fi

  {
    echo "Schnapp clients staged for appliance SMB share"
    echo "stamp=$STAMP"
    echo "linux_rid=$CLIENT_LINUX_RID"
    echo "linux_kit=$LINUX_KIT"
    if [[ -d "$CLIENTS_STAGE/windows" ]]; then
      echo "windows=yes"
    else
      echo "windows=no"
    fi
  } >"$CLIENTS_STAGE/CLIENTS.txt"
  # Keep README from overlay; stage overwrites CLIENTS.txt only
else
  echo "skipping client kits (--skip-clients)"
  echo "clients skipped at build" >"$CLIENTS_STAGE/CLIENTS.txt"
fi

# alpine-make-vm-image --script-chroot binds the *directory containing the script* at /mnt.
# Put a staging copy of provision + publish under BUILD_DIR (not the worktree) so large
# self-contained binaries never land on /home.
PROVISION_SRC="${SCRIPT_DIR}/provision"
PROVISION_DIR="${BUILD_DIR}/provision-stage"
rm -rf "$PROVISION_DIR"
mkdir -p "$PROVISION_DIR"
cp -a "${PROVISION_SRC}/chroot-setup.sh" "${PROVISION_DIR}/chroot-setup.sh"
chmod +x "${PROVISION_DIR}/chroot-setup.sh"

# Static SSH material (survive system image rebuild when present on build host)
SSH_KEYS_DIR="${SCHNAPP_SSH_KEYS_DIR:-$SCRIPT_DIR/ssh}"
mkdir -p "${PROVISION_DIR}/ssh" "${PROVISION_DIR}/ssh/hostkeys"
if [[ -f "${SSH_KEYS_DIR}/authorized_keys.root" ]]; then
  cp -a "${SSH_KEYS_DIR}/authorized_keys.root" "${PROVISION_DIR}/ssh/"
  echo "SSH authorized_keys: root from ${SSH_KEYS_DIR}"
else
  echo "note: no ${SSH_KEYS_DIR}/authorized_keys.root — root password login only"
fi
if [[ -f "${SSH_KEYS_DIR}/authorized_keys.schnapp" ]]; then
  cp -a "${SSH_KEYS_DIR}/authorized_keys.schnapp" "${PROVISION_DIR}/ssh/"
  echo "SSH authorized_keys: schnapp from ${SSH_KEYS_DIR}"
else
  echo "note: no ${SSH_KEYS_DIR}/authorized_keys.schnapp — skip schnapp user keys"
fi
# Host keys (server identity) — without these, each rebuild gets new fingerprints
HOSTKEYS_SRC="${SSH_KEYS_DIR}/hostkeys"
if [[ -d "$HOSTKEYS_SRC" ]] && compgen -G "${HOSTKEYS_SRC}/ssh_host_*_key" >/dev/null; then
  cp -a "${HOSTKEYS_SRC}"/ssh_host_* "${PROVISION_DIR}/ssh/hostkeys/" 2>/dev/null || true
  echo "SSH host keys: from ${HOSTKEYS_SRC}"
  ssh-keygen -lf "${HOSTKEYS_SRC}/ssh_host_ed25519_key.pub" 2>/dev/null || true
else
  echo "note: no ${HOSTKEYS_SRC}/ssh_host_*_key — host keys will be regenerated per image (known_hosts churn)"
  echo "      see scripts/appliance/ssh/README.md"
fi

cat >"${PROVISION_DIR}/build.env" <<EOF
HOSTNAME=${HOSTNAME}
ROOT_PASSWORD=${ROOT_PASSWORD}
DB_NAME=${DB_NAME}
DB_USER=${DB_USER}
DB_PASSWORD=${DB_PASSWORD}
BLOB_NFS=${BLOB_NFS}
BLOB_MOUNTPOINT=${BLOB_MOUNTPOINT}
DATA_MOUNTPOINT=${DATA_MOUNTPOINT}
DATA_DISK_LABEL=${DATA_DISK_LABEL}
PGDATA=${PGDATA}
ADMIN_EMAIL=${ADMIN_EMAIL}
ADMIN_PASSWORD=${ADMIN_PASSWORD}
API_PORT=${API_PORT}
WEB_PORT=${WEB_PORT}
HTTPS_PORT=${HTTPS_PORT:-443}
HTTPS_ENABLE=${HTTPS_ENABLE:-1}
API_BIND=${API_BIND}
WEB_BIND=${WEB_BIND}
BACKUP_DIR=${BACKUP_DIR}
SMB_SHARE_NAME=${SMB_SHARE_NAME}
EOF

# Stage publish next to provision so chroot sees /mnt/publish and /mnt/clients
mkdir -p "${PROVISION_DIR}/publish/api" "${PROVISION_DIR}/publish/web" "${PROVISION_DIR}/clients"
rsync -a "$PUBLISH_DIR/api/" "${PROVISION_DIR}/publish/api/"
rsync -a "$PUBLISH_DIR/web/" "${PROVISION_DIR}/publish/web/"
rsync -a "$CLIENTS_STAGE/" "${PROVISION_DIR}/clients/"

# --- packages for Alpine image ---
# postgresql16 is in community on current Alpine; fallback handled in image if rename
# ifupdown-ng + dhcpcd: DHCP on eth0 (alpine-make-vm-image writes /etc/network/interfaces)
# samba: SMB share of client kits; cifs-utils: optional mount of QNAP shares via SMB
PACKAGES=(
  openssh
  openssh-server
  ifupdown-ng
  dhcpcd
  postgresql16
  postgresql16-contrib
  postgresql16-client
  nfs-utils
  cifs-utils
  samba
  samba-common-tools
  e2fsprogs
  util-linux
  curl
  ca-certificates
  tzdata
  icu-libs
  libstdc++
  sudo
  bash
  coreutils
  nano
  openssl
  libcap-utils
)

# --- system disk ---
# QNAP: filename often must be .img; content that boots is usually qcow2
SYSTEM_IMG="${ARTIFACTS}/schnapp-system.${DISK_EXT}"
if [[ "$SKIP_SYSTEM" -eq 0 ]]; then
  echo "building system disk ${SYSTEM_IMG} (qemu format=${DISK_FORMAT}, size=${SYSTEM_DISK_SIZE})"
  rm -f "$SYSTEM_IMG"
  # APK= must point at host apk or prefetched apk.static (see ensure_host_apk)
  # shellcheck disable=SC2068
  env APK="${APK:-apk}" \
  "$AMVI" \
    --image-format "$DISK_FORMAT" \
    --image-size "$SYSTEM_DISK_SIZE" \
    --branch "$ALPINE_BRANCH" \
    --kernel-flavor virt \
    --serial-console \
    --packages "${PACKAGES[*]}" \
    --fs-skel-dir "${SCRIPT_DIR}/overlay" \
    --script-chroot \
    "$SYSTEM_IMG" \
    "${PROVISION_DIR}/chroot-setup.sh"
  chmod 644 "$SYSTEM_IMG" || true
  if command -v qemu-img >/dev/null; then
    echo "system disk format check:"
    qemu-img info "$SYSTEM_IMG" | sed -n '1,8p' || true
  fi
else
  echo "skipping system disk"
fi

# --- data disk ---
DATA_IMG="${ARTIFACTS}/schnapp-data.${DISK_EXT}"
if [[ "$SKIP_DATA" -eq 0 ]]; then
  echo "building data disk ${DATA_IMG} (qemu format=${DISK_FORMAT}, size=${DATA_DISK_SIZE})"
  IMAGE_FORMAT="$DISK_FORMAT" "${SCRIPT_DIR}/make-data-disk.sh" "$DATA_IMG" "$DATA_DISK_SIZE"
  chmod 644 "$DATA_IMG" || true
else
  echo "skipping data disk"
fi

# --- manifest ---
{
  echo "Schnapp appliance build"
  echo "stamp=$STAMP"
  echo "date_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "alpine_branch=$ALPINE_BRANCH"
  echo "dotnet_rid=$DOTNET_RID"
  echo "system_disk=$(basename "$SYSTEM_IMG") size=$SYSTEM_DISK_SIZE format=$DISK_FORMAT ext=$DISK_EXT"
  echo "data_disk=$(basename "$DATA_IMG") size=$DATA_DISK_SIZE format=$DISK_FORMAT label=schnapp-data"
  echo "root_password_from_defaults=see defaults.env ROOT_PASSWORD (default example: schnapp)"
  echo "hostname=$HOSTNAME"
  echo "api=${API_BIND}:${API_PORT} web=${WEB_BIND}:${WEB_PORT} https=${HTTPS_ENABLE:-1}:${HTTPS_PORT:-443}"
  echo "smb_share=\\\\<vm-ip>\\${SMB_SHARE_NAME}  path=/srv/schnapp-clients"
  if [[ -d "$CLIENTS_STAGE/linux" ]]; then echo "clients_linux=yes"; else echo "clients_linux=no"; fi
  if [[ -d "$CLIENTS_STAGE/windows" ]]; then echo "clients_windows=yes"; else echo "clients_windows=no"; fi
  echo "blob_mount=$BLOB_MOUNTPOINT blob_nfs=${BLOB_NFS:-"(set on first boot)"}"
  echo "db=$DB_NAME user=$DB_USER"
  if [[ -f "$SYSTEM_IMG" ]]; then
    echo "system_sha256=$(sha256sum "$SYSTEM_IMG" | awk '{print $1}')"
  fi
  if [[ -f "$DATA_IMG" ]]; then
    echo "data_sha256=$(sha256sum "$DATA_IMG" | awk '{print $1}')"
  fi
} | tee "${ARTIFACTS}/MANIFEST.txt"

cp "${SCRIPT_DIR}/README-IMPORT.txt" "${ARTIFACTS}/README-IMPORT.txt"

# Relax ownership for the sudo user (images on /opt may stay root-readable)
if [[ -n "$BUILD_USER" ]]; then
  chown -R "${BUILD_USER}:${BUILD_USER}" "$ARTIFACTS" 2>/dev/null || true
  # .build can be huge; best-effort
  chown -R "${BUILD_USER}:${BUILD_USER}" "$BUILD_DIR" 2>/dev/null || true
fi

echo
echo "Done. Artifacts in ${ARTIFACTS}"
ls -lh "$ARTIFACTS"
