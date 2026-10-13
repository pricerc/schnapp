# Schnapp QNAP appliance (Alpine)

Build two disks for QNAP Virtualization Station (**qcow2 content, `.img` names** — VS often wants the extension while only qcow2 boots):

| Disk | File | Purpose |
|------|------|---------|
| System | `schnapp-system.img` | Alpine + OpenRC + self-contained .NET API/Web + guest tools |
| Data | `schnapp-data.img` | Durable volume (`LABEL=schnapp-data` → `/opt/schnapp/data`: postgresql + blobs) |

**Root password:** `ROOT_PASSWORD` in `defaults.env` (example default: `schnapp`).

**SSH keys (survive rebuild):** put public keys in `scripts/appliance/ssh/authorized_keys.root` and `authorized_keys.schnapp` (see `ssh/README.md`). Baked into every system image at build time.

**IPv6:** disabled in the image (sysctl + `dhcpcd` `noipv6` + kernel `ipv6.disable=1`) — IPv4 LAN only.

If a booted VM shows hostname `(none)` or root login fails, provision likely did not stick on that image. Offline fix (no rebuild):

```bash
sudo ./scripts/appliance/recover-system-disk.sh /opt/schnapp/appliance/YYYYMMDD/schnapp-system.img \
  --password schnapp --hostname schnapp
# then re-copy the .img to the NAS / re-attach
```

Blobs default to `/opt/schnapp/data/blobs` on the data disk. Set `BLOB_NFS` to overmount a QNAP share on that path. DHCP is used for networking; set a reservation on-site.

## Locked choices

- Distro: **Alpine** (musl) → server RID `linux-musl-x64`
- Import: **create VM + attach disks** (not OVA)
- Network: **DHCP**
- DB persistence: **second volume**
- DB engine: **PostgreSQL** on this image. The API also supports Firebird 5 on other hosts (`docs/deployment-fedora.md`)
- **SMB**: Samba serves tray client kits at `\\<vm>\clients` (`/srv/schnapp-clients`)

## Client kits on the VM

Build embeds:

| Path on VM | Source |
|------------|--------|
| `/srv/schnapp-clients/linux/` | `build-linux-client-deployment.sh` (`linux-x64` self-contained) |
| `/srv/schnapp-clients/windows/` | Optional — pass `--windows-client DIR` or set `WINDOWS_CLIENT_KIT` (Windows-built kit) |

Share is **guest-readable** (trusted LAN). Also installs `cifs-utils` if you want the VM to mount a QNAP share via SMB instead of NFS.

## Build host requirements

- Root (for `qemu-nbd` / mounts)
- `qemu-img`, `qemu-nbd`, `rsync`, `mkfs.ext4`, `curl` (or `wget`)
- .NET **10 SDK**
- Network access to **Alpine CDN** (`dl-cdn.alpinelinux.org`) for packages + static `apk`
- Optional: GitHub for `alpine-make-vm-image` script (cached under `vendor/`)

If the build stalls on `apk not found, downloading static apk-tools` / GitLab timeout:
the script now pulls `apk-tools-static` from the Alpine CDN into `vendor/apk.static-*`
instead of GitLab. Re-run the build; first successful fetch is cached.

## Quick build

```bash
cd /path/to/schnapp
cp scripts/appliance/defaults.env.example scripts/appliance/defaults.env
# edit defaults.env (passwords, optional BLOB_NFS, admin seed)

sudo ./scripts/appliance/build-appliance.sh
# → /opt/schnapp/appliance/YYYYMMDD/{schnapp-system.img,schnapp-data.img,...}
# intermediate publish: /opt/schnapp/appliance/.build/publish/
```

Images default to **`/opt/schnapp/appliance/`** (not the worktree) so large raw disks do not fill `/home`. Override with `APPLIANCE_ROOT`, `--appliance-root`, or `--artifacts`.

**Note:** qcow2-named-as-`.img` stays sparse-ish on the build host. Set `DISK_FORMAT=raw` only if your VS truly needs raw.

Reuse binaries:

```bash
sudo ./scripts/appliance/build-appliance.sh --skip-publish
```

Rebuild only the data disk:

```bash
sudo ./scripts/appliance/build-appliance.sh --skip-system
```

## Layout

```
scripts/appliance/
  build-appliance.sh       # orchestrator
  make-data-disk.sh        # empty labeled ext4 data disk
  defaults.env.example
  provision/chroot-setup.sh
  overlay/                 # files copied into image root
    etc/init.d/schnapp-*
    usr/local/sbin/schnapp-*
  README-IMPORT.txt        # copied into artifact folder
  vendor/                  # downloaded alpine-make-vm-image
```

## Guest tools

| Command | Role |
|---------|------|
| `schnapp-configure` | Blob NFS, DB password, Kestrel binds, Production appsettings |
| `schnapp-backup` | `pg_dump -Fc` → `$BACKUP_DIR` (on blob share) |
| `schnapp-restore` | restore dump; restart API |
| `schnapp-data-volume` | format/mount second disk |
| `schnapp-firstboot` | one-shot init (OpenRC) |

## Upgrade model

1. `schnapp-backup` (and/or rely on data disk).  
2. Power off; **replace system .img only**; keep data disk.  
3. Power on; if needed `schnapp-restore` from blob share.

See [docs/deployment-qnap-appliance.md](../../docs/deployment-qnap-appliance.md).
