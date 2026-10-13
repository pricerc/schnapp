#!/usr/bin/env bash
# Create an empty labeled-ext4 data disk image (raw .img for QNAP VS).
# Usage: sudo ./make-data-disk.sh [path.img] [size]
set -euo pipefail

OUT="${1:-}"
SIZE="${2:-8G}"
LABEL="${DATA_DISK_LABEL:-schnapp-data}"
# raw = .img for QNAP; override with IMAGE_FORMAT=qcow2 if needed
IMAGE_FORMAT="${IMAGE_FORMAT:-raw}"

if [[ -z "$OUT" ]]; then
  echo "usage: $0 <path.img> [size]" >&2
  exit 1
fi

if [[ "$(id -u)" -ne 0 ]]; then
  echo "error: run as root (needs qemu-nbd)" >&2
  exit 1
fi

mkdir -p "$(dirname "$OUT")"
if [[ -f "$OUT" ]]; then
  echo "removing existing $OUT"
  rm -f "$OUT"
fi

echo "creating $OUT ($SIZE, format=$IMAGE_FORMAT)"
qemu-img create -f "$IMAGE_FORMAT" "$OUT" "$SIZE"

# Load nbd
modprobe nbd max_part=16 2>/dev/null || true
# Find free nbd
NBD=""
for d in /dev/nbd{0..15}; do
  if [[ -b "$d" ]] && [[ "$(blockdev --getsize64 "$d" 2>/dev/null || echo 1)" -eq 0 ]]; then
    NBD=$d
    break
  fi
done
[[ -n "$NBD" ]] || { echo "error: no free /dev/nbd*" >&2; exit 1; }

cleanup() {
  qemu-nbd --disconnect "$NBD" 2>/dev/null || true
}
trap cleanup EXIT

qemu-nbd --connect="$NBD" --format="$IMAGE_FORMAT" "$OUT"
sleep 1
mkfs.ext4 -F -L "$LABEL" "$NBD"
sync
qemu-nbd --disconnect "$NBD"
trap - EXIT

echo "data disk ready: $OUT (LABEL=$LABEL, format=$IMAGE_FORMAT)"
qemu-img info "$OUT"
