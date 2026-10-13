# Deploying Schnapp on Fedora (API + Web + catalog)

This guide covers production-style install of the **database**, **API**, and **Web** BFF on a Fedora (or close RHEL-family) host. The catalog engine is **PostgreSQL** (default) or **Firebird 5 SuperServer**, chosen in `Schnapp:DatabaseProvider`.

| Step | What | How |
|------|------|-----|
| **1** | Server packages | Documented below |
| **2** | Database role + DB | `setup-database.sh` (PostgreSQL) or `setup-database-firebird.sh` (Firebird) |
| **3** | System user + directories | Script: `setup-server-dirs.sh` |
| **4** | Build / copy binaries | `build-server-deployment.sh` → `artifacts/publish/server/YYYYMMDD` (or upgrade kit) |
| **5** | Production config | Script: `write-server-config.sh` (publish kit only for new installs) |
| **6** | systemd units | Documented below (+ unit files in kit) |
| **7** | Reverse proxy + TLS | Documented below |
| **8** | Firewall | Documented below |
| — | Tray clients | Documented below; separate client kits |
| — | Upgrade existing host | `artifacts/upgrade/server/YYYYMMDD` + `upgrade-server.sh` |

Build kits (staging + full install publish + upgrade):

```bash
./scripts/deployment/build-server-deployment.sh
# or self-contained (no shared runtime on server):
./scripts/deployment/build-server-deployment.sh --self-contained
```

| Path | Purpose |
|------|---------|
| `artifacts/staging/server/` | Raw API + Web publish (overwritten each build) |
| `artifacts/publish/server/YYYYMMDD/` | Full install kit (binaries, setup, config writers, systemd, this doc) |
| `artifacts/upgrade/server/YYYYMMDD/` | Binary upgrade only (`upgrade-server.sh`; does not touch config/data) |

Kit folders are dated (`YYYYMMDD`, UTC). Optional `.zip` archives are off by default; pass `--zip` to write them beside the dated kit folders.

---

## 1. Server packages (manual)

On the **Fedora server**:

**PostgreSQL** (default):

```bash
sudo dnf install -y postgresql-server postgresql-contrib
```

Initialize and start Postgres if this host has never run it:

```bash
sudo postgresql-setup --initdb
sudo systemctl enable --now postgresql
```

**Firebird 5 SuperServer** (when `Schnapp:DatabaseProvider=Firebird`):

```bash
sudo dnf install -y firebird-superserver firebird-utils
sudo systemctl enable --now firebird-superserver
```

Change the SYSDBA password from `masterkey` before creating the app database. SuperServer listens on port **3050** (bind to localhost).

**.NET runtime** — choose one approach:

**A. Framework-dependent publish** (smaller kit under `artifacts/publish/server/YYYYMMDD`):

```bash
# Microsoft package repo if Fedora lags; see:
# https://learn.microsoft.com/dotnet/core/install/linux-fedora
sudo dnf install -y aspnetcore-runtime-10.0
# or whatever package provides /usr/bin/dotnet with ASP.NET Core 10
```

**B. Self-contained publish** — no runtime package; use:

```bash
./scripts/deployment/build-server-deployment.sh --self-contained
```

Optional for §7:

```bash
sudo dnf install -y nginx certbot python3-certbot-nginx
# or Caddy
```

---

## 2. Database (script)

Pick **one** engine. There is no conversion path between them.

**PostgreSQL** (needs ability to run as the `postgres` OS user):

```bash
# From a checkout or from the deployment kit's host/ folder
export SCHNAPP_DB_PASSWORD='use-a-strong-password'
sudo -E ./scripts/deployment/host/setup-database.sh
# or from kit:
# sudo -E ./host/setup-database.sh
```

Creates role `schnapp` and database `schnapp` (names overridable via env/`--name`/`--user`).

If the API cannot connect, ensure `pg_hba.conf` allows password auth from `127.0.0.1` for that user, then `systemctl reload postgresql`.

**Firebird 5 SuperServer:**

```bash
export SCHNAPP_DB_PASSWORD='use-a-strong-password'
sudo -E ./scripts/deployment/host/setup-database-firebird.sh
# or from kit:
# sudo -E ./host/setup-database-firebird.sh
```

Creates user `schnapp` and an empty `{prefix}/data/firebird/schnapp.fdb` (override directory with `SCHNAPP_FIREBIRD_DIR`). The API does not create the `.fdb`.

Then pass `--provider firebird` to `write-server-config.sh` (step 5).

Schema migrations are applied by the **API on first start** when `Schnapp:MigrateOnStartup` is true (default). Instant columns are stored as **UTC**. IANA zones live on `app_user.time_zone_id` / `screenshot.client_time_zone_id`.

---

## 3. Directories and service user (script)

```bash
sudo ./scripts/deployment/host/setup-server-dirs.sh
# default: user schnapp, /opt/schnapp/{api,web,data/blobs,config}
```

---

## 4. Build kits under `artifacts/` (script)

On a **build machine** with the .NET 10 SDK and this repo:

```bash
# Framework-dependent (default): staging + publish + upgrade
./scripts/deployment/build-server-deployment.sh

# Self-contained linux-x64
./scripts/deployment/build-server-deployment.sh --self-contained

# Custom artifacts root (still uses staging/publish/upgrade under it)
./scripts/deployment/build-server-deployment.sh -o /tmp/out

# Staging only (raw publish), then assemble kits later:
./scripts/deployment/publish-server.sh
./scripts/deployment/build-server-deployment.sh --skip-publish
```

Layout:

```
artifacts/
  staging/server/              # raw build: api/, web/, MANIFEST.txt
  publish/server/YYYYMMDD/     # full install kit
    api/  web/  host/  systemd/  INSTALL.txt  README-DEPLOY.md
  upgrade/server/YYYYMMDD/     # upgrade kit
    api/  web/  upgrade-server.sh  systemd/  UPGRADE.txt
```

**New install** — copy the **publish** kit to the server and follow `INSTALL.txt` (dirs, DB, config, rsync, units).

**Existing install** — copy the **upgrade** kit and run:

```bash
sudo ./upgrade-server.sh
# optional: sudo ./upgrade-server.sh --update-units
```

That replaces `api/` and `web/` only; `{prefix}/config` and `{prefix}/data` are left alone.

---

## 5. Production config (script)

On the server, after §2–4:

```bash
sudo SCHNAPP_DB_PASSWORD='…' \
  SCHNAPP_ADMIN_EMAIL='admin@example.com' \
  SCHNAPP_ADMIN_PASSWORD='long-random-admin-password' \
  ./scripts/deployment/host/write-server-config.sh --link-into-apps --force
```

Firebird SuperServer:

```bash
sudo SCHNAPP_DB_PASSWORD='…' \
  SCHNAPP_ADMIN_EMAIL='admin@example.com' \
  SCHNAPP_ADMIN_PASSWORD='long-random-admin-password' \
  ./scripts/deployment/host/write-server-config.sh --provider firebird --link-into-apps --force
```

`--provider` is `postgresql` (default) or `firebird`. That writes `Schnapp:DatabaseProvider` and the matching `ConnectionStrings:Main`.

### Listen addresses and ports

Kestrel URLs are written to `ASPNETCORE_URLS` in `api.env` / `web.env`.

| Option | Default | Meaning |
|--------|---------|---------|
| `--api-bind HOSTS` | `127.0.0.1` | Comma-separated IPs/hosts for the API |
| `--web-bind HOSTS` | `127.0.0.1` | Comma-separated IPs/hosts for the Web UI |
| `--api-port PORT` | `5080` | API port (applied to each API bind) |
| `--web-port PORT` | `5081` | Web port (applied to each Web bind) |
| `--api-urls URLS` | *(from bind+port)* | Full override, e.g. `http://127.0.0.1:5080;http://0.0.0.0:5080` |
| `--web-urls URLS` | *(from bind+port)* | Full override for Web |
| `--api-url URL` | first API listen URL | Web BFF → API base (`Schnapp:ApiBaseUrl`) |

