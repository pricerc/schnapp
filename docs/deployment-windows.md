# Deploying Schnapp on a Windows laptop (API + Web + Firebird)

This is the server kit for a Windows laptop that also runs the Schnapp tray client. The catalog and screenshot files stay on the laptop. The NAS only receives the backup it already takes.

The API and Web listen on **all interfaces** (`0.0.0.0`) by default, so a VM guest on this laptop can call them. Firebird stays on `127.0.0.1`. There is no TLS.

| Step | What | How |
|------|------|-----|
| **1** | Build a kit | `build-windows-server-deployment.ps1` |
| **2** | Firebird 5 SuperServer | Windows installer |
| **3** | API, Web, catalog, firewall | `deploy-windows-server.ps1` from the publish kit |
| **4** | Consistent catalog backup | `backup-firebird.ps1` |
| — | Upgrade binaries | `upgrade-windows-server.ps1` from the upgrade kit |

PostgreSQL is supported as config only (`-Provider PostgreSQL -SkipDatabase`). There is no Windows PostgreSQL setup script. There is no conversion between catalogs.

---

## Layout

| Path | Purpose |
|------|---------|
| `C:\Program Files\Schnapp\` | Tray client. This kit does not replace that folder. |
| `C:\Program Files\Schnapp\Server\api` | SchnappApi service |
| `C:\Program Files\Schnapp\Server\web` | SchnappWeb service |
| `C:\Program Files\Schnapp\Server\utils` | `schnapp-utils` |
| `C:\Program Files\Schnapp\Server\scripts` | Firebird setup, config writer, gbak (copied by deploy) |
| `%ProgramData%\Schnapp\config` | `api.env`, `web.env`, Production json. Passwords. Administrators and SYSTEM only. |
| `%ProgramData%\Schnapp\firebird\schnapp.fdb` | Firebird catalog. Created once, `PAGE_SIZE 16384`, UTF8. |
| `%ProgramData%\Schnapp\blobs` | Screenshot files (`Schnapp:BlobRoot`) |
| `%ProgramData%\Schnapp\backup` | Default folder for `gbak` output |

Services run as LocalSystem. A `-BlobRoot` or `-BackupDir` under a user profile has to allow SYSTEM to write.

---

## 1. Build the kit

On a machine with the .NET 10 SDK (Windows, or another OS that can `dotnet publish -r win-x64`):

```powershell
.\scripts\deployment\build-windows-server-deployment.ps1
.\scripts\deployment\build-windows-server-deployment.ps1 -SelfContained
.\scripts\deployment\build-windows-server-deployment.ps1 -Zip
```

| Path | Purpose |
|------|---------|
| `artifacts/staging/server-windows/` | Raw publish (overwritten each build) |
| `artifacts/publish/server-windows/YYYYMMDD/` | Install kit (binaries, scripts, this doc as `README-DEPLOY.md`) |
| `artifacts/upgrade/server-windows/YYYYMMDD/` | Binaries and `upgrade-windows-server.ps1` only |

`YYYYMMDD` is UTC. A framework-dependent kit still needs the ASP.NET Core 10 **desktop runtime** (the Windows apphost). `-SelfContained` bundles the runtime. Both layouts publish `Schnapp.Api.exe` and `Schnapp.Web.exe`.

---

## 2. Firebird 5 SuperServer

Install Firebird 5 **SuperServer** (not Embedded). The Windows installer default SYSDBA password is `masterkey`; change it and pass `-SysdbaPassword` to deploy.

`setup-database-firebird.ps1` (called by deploy) creates the user and an empty `schnapp.fdb` with `PAGE_SIZE 16384` and `DEFAULT CHARACTER SET UTF8`. The API does not create that file. Page size is fixed for the life of the file; a 4 KB database cannot store Schnapp's UTF8 unique indexes.

If `firebird.conf` does not already set `RemoteBindAddress`, the script sets it to `127.0.0.1` and restarts the Firebird service. Port 3050 is not given a firewall rule. Pass `-AllowRemoteFirebird` to leave `firebird.conf` alone.

The API connection string is:

```text
DataSource=127.0.0.1;Port=3050;Database=C:\ProgramData\Schnapp\firebird\schnapp.fdb;User=schnapp;Password=...;Charset=UTF8;Dialect=3
```

The database password is required (`-DbPassword` or `SCHNAPP_DB_PASSWORD`). Do not use a password that contains `;`. That character splits the connection string.

---

## 3. Install API and Web

From an unpacked publish kit. The script asks for elevation. Passwords go through a short-lived file in `%TEMP%` for that relaunch, not on the elevated command line.

```powershell
.\deploy-windows-server.ps1 `
  -DbPassword '...' `
  -AdminEmail 'you@example.com' `
  -AdminPassword '...'
```

