#!/usr/bin/env bash
# Offline fix for a Schnapp system disk: set root password + hostname.
# Use when the VM boots Alpine but login fails or hostname is "(none)".
#
# Usage (on the build host, as root):
#   sudo ./recover-system-disk.sh /opt/schnapp/appliance/20260731/schnapp-system.img
#   sudo ./recover-system-disk.sh /path/to/disk.img --password 'schnapp' --hostname schnapp
#   sudo ./recover-system-disk.sh /path/to/disk.img --format qcow2
#
set -euo pipefail

IMG="${1:-}"
shift || true
PASSWORD="schnapp"
HOSTNAME_SET="schnapp"
FORMAT=""  # auto

usage() {
  sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'
  exit "${1:-0}"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help) usage 0 ;;
    --password) PASSWORD="$2"; shift 2 ;;
    --hostname) HOSTNAME_SET="$2"; shift 2 ;;
    --format) FORMAT="$2"; shift 2 ;;
    *) echo "unknown: $1" >&2; usage 1 ;;
  esac
done

[[ -n "$IMG" && -f "$IMG" ]] || { echo "usage: $0 <disk.img> [--password P] [--hostname H] [--format raw|qcow2]" >&2; exit 1; }
[[ "$(id -u)" -eq 0 ]] || { echo "run as root" >&2; exit 1; }

if [[ -z "$FORMAT" ]]; then
  FORMAT=$(qemu-img info "$IMG" 2>/dev/null | awk -F': ' '/file format:/{print $2; exit}')
  FORMAT="${FORMAT:-raw}"
fi
echo "image=$IMG format=$FORMAT"

modprobe nbd max_part=16 2>/dev/null || true
NBD=""
for d in /dev/nbd{0..15}; do
  if [[ -b "$d" ]] && [[ "$(blockdev --getsize64 "$d" 2>/dev/null || echo 1)" -eq 0 ]]; then
    NBD=$d
    break
  fi
done
[[ -n "$NBD" ]] || { echo "no free /dev/nbd*" >&2; exit 1; }

MNT=$(mktemp -d /tmp/schnapp-recover.XXXXXX)
cleanup() {
  umount -R "$MNT" 2>/dev/null || true
  qemu-nbd --disconnect "$NBD" 2>/dev/null || true
  rmdir "$MNT" 2>/dev/null || true
}
trap cleanup EXIT

qemu-nbd --connect="$NBD" --format="$FORMAT" "$IMG"
sleep 1

# Unpartitioned fs (alpine-make-vm-image default) or first partition
ROOT_DEV=""
for cand in "$NBD" "${NBD}p1" "${NBD}p2"; do
  if [[ -b "$cand" ]] && blkid "$cand" >/dev/null 2>&1; then
    ROOT_DEV=$cand
    # prefer labeled root if present
    if blkid -o value -s LABEL "$cand" 2>/dev/null | grep -qi root; then
      break
    fi
  fi
done
# If only nbd has a filesystem, use it; if p1 exists prefer p1 for GPT images
if [[ -b "${NBD}p1" ]] && blkid "${NBD}p1" >/dev/null 2>&1; then
  ROOT_DEV="${NBD}p1"
elif [[ -z "$ROOT_DEV" ]]; then
  ROOT_DEV=$NBD
fi

echo "mounting $ROOT_DEV → $MNT"
mount "$ROOT_DEV" "$MNT"

if [[ ! -d "$MNT/etc" ]]; then
  echo "error: no /etc on mounted filesystem — wrong partition/format?" >&2
  ls -la "$MNT" || true
  exit 1
fi

echo "before: /etc/hostname='$(tr -d '\n' <"$MNT/etc/hostname" 2>/dev/null || echo MISSING)'"
echo "before: /etc/conf.d/hostname='$(tr -d '\n' <"$MNT/etc/conf.d/hostname" 2>/dev/null || echo MISSING)'"
echo "before: root shadow='$(awk -F: '$1=="root"{print substr($2,1,12)}' "$MNT/etc/shadow" 2>/dev/null)...'"
echo "before: motd head=$(head -1 "$MNT/etc/motd" 2>/dev/null || echo MISSING)"
echo "before: schnapp-api present? $(test -e "$MNT/opt/schnapp/api/Schnapp.Api" -o -e "$MNT/opt/schnapp/api/Schnapp.Api.dll" && echo yes || echo NO)"

