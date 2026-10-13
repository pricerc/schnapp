Schnapp appliance — QNAP Virtualization Station import
======================================================

You need two disk images from this build folder
(default on the build host: /opt/schnapp/appliance/YYYYMMDD/):

  schnapp-system.img     OS + Schnapp API/Web (replace when upgrading the appliance)
  schnapp-data.img       Durable data (LABEL=schnapp-data → /opt/schnapp/data) — keep across upgrades

Format quirk: files are named *.img (QNAP UI) but contain qcow2 (what actually boots).
Do not convert to raw unless you know VS wants raw on your model.

Root / schnapp login (console / SSH)
------------------------------------
  root     password: ROOT_PASSWORD from defaults.env (example: schnapp)
           and/or SSH key from scripts/appliance/ssh/authorized_keys.root
  schnapp  SSH key from scripts/appliance/ssh/authorized_keys.schnapp
           (shell /bin/ash; used for services + optional login)

  Keys are baked at image build — re-adding authorized_keys.* and rebuilding
  the system disk keeps the same access after replace.

  Change root password after first login:  passwd

If hostname is "(none)" or root password is rejected, the image did not get
full Schnapp provision identity. On the build host, offline-fix the system
disk then re-attach on QNAP:

  sudo ./scripts/appliance/recover-system-disk.sh /path/to/schnapp-system.img \
    --password schnapp --hostname schnapp


Virtualization Station (typical flow)
-------------------------------------

1. Copy both .img files to the NAS (e.g. a folder under Public or a dedicated share).

2. Create a new VM:
   - CPU: 1–2 vCPU
   - RAM: 2048 MB
   - Network: VirtIO or Intel e1000 on the LAN bridge (DHCP; set a DHCP reservation)
   - Do NOT use a large single disk from the wizard if you will attach these images instead.

3. Attach disks (order matters):
   - Disk 1 (boot): schnapp-system.img  (VirtIO, image type raw/img)
   - Disk 2:        schnapp-data.img    (VirtIO)
   If VS only imports one disk at create time, create the VM with the system
   disk, then add the data disk as a second hard drive.

4. Firmware: BIOS (SeaBIOS) is the default for these images (extlinux).
   If your VS profile defaults to UEFI only, switch to BIOS/legacy for this VM.

5. Console: open the VS console, log in as root (password from build defaults.env,
   default example is "schnapp" unless you changed it).

6. Data layout (second disk must be attached as disk 2):
     /opt/schnapp/data/postgresql   PGDATA (unversioned)
     /opt/schnapp/data/blobs        screenshots (local on data disk by default)
   Optional NFS overmount onto the same blobs path:
     schnapp-configure --blob-nfs 'nas:/schnapp/blobs' --write-env
     # or: mount nas:/schnapp/blobs /opt/schnapp/data/blobs
     rc-service schnapp-api restart
     rc-service schnapp-web restart
   Ensure the NFS share is exported to the VM if used.

7. Smoke:
     curl -s http://127.0.0.1:5080/api/v1/health
     curl -sI http://127.0.0.1/Login
     curl -skI https://127.0.0.1/Login
   From another machine: http://<vm-ip>/  and  https://<vm-ip>/
   (HTTPS uses a self-signed cert by default — browser warning is expected.
    Replace /opt/schnapp/config/tls/{cert,key}.pem for a real cert.)

8. Tray clients (SMB share, guest read-only):
     Windows Explorer:  \\<vm-ip>\clients
     Linux:             smb://<vm-ip>/clients
   Layout: clients/linux/  (and clients/windows/ if the kit was included at build)
   On Linux workstations run deploy-linux-client.sh from the linux kit.
   Point the client API URL at http://<vm-ip>:5080 (or your reverse-proxy HTTPS URL).

Upgrade (replace system disk, keep data)
----------------------------------------

1. On the running VM (or via backup cron):
     schnapp-backup pre-upgrade
   (writes under /opt/schnapp/data/blobs/_appliance/db)

2. Power off the VM.

3. Detach/replace only the system disk with a new schnapp-system.img.
   Leave schnapp-data.img attached as the second disk.

4. Power on. First boot scripts re-apply app config; Postgres uses existing data.

5. If Postgres data was lost or you use dump-based recovery instead:
     schnapp-restore
   (defaults to /opt/schnapp/data/blobs/_appliance/db/schnapp-latest.dump)

Guest commands
--------------

  schnapp-configure   network-agnostic settings + BlobRoot + appsettings
  schnapp-backup      pg_dump to blob share
  schnapp-restore     pg_restore from dump
  schnapp-data-volume ensure second disk mounted at /opt/schnapp/data

Ports: Web 80 + 443 (HTTPS), API 5080 (LAN/clients), SSH 22, SMB 445 (share "clients").
