# Development

## Prerequisites

- .NET 10 SDK (+ ASP.NET Core 10 shared framework for non–self-contained runs)
- PostgreSQL 16+ listening locally (default catalog; or set `ConnectionStrings:Main`)
- Optional: Firebird 5 SuperServer for the alternate catalog (`Schnapp:DatabaseProvider=Firebird`)
- For tray capture: X11 session

## Database

```bash
./scripts/dev-db.sh   # creates role + database (peer auth as postgres where applicable)
```

Default app connection (Development):

```
Host=127.0.0.1;Port=5432;Database=schnapp;Username=schnapp;Password=schnapp
```

Migrations live in `src/servers/Schnapp.Data/Migrations/` (PostgreSQL) and `Migrations/Firebird/` (Firebird 5). The engine is `Schnapp:DatabaseProvider` (`PostgreSQL` default, or `Firebird`). Instants are UTC at rest; IANA zones are stored on the user / screenshot rows. Migrations apply on API start by default.

### Hierarchy fix scripts

If customer/project/task NONE / parent links need repair:

- `scripts/fix-cpt-hierarchy.sql` (Postgres)
- `scripts/fix-cpt-hierarchy-sqlite*.sql` / `.sh` (client SQLite ref data)

## Run API + Web

```bash
export SCHNAPP_ADMIN_EMAIL=admin@example.com
export SCHNAPP_ADMIN_PASSWORD='change-me'
./scripts/run-dev.sh
# or two terminals:
dotnet run --project src/servers/Schnapp.Api --urls http://127.0.0.1:5080
dotnet run --project src/servers/Schnapp.Web --urls http://127.0.0.1:5081
```

Self-contained API if shared framework is missing:

```bash
dotnet publish src/servers/Schnapp.Api -c Debug -r linux-x64 --self-contained true -o /tmp/schnapp-api
```

## Run clients

```bash
dotnet run --project src/clients/Schnapp.Client.Cli -- --help
dotnet run --project src/clients/Schnapp.Client.Linux
```

CLI and tray share `~/.config/schnapp` + `~/.local/share/schnapp`.

### Idle scan and blob re-encode

`schnapp-utils` compares idle-detection methods. Default source is the local spool. Server blobs (mounted kit root `/srv/schnapp` on this workstation) use the catalog:

```bash
dotnet run --project src/clients/Schnapp.Utils -p:Platform=x64 -- --help
dotnet run --project src/clients/Schnapp.Utils -p:Platform=x64 -- reencode --dry-run --limit 5
dotnet run --project src/clients/Schnapp.Utils -p:Platform=x64 -- reencode --jobs 4
dotnet run --project src/clients/Schnapp.Utils -p:Platform=x64 -- thumbs --dry-run --limit 5
dotnet run --project src/clients/Schnapp.Utils -p:Platform=x64 -- thumbs --jobs 4 --progress
dotnet run --project src/clients/Schnapp.Utils -p:Platform=x64 -- --blobs --monitor '*' --limit 200
```

`--root` defaults to `/srv/schnapp`. Connection comes from `--connection`, `SCHNAPP_CONNECTION`, or `{root}/api/appsettings.json`. That file often points at `127.0.0.1`, which is this machine — use the production catalog host if the mount is files only.

### Windows tray (WinForms net48)

Build and run on a **Windows** machine (shared project folder / VM) with .NET Framework 4.8 + .NET 10 SDK:

```powershell
dotnet build src/clients/Schnapp.Client.Windows/Schnapp.Client.Windows.csproj -c Debug
dotnet run --project src/clients/Schnapp.Client.Windows
```

Project is **x64** (SQLite native). Forms are VS-designer friendly (`*.Designer.cs` / `*.resx`). Config: `%AppData%\Schnapp\`, data: `%LocalAppData%\Schnapp\`.

## Tests

```bash
./scripts/test.sh           # full suite
./scripts/test.sh unit      # no Postgres required for Domain + Client.Core unit tests
./scripts/test.sh data
./scripts/test.sh api
```

API tests use Testcontainers or `SCHNAPP_TEST_CONNECTION` and create temporary databases (`schnapp_t_*`). Cleanup helper: `scripts/cleanup-test-dbs.sh`.

Firebird data/API tests use Testcontainers (`firebirdsql/firebird:5.0.2`) or `SCHNAPP_TEST_FIREBIRD_CONNECTION` (FirebirdClient string, `Charset=UTF8;Dialect=3`).

```bash
dotnet test --project tests/Schnapp.Api.Tests -p:Platform=x64 --filter 'FullyQualifiedName~ScreenshotEndpoint'
dotnet test --project tests/Schnapp.Client.Core.Tests -p:Platform=x64 --filter 'FullyQualifiedName~ScreenshotSpool'
```

## Solutions

| File | Contents |
|------|----------|
| `src/Schnapp.slnx` | Full tree |
| `src/Schnapp.Servers.slnx` | API/Web/Domain/Data |
| `src/clients/Schnapp.Clients.slnx` | Client projects |
| `src/Schnapp.Tests.slnx` | Test projects |

## XML documentation

Prefer **`/// <summary>`** on every public type. Document non-obvious public methods with summary (and params/returns when helpful). Keep comments accurate when behavior changes (especially capture/spool and screenshot upload id rules).

Generate XML doc output only if a project enables `GenerateDocumentationFile` (optional).

## Style notes (Web)

- `src/servers/Schnapp.Web/style.md`, `layout.md` — UI conventions for the day grid and dialogs

## Deployment

| Target | Entry point |
|--------|-------------|
| **Server (Fedora)** | [deployment-fedora.md](deployment-fedora.md) — kits under `artifacts/{staging,publish,upgrade}/server` |
| **QNAP Alpine appliance** | [deployment-qnap-appliance.md](deployment-qnap-appliance.md) — `scripts/appliance/build-appliance.sh` → `/opt/schnapp/appliance/YYYYMMDD` (qcow2 as `.img`) |
| **Linux tray client** | `scripts/deployment/build-linux-client-deployment.sh` (kit scripts in `clients/`) |
| **Windows tray client** | `scripts/deployment/build-windows-client-deployment.ps1` (on Windows; kit scripts in `clients/`) |
| **Windows server** | [deployment-windows.md](deployment-windows.md) — `scripts/deployment/build-windows-server-deployment.ps1`; kits under `artifacts/{staging,publish,upgrade}/server-windows` |

Artifact layout (every component):

| Path | Purpose |
|------|---------|
| `artifacts/staging/<name>/` | Raw `dotnet publish` output (overwritten each build) |
| `artifacts/publish/<name>/YYYYMMDD/` | Install kit: binaries + deploy/config scripts |
| `artifacts/upgrade/<name>/YYYYMMDD/` | Upgrade kit: binaries + upgrade scripts only |

```bash
./scripts/deployment/build-server-deployment.sh
./scripts/deployment/build-linux-client-deployment.sh
# staging only (server): ./scripts/deployment/publish-server.sh
```