Examples:

```bash
# Web on all interfaces :8080; API only localhost :5080
sudo SCHNAPP_DB_PASSWORD='…' ./scripts/deployment/host/write-server-config.sh --force \
  --web-bind 0.0.0.0 --web-port 8080 \
  --api-bind 127.0.0.1 --api-port 5080 \
  --api-url http://127.0.0.1:5080 \
  --link-into-apps

# API on loopback + LAN
sudo SCHNAPP_DB_PASSWORD='…' ./scripts/deployment/host/write-server-config.sh --force \
  --api-bind 127.0.0.1,192.168.1.10 --api-port 5080 \
  --web-bind 192.168.1.10 --web-port 80 \
  --api-url http://127.0.0.1:5080 \
  --link-into-apps

# Explicit multi-URL form
sudo SCHNAPP_DB_PASSWORD='…' ./scripts/deployment/host/write-server-config.sh --force \
  --api-urls 'http://127.0.0.1:5080;http://0.0.0.0:5080' \
  --web-urls 'http://0.0.0.0:8080' \
  --api-url http://127.0.0.1:5080 \
  --link-into-apps
```

Env aliases: `SCHNAPP_API_BIND`, `SCHNAPP_WEB_BIND`, `SCHNAPP_API_PORT`, `SCHNAPP_WEB_PORT`,
`SCHNAPP_API_URLS`, `SCHNAPP_WEB_URLS`, `SCHNAPP_API_URL`.

Writes under `/opt/schnapp/config/`:

| File | Purpose |
|------|---------|
| `api.appsettings.Production.json` | `DatabaseProvider`, connection string, BlobRoot |
| `web.appsettings.Production.json` | `ApiBaseUrl` for BFF → API |
| `api.env` | systemd `EnvironmentFile` for API (`ASPNETCORE_URLS`, …) |
| `web.env` | systemd `EnvironmentFile` for Web |

Admin env vars seed a user **only when the database has zero users**.

`--link-into-apps` symlinks `appsettings.Production.json` into `/opt/schnapp/api` and `web`.

After changing ports/binds, restart: `systemctl restart schnapp-api schnapp-web`.

---

## 6. systemd units (manual install)

Unit templates ship in the repo and kit:

- `scripts/deployment/systemd/schnapp-api.service`
- `scripts/deployment/systemd/schnapp-web.service`

Install:

```bash
sudo cp scripts/deployment/systemd/schnapp-api.service /etc/systemd/system/
sudo cp scripts/deployment/systemd/schnapp-web.service /etc/systemd/system/
# From a kit: sudo cp systemd/*.service /etc/systemd/system/

# If you published self-contained, edit both units:
#   comment:  ExecStart=/usr/bin/dotnet …/Schnapp.*.dll
#   uncomment: ExecStart=/opt/schnapp/api/Schnapp.Api  (or …/web/Schnapp.Web)

sudo systemctl daemon-reload
sudo systemctl enable --now schnapp-api schnapp-web
sudo journalctl -u schnapp-api -u schnapp-web -f
```

Smoke:

```bash
curl -s http://127.0.0.1:5080/api/v1/health
curl -sI http://127.0.0.1:5081/Login
```

Units load `/opt/schnapp/config/api.env` and `web.env` (created in §5). Kestrel listens on **localhost** only so a reverse proxy can own the public ports.

`schnapp-api.service` waits for `postgresql.service` **and** `firebird-superserver.service` (`After=`). Only the engine you installed needs to be running.

---

## 7. Reverse proxy + TLS

Keep API/Web on `127.0.0.1` and terminate HTTPS at the edge.

