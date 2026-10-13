#!/bin/sh
# Shared helpers for appliance guest tools (BusyBox ash / dash compatible).
# shellcheck shell=sh

SCHNAPP_CONF_D="${SCHNAPP_CONF_D:-/etc/conf.d/schnapp-appliance}"
SCHNAPP_ENV="${SCHNAPP_ENV:-/etc/schnapp/appliance.env}"

# IMPORTANT: with set -e, never end a function with a failing `[ -f ] && …`
# chain when the file is optional — that exits the whole script silently.
schnapp_load_config() {
	if [ -f "$SCHNAPP_CONF_D" ]; then
		# shellcheck disable=SC1090
		. "$SCHNAPP_CONF_D"
	fi
	if [ -f "$SCHNAPP_ENV" ]; then
		# shellcheck disable=SC1090
		. "$SCHNAPP_ENV"
	fi
	return 0
}

schnapp_need_root() {
	if [ "$(id -u)" -ne 0 ]; then
		echo "error: run as root" >&2
		exit 1
	fi
}

schnapp_log() {
	echo "schnapp-appliance: $*"
}

schnapp_die() {
	echo "error: $*" >&2
	exit 1
}

# Resolve the second disk device (prefer LABEL, then common virtio/SCSI names).
schnapp_data_device() {
	schnapp_load_config
	label="${DATA_DISK_LABEL:-schnapp-data}"
	if blkid -L "$label" >/dev/null 2>&1; then
		blkid -L "$label"
		return 0
	fi
	for dev in /dev/vdb /dev/sdb; do
		if [ -b "$dev" ]; then
			echo "$dev"
			return 0
		fi
	done
	return 1
}
