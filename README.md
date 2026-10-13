# Schnapp

Multi-user **screenshot capture and review** for a single organization: tray (and CLI) clients capture screens with journal context, upload to a C# API (PostgreSQL metadata + filesystem image blobs), and users review their own days in a Razor web UI.

## What it does

| Piece | Role |
|--------|------|
| **Linux tray client** | Interval / manual capture (monitors, full desktop, active window), journals, offline spool, local recent cache |
| **CLI** | Login, machine register/use, journals, file upload, spool push/status (no capture) |
| **API** | Auth tokens, machines, screenshots, journals, customer/project/task lists, admin |
| **Web** | BFF over the API: day timeline, shot viewer, journals, lists, admin |

Clients **always enqueue** captures to a local spool first; a background loop uploads pending items and keeps them as a size-capped local cache. Spool item ids are sent as the preferred server screenshot id (idempotent retries).

## Prerequisites

- **.NET 10 SDK**
- **ASP.NET Core 10 runtime** (or self-contained publish)
- **PostgreSQL 16+** for the API
- Linux tray: **X11** session (native capture); optional ImageMagick `import` / `scrot` / `gnome-screenshot` fallbacks

## Quick start (dev)

```bash
./scripts/dev-db.sh
export SCHNAPP_ADMIN_EMAIL=admin@example.com
export SCHNAPP_ADMIN_PASSWORD='change-me'
./scripts/run-dev.sh
```

| Service | Default URL |
|---------|-------------|
| API health | http://127.0.0.1:5080/api/v1/health |
| Web login | http://127.0.0.1:5081/Login |

Admin is seeded only when the database has **no users** and admin email/password are set via env or `Schnapp:Admin*` config.

Connection string (dev default):

`Host=127.0.0.1;Port=5432;Database=schnapp;Username=schnapp;Password=schnapp`

### Clients

```bash
# CLI (paths under ~/.config/schnapp and ~/.local/share/schnapp)
dotnet run --project src/clients/Schnapp.Client.Cli -- login admin@example.com
dotnet run --project src/clients/Schnapp.Client.Cli -- machine register
dotnet run --project src/clients/Schnapp.Client.Cli -- upload ./scripts/test-image.png --at 13:15

# Tray (shares config/token/machine with CLI)
dotnet run --project src/clients/Schnapp.Client.Linux
```

## Tests

```bash
./scripts/test.sh              # all
./scripts/test.sh unit         # domain + client.core (no DB)
./scripts/test.sh data         # migrations
./scripts/test.sh api          # API integration (Postgres)
./scripts/test.sh --filter 'FullyQualifiedName~Health'
```

Integration tests use `SCHNAPP_TEST_CONNECTION` (defaults to local `schnapp`/`schnapp` on `127.0.0.1:5432`) and create isolated databases.

## Repository layout

```
src/
  clients/
    Schnapp.Client.Core/     Shared client: settings, API client, spool, mutations, ref cache
    Schnapp.Client.Cli/      CLI (schnapp)
    Schnapp.Client.Linux/    Avalonia tray + capture
  common/
    Schnapp.Contracts/       Shared DTOs + Uuid7 JSON helpers
  servers/
    Schnapp.Api/             REST API
    Schnapp.Web/             Razor Pages BFF
    Schnapp.Domain/          Entities, roles, settings keys
    Schnapp.Data/            Dapper, DbUp migrations, blob store
tests/                       xUnit (Api, Client.Core, Data, Domain)
docs/                        Architecture and product docs
scripts/                     Dev DB, run-dev, tests, deployment helpers
```

## Documentation

| Doc | Contents |
|-----|----------|
| [docs/architecture.md](docs/architecture.md) | System design, data flow, ids, offline model |
| [docs/client.md](docs/client.md) | Tray, CLI, spool, config paths |
| [docs/server.md](docs/server.md) | API, Web BFF, storage, auth |
| [docs/development.md](docs/development.md) | Local setup, tests, scripts |
| [docs/deployment-fedora.md](docs/deployment-fedora.md) | Production deploy on Fedora |
| [docs/deployment-qnap-appliance.md](docs/deployment-qnap-appliance.md) | Alpine VM for QNAP (qcow2 as .img) |
| [docs/goals.md](docs/goals.md) | Product goals (intent) |
| [docs/mvp-tech-plan.md](docs/mvp-tech-plan.md) | Locked MVP tech decisions |
| [docs/Archive/](docs/Archive/) | Historical session notes and superseded drafts |

### Build deployment kits (under `artifacts/`)

All build scripts use the same layout:

| Folder | Contents |
|--------|----------|
| `artifacts/staging/<component>/` | Raw build output (overwritten each build) |
| `artifacts/publish/<component>/YYYYMMDD/` | Full install kit (binaries + deploy/config scripts) |
| `artifacts/upgrade/<component>/YYYYMMDD/` | Upgrade kit (binaries + upgrade scripts only; no config overwrite) |

```bash
# Server: staging + publish + upgrade
./scripts/deployment/build-server-deployment.sh
# Linux client:
./scripts/deployment/build-linux-client-deployment.sh
# Windows client (on Windows):
# .\scripts\deployment\build-windows-client-deployment.ps1
```

## Configuration notes

- **Web → API:** `Schnapp:ApiBaseUrl` (Web `appsettings`)
- **Blobs:** `Schnapp:BlobRoot` (API; default under `/opt/schnapp/data/blobs` or as configured)
- **Client data:** config `~/.config/schnapp/`, data `~/.local/share/schnapp/` (`spool/`, `schnapp.db`)
- **Migrations:** applied on API startup when `Schnapp:MigrateOnStartup` is true (default)

## License / status

Internal multi-user MVP. See product goals and tech plan under `docs/` for scope (what is in/out of MVP).