### nginx sketch

```nginx
# /etc/nginx/conf.d/schnapp.conf
server {
    listen 443 ssl http2;
    server_name schnapp.example.com;

    # ssl_certificate     /etc/letsencrypt/live/schnapp.example.com/fullchain.pem;
    # ssl_certificate_key /etc/letsencrypt/live/schnapp.example.com/privkey.pem;

    # Web UI (BFF)
    location / {
        proxy_pass         http://127.0.0.1:5081;
        proxy_http_version 1.1;
        proxy_set_header   Host $host;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
    }

    # Optional: expose API on same host for tray/CLI
    location /api/ {
        proxy_pass         http://127.0.0.1:5080/api/;
        proxy_http_version 1.1;
        proxy_set_header   Host $host;
        proxy_set_header   X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        client_max_body_size 60m;   # screenshots
    }
}
```

```bash
sudo nginx -t && sudo systemctl reload nginx
sudo certbot --nginx -d schnapp.example.com
```

**Web BFF** only needs to reach the API on the server (`Schnapp:ApiBaseUrl=http://127.0.0.1:5080` is fine).

**Tray / CLI** need a URL that reaches the API (e.g. `https://schnapp.example.com` if you proxy `/api`, or a dedicated API hostname). Configure that URL in client login/settings—not in the Web appsettings—unless you also change how clients are documented for your DNS layout.

---

## 8. Firewall

If only nginx (or Caddy) is public:

```bash
sudo firewall-cmd --permanent --add-service=https
sudo firewall-cmd --permanent --add-service=http   # for ACME redirect if needed
sudo firewall-cmd --reload
```

Do **not** open 5080/5081 publicly when using a reverse proxy on the same host.

```bash
# verify nothing public on Kestrel ports
ss -lntp | grep -E '5080|5081'
```

---

## Clients (workstations)

Server deploy does **not** install the tray app. Build client kits on a dev machine:

```bash
./scripts/deployment/build-linux-client-deployment.sh
# First install:  copy artifacts/publish/linux-client/YYYYMMDD → workstation, ./deploy-linux-client.sh
# Upgrade later:  copy artifacts/upgrade/linux-client/YYYYMMDD → workstation, ./upgrade-linux-client.sh
# Login → API base URL = your public API (see §7)
```

Windows (on a Windows build machine): `.\scripts\deployment\build-windows-client-deployment.ps1` → `artifacts/{staging,publish,upgrade}/windows-client` (publish/upgrade under a `YYYYMMDD` subfolder).

Kit install/upgrade scripts live under `scripts/deployment/clients/` (same idea as `host/` for server).

See [client.md](client.md).

---

## Ops notes

| Topic | Practice |
|-------|----------|
| Backups | `pg_dump` + copy `/opt/schnapp/data/blobs` |
| Updates | Build upgrade kit (`build-server-deployment.sh`), copy `artifacts/upgrade/server/YYYYMMDD` to host, `sudo ./upgrade-server.sh` |
| Disk | Monitor blob volume growth |
| Secrets | `/opt/schnapp/config/*` mode `640`, owned by `schnapp` |
| Admin seed | Only when no users exist; use Web admin for later password resets |

---

## Script index

| Script | Step |
|--------|------|
| `scripts/deployment/host/setup-database.sh` | 2 (also in publish kit `host/`) |
| `scripts/deployment/host/setup-server-dirs.sh` | 3 |
| `scripts/deployment/publish-server.sh` | 4 → `artifacts/staging/server` |
| `scripts/deployment/build-server-deployment.sh` | 4 → `publish/server/YYYYMMDD` + `upgrade/server/YYYYMMDD` |
| `scripts/deployment/host/write-server-config.sh` | 5 (publish kit only) |
| `scripts/deployment/upgrade-server.sh` | Upgrade (in upgrade kit) |
| `scripts/deployment/systemd/*.service` | 6 (install manually / kit) |