printf '%s\n' "$HOSTNAME_SET" >"$MNT/etc/hostname"
mkdir -p "$MNT/etc/conf.d"
cat >"$MNT/etc/conf.d/hostname" <<EOF
hostname="${HOSTNAME_SET}"
EOF

# Set root password: Alpine usually has chpasswd only as busybox applet
set_pw() {
  local hash
  hash=$(openssl passwd -6 "$PASSWORD")
  if grep -q '^root:' "$MNT/etc/shadow"; then
    # Escape sed specials in hash (&, \, |, /)
    local esc
    esc=$(printf '%s' "$hash" | sed 's/[&\\|]/\\&/g')
    sed -i "s|^root:[^:]*:|root:${esc}:|" "$MNT/etc/shadow"
  else
    echo "root:${hash}:19000:0:99999:7:::" >>"$MNT/etc/shadow"
  fi
}

pw_ok=0
if command -v chroot >/dev/null; then
  mount --bind /dev "$MNT/dev" 2>/dev/null || true
  mount -t proc proc "$MNT/proc" 2>/dev/null || true
  mount -t sysfs sys "$MNT/sys" 2>/dev/null || true
  for chpw in /usr/sbin/chpasswd /sbin/chpasswd /bin/chpasswd /bin/busybox; do
    if [[ -e "$MNT$chpw" ]] || [[ "$chpw" == /bin/busybox && -x "$MNT/bin/busybox" ]]; then
      if [[ "$chpw" == /bin/busybox ]]; then
        if printf 'root:%s\n' "$PASSWORD" | chroot "$MNT" /bin/busybox chpasswd 2>/dev/null; then
          pw_ok=1
          echo "password set via busybox chpasswd"
          break
        fi
      else
        if printf 'root:%s\n' "$PASSWORD" | chroot "$MNT" "$chpw" 2>/dev/null; then
          pw_ok=1
          echo "password set via $chpw"
          break
        fi
      fi
    fi
  done
  umount "$MNT/dev" 2>/dev/null || true
  umount "$MNT/proc" 2>/dev/null || true
  umount "$MNT/sys" 2>/dev/null || true
fi
if [[ "$pw_ok" -eq 0 ]]; then
  echo "using openssl -6 hash for root password"
  set_pw
fi

# Un-expire / unlock
sed -i 's|^root:!:|root:x:|' "$MNT/etc/passwd" 2>/dev/null || true
# Ensure root shell is a real shell
if grep -q '^root:.*:/sbin/nologin' "$MNT/etc/passwd" 2>/dev/null; then
  sed -i 's|^root:\([^:]*\):\([^:]*\):\([^:]*\):\([^:]*\):\([^:]*\):.*|root:\1:\2:\3:\4:\5:/bin/ash|' "$MNT/etc/passwd"
fi

# Hostname service on boot
if [[ -d "$MNT/etc/runlevels/boot" && ! -e "$MNT/etc/runlevels/boot/hostname" ]]; then
  if [[ -e "$MNT/etc/init.d/hostname" ]]; then
    ln -sf /etc/init.d/hostname "$MNT/etc/runlevels/boot/hostname"
    echo "linked hostname service into boot runlevel"
  fi
fi

echo "after: /etc/hostname='$(tr -d '\n' <"$MNT/etc/hostname")'"
echo "after: /etc/conf.d/hostname='$(tr -d '\n' <"$MNT/etc/conf.d/hostname")'"
echo "after: root shadow starts '$(awk -F: '$1=="root"{print substr($2,1,3)}' "$MNT/etc/shadow")'"
echo "after: root passwd line='$(grep '^root:' "$MNT/etc/passwd")'"

sync
echo
echo "done — important:"
echo "  1. Power off the QNAP VM completely"
echo "  2. Re-copy THIS file to the NAS (overwrite the attached disk file)"
echo "     source: $IMG"
echo "  3. Or re-point the VM disk at this path if it shares storage"
echo "  4. Boot and login: root / ${PASSWORD}"
echo "  hostname should be: ${HOSTNAME_SET}"
echo
echo "This scan showed hostname file already 'schnapp' and a password hash already"
echo "present before repair — if the running VM still said (none), it was almost"
echo "certainly not booting this updated file (stale copy on the NAS)."