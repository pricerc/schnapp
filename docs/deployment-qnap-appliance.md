# Schnapp appliance on QNAP (Alpine VM)

Personal / small-team deployment: a **2 GB Alpine VM** on QNAP Virtualization Station with:

- **System disk** — OS + Schnapp API/Web (replace to upgrade the appliance)
- **Data disk** — mounted at `/opt/schnapp/data` (`LABEL=schnapp-data`): PostgreSQL + blobs
- **Optional NFS** — overmount `nas:/…` onto `/opt/schnapp/data/blobs` (or keep blobs on the data disk only)

Build scripts live under [`scripts/appliance/`](../scripts/appliance/).

## Why this shape

| Choice | Rationale |
|--------|-----------|
| Alpine | Light enough for entry-level NAS VMs |
| Attach disks (not OVA) | Simple; fine for rare rebuilds |
| DHCP + reservation | No baked static IP |
| Second disk for Postgres | Upgrade OS/app without dump if the data disk is kept |
| Keep PostgreSQL | App is Npgsql/DbUp/Postgres-specific; dumps still supported |

## Build

On a Linux host with root, QEMU nbd tools, and the .NET 10 SDK:

```bash
cp scripts/appliance/defaults.env.example scripts/appliance/defaults.env
# set ROOT_PASSWORD, DB_PASSWORD, ADMIN_*, optional BLOB_NFS

sudo ./scripts/appliance/build-appliance.sh
```

Artifacts (default **`/opt/schnapp/appliance/YYYYMMDD/`**, not under the worktree — large disks stay off `/home`):

- `schnapp-system.img` — **qcow2** image with a `.img` name (QNAP often requires the extension)
- `schnapp-data.img` — Postgres data disk (same format quirk)
- `MANIFEST.txt`
- `README-IMPORT.txt`

**Root password** is whatever you set as `ROOT_PASSWORD` in `scripts/appliance/defaults.env` when the image was built (repo example default: `schnapp`).

Intermediate `dotnet publish` output: `/opt/schnapp/appliance/.build/`. Override with `APPLIANCE_ROOT` or `--artifacts`.

Publish uses **self-contained** `linux-musl-x64` binaries (no ASP.NET package on Alpine).

## QNAP import (summary)

1. Copy both `.img` files to the NAS.  
2. Create VM: **2 GB RAM**, 1–2 vCPU, LAN bridge, **BIOS**.  
3. Attach **system** as disk 1, **data** as disk 2 (VirtIO). Prefer image type that accepts qcow2 if offered; otherwise attach the `.img` files as produced.  
4. Boot; login **`root` / `ROOT_PASSWORD` from build `defaults.env`** (default example: **`schnapp`**).  
5. Blobs default to `/opt/schnapp/data/blobs` on the data disk. Optional NFS overmount:

```bash
schnapp-configure --blob-nfs 'nas:/schnapp/blobs' --write-env
rc-service schnapp-api restart
rc-service schnapp-web restart
```

6. Health: API `http://<vm-ip>:5080/api/v1/health`; Web `http://<vm-ip>/Login` and `https://<vm-ip>/Login` (self-signed TLS by default).

7. **Tray clients via SMB** (guest-readable):

   - Windows: `\\<vm-dhcp-ip>\clients`
   - Linux: `smb://<vm-dhcp-ip>/clients`
   - Contents: `linux/` kit from the appliance build; `windows/` if you passed `--windows-client` when building the image.

Full notes: `scripts/appliance/README-IMPORT.txt` (also shipped in the artifact folder).

## Backup and upgrade

**Backup** (while running):

```bash
schnapp-backup nightly   # → /opt/schnapp/data/blobs/_appliance/db/schnapp-*.dump
```

**Upgrade system only** (preferred when data disk is intact):

1. `schnapp-backup pre-upgrade`  
2. Power off; replace **system** `.img`; keep **data** disk  
3. Power on  

**Restore from dump** (new data disk or disaster recovery):

```bash
schnapp-restore /mnt/blobs/_appliance/db/schnapp-latest.dump
```

## Resource notes (2 GB VM)

- Postgres tuned for low RAM via `/etc/schnapp/postgresql-appliance.conf` (apply into PGDATA after first init if needed).  
- Avoid concurrent huge uploads (default max shot size is large; lower `screenshot.max_upload_bytes` in admin settings if you OOM).  
- Prefer QNAP reverse proxy for HTTPS; guest listens on `0.0.0.0:5080/5081`.

## Related

- [deployment-fedora.md](deployment-fedora.md) — conventional host install (systemd)  
- [server.md](server.md) — API/Web behavior  
- [architecture.md](architecture.md) — blobs + metadata split  