`-AdminEmail` and `-AdminPassword` create the first user only when the catalog has no users yet.

Defaults:

| Setting | Default |
|---------|---------|
| API listen | `http://0.0.0.0:5080` |
| Web listen | `http://0.0.0.0:5081` |
| Web → API | `http://127.0.0.1:5080` (`Schnapp:ApiBaseUrl`) |

`http://0.0.0.0` is a listen address. The Web process does not call it. When the API bind includes `127.0.0.1`, `localhost`, `0.0.0.0`, `*`, or `::`, the BFF uses `http://127.0.0.1:<api port>`.

This laptop:

- Web: `http://127.0.0.1:5081/Login`
- Health: `http://127.0.0.1:5080/api/v1/health`

A VM guest uses the host address on the virtual switch, not the guest's `127.0.0.1`. Deploy prints the host's IPv4 addresses. The tray inside a guest stores that API URL as `apiBaseUrl`. A browser in the guest opens `http://<host>:5081/Login`.

Keep both processes on the laptop only:

```powershell
.\deploy-windows-server.ps1 -DbPassword '...' -ApiBind 127.0.0.1 -WebBind 127.0.0.1
```

Listen on localhost and one host address (comma-separated), or pass full URL lists:

```powershell
-ApiBind '127.0.0.1,192.168.137.1' -WebBind '127.0.0.1,192.168.137.1'
-ApiUrls 'http://127.0.0.1:5080;http://192.168.137.1:5080'
```

### Firewall

When a listen address is not loopback-only, deploy adds two kinds of inbound TCP allow rules:

- **Ordinary adapters:** profile `Private,Domain` (override with `-FirewallProfile`). A Public network, such as cafe Wi-Fi, is not included.
- **Virtual adapters** (`vEthernet*`, or a description containing `Hyper-V` or `Virtual`): the same ports on profile Any. The Hyper-V Default Switch is often classified Public, and its address changes, so the API stays on `0.0.0.0` and the rule follows the adapter.

`-NoFirewall` skips both. Anything that can open the port can call the API. There is no TLS in this kit.

### Services

| Service | Binary | Depends on |
|---------|--------|------------|
| `SchnappApi` | `Schnapp.Api.exe` | the first `FirebirdServer*` service, when it exists |
| `SchnappWeb` | `Schnapp.Web.exe` | `SchnappApi` |

`UseWindowsService()` sets the content root to the install directory (a service's working directory would otherwise be `C:\Windows\System32`) and forwards stop signals to Kestrel. The same call does nothing when the process is not a Windows service.

Environment for each service comes from `%ProgramData%\Schnapp\config\api.env` and `web.env`. Deploy also copies `appsettings.Production.json` next to each binary. Running deploy again recreates the services. It leaves an existing config in place unless `-ForceConfig` is set.

`-NoStart` installs without starting.

---

## 4. Backup

A copy of the live `.fdb` can tear. `gbak` runs while the API is up.

```powershell
& 'C:\Program Files\Schnapp\Server\scripts\backup-firebird.ps1' `
  -BackupDir 'D:\Backups\Schnapp' `
  -RegisterDaily
```

That registers scheduled task **Schnapp Firebird backup**: daily at 02:15, as SYSTEM, keeping 14 `schnapp-*.fbk` files. The task reads the database password from `api.env`. `-BackupDir` has to be a folder SYSTEM can write and the NAS backup already copies.

Point that backup at:

- `%ProgramData%\Schnapp\blobs` (or the `-BlobRoot` you set)
- the `gbak` folder

Config files under `%ProgramData%\Schnapp\config` hold the database password and are not readable by a normal user process. A backup agent that runs as SYSTEM can read them. Prefer backing up `gbak` output rather than those files.

---

## Upgrade

Unpack `artifacts/upgrade/server-windows/YYYYMMDD` and run `upgrade-windows-server.ps1`. It replaces `api\`, `web\`, and `utils\`. It does not change `%ProgramData%\Schnapp` or `appsettings.Production.json`.

---

## PostgreSQL

Create the database and role yourself, then:

```powershell
.\deploy-windows-server.ps1 -Provider PostgreSQL -SkipDatabase -DbPassword '...' -DbPort 5432
```

`backup-firebird.ps1` refuses a config whose provider is not Firebird.
