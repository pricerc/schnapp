# Clients

Shared logic lives in **`Schnapp.Client.Core`**. UI capture is **`Schnapp.Client.Linux`** (Avalonia tray) and **`Schnapp.Client.Windows`** (WinForms net48 tray). Scripting is **`Schnapp.Client.Cli`**.

## Directories

| Kind | Linux | Windows |
|------|--------|---------|
| **Config** (settings, token) | `~/.config/schnapp/` | `%AppData%\Schnapp\` |
| **Data** (spool, DB) | `~/.local/share/schnapp/` | `%LocalAppData%\Schnapp\` |

Respects `XDG_CONFIG_HOME` / `XDG_DATA_HOME` on Linux.

| Path | Purpose |
|------|---------|
| `settings.json` | API URL, identity, machine, timezone, capture prefs, path overrides |
| `token` | Opaque API bearer token |
| `spool/{uuid7}/` | Per-item `meta.json`, `upload.json`, `image.bin` |
| `schnapp.db` | SQLite: spool index, `ref_*` cache, offline `mutation` queue |

Overrides: `settings.json` (`spoolDirectory`, …) or env such as `SCHNAPP_SPOOL_DIR` / `SCHNAPP_TOKEN`.

### Pre-configure Windows client (API / web URL)

The tray client only needs **`apiBaseUrl`** (Schnapp API). The browser UI is separate.

```powershell
# One-shot install against a QNAP appliance
.\deploy-windows-client.ps1 `
  -ApiBaseUrl "http://10.10.10.55:5080" `
  -WebUrl "https://10.10.10.55/"

# Or ship client-defaults.json in the kit:
# { "apiBaseUrl": "http://10.10.10.55:5080", "webUrl": "https://10.10.10.55/" }
```

Writes/merges `%AppData%\Schnapp\settings.json`. Optional Start menu **Schnapp Web** shortcut uses `-WebUrl` (browser only).

## Capture pipeline (tray)

```
timer / Capture now
    → grab frames (X11 native preferred on Linux; GDI on Windows)
    → encode (Linux ImageSharp: JPEG/PNG/WebP; Windows System.Drawing JPEG/PNG, Imazen.WebP for WebP)
    → ScreenshotSpool.Enqueue  (always; sets metadata.Id = spool id)
    → TryBackgroundPush / uploads loop
         → UploadService.PushAsync (mutations first)
         → ScreenshotSpool.PushAsync(retainUploaded: true)
```

### Capture targets (Settings)

| Target | Default | Behavior |
|--------|---------|----------|
| Full desktop | off | One image of the root / virtual desktop |
| Monitors | on | Separate image per monitor (optional subset) |
| Active window | off | Focused window (Linux `_NET_ACTIVE_WINDOW`; Windows foreground window) |

Also: interval (min 15s), skip when screensaver is on (timer only), pause after idle minutes (timer only; 0 = off; keyboard/mouse inactivity), per-monitor quality/scale overrides.

### Output scale steps (`MonitorScaling`)

Both trays share the same scale-step algorithm (duplicated under each client’s `Capture/MonitorScaling.cs`):

1. **GCD path** — For width×height with `g = gcd(w,h) > 1`, generate integer ratios `k/g` in `[0.25, 1.0]`. Pixel sizes stay multiples of the reduced base tile. Windows also drops steps whose reduced fraction denominator is &gt; 32 (noisy UI).
2. **Percent fallback** — If `g ≤ 1` (coprime / odd VM modes) or fewer than two GCD steps remain, use **5% increments from 25% through 100%**, rounding output pixels from full resolution.

**Order:** always **ascending** (smallest first). No list reverse in the algorithm.

| Control | Left | Right |
|---------|------|--------|
| Windows trackbar | index 0 ≈ 25% | max index = 100% |
| Linux Avalonia slider | `Minimum=25` | `Maximum=100` |

### Journals on the client

- Open journal on the default machine; optional customer/project/task (NONE allowed).
- Active open journal id is attached to each capture as **linked** `JournalEntryId`.
- Offline journal/list changes go through the **mutation** queue and are pushed with the upload loop.
- Reference lists (customers, projects, tasks, journal targets) are cached in SQLite via `ReferenceDataCache` / `UploadService.RefreshReferenceDataAsync` for offline menus and autocomplete.

### Offline queue window

Browse pending + cached spool items by day/batch; delete local items; push is available from the tray. Windows includes a **Notes** column (journal notes, else screenshot metadata notes) with fixed columns declared in the designer; capture-kind columns remain dynamic.

## Spool semantics

| Status | Meaning |
|--------|---------|
| Pending | Not yet successfully uploaded (or not acknowledged) |
| Uploaded | API accepted; kept as recent cache until byte budget eviction |

**Pending caps** (count/bytes/age) only drop *pending* items — never uploaded cache. Cache eviction is separate (`ScreenshotCacheHousekeeping` / max cache bytes).

Push sends `metadata.Id = spool_item.id`. Response id is stored as `server_screenshot_id` (see [architecture.md](architecture.md)).

## CLI overview

```bash
dotnet run --project src/clients/Schnapp.Client.Cli -- --help

schnapp login <email|code|name>
schnapp machine register | find | use
schnapp journal start | stop | list
schnapp upload <file> [--at TIME] [--tags …] [--monitor …]
schnapp spool status | push
```

CLI **does not capture**; use `upload` for files. Login/machine state is shared with the tray.

Smoke script: `./scripts/test-cli.sh`.

Identity for login: email, unique **code**, or unique **name**. Password optional for passwordless accounts.

`config set timezone` affects `--at` parsing and `ClientTimeZoneId` on uploads.

## Linux tray UI (summary)

- Tray icon states: running / paused / stopped
- Status dialog, main window (journal + capture), settings, machines, login, offline queue
- **Push offline spool** forces a mutation + screenshot push (retains uploaded cache)
- Journal quick menu filters project/task by pending customer/project; headers show current values

Build/run:

```bash
dotnet run --project src/clients/Schnapp.Client.Linux
# binary name: schnapp-client-linux / schnapp-tray depending on publish profile
```

Deployment helpers: `scripts/deployment/build-linux-client-deployment.sh` → `artifacts/{staging,publish,upgrade}/linux-client` (kit scripts from `scripts/deployment/clients/`).

## Windows tray UI (summary)

- **Stack:** .NET Framework **4.8** WinForms, GDI capture, System.Drawing JPEG/PNG encode, WebP via NuGet **`Imazen.WebP`** + **`Imazen.WebP.NativeRuntime.win-x64`**
- **Tray:** `NotifyIcon` + designer-hosted `ContextMenuStrip` on hidden `TrayShellForm` (VS designer pattern); menu icons from `Assets/menu/`
- **Tooltip:** status short label plus open journal **codes** (not full names); 63-character WinForms limit. Journal target is loaded **before** the first tooltip update at startup (`await TryRefreshAsync`).
- **Journal menus:** Customer / Project / Task flyouts filter by open journal parent; blank project is a real filter (tasks with no project). Flyouts check the current selection; parent labels show `Label (CODE)`.
- **Main window:** `ContainsFilterComboBox` provides substring (contains) suggestions from the local ref cache for customer/project/task/MRU.
- **Status menu item:** opens an **About** dialog (`StatusDetailForm`) — product/version/paths first, current tray status last.
- Same feature set as Linux: login, machines, settings (per-monitor + full-desktop scale), journal main window, offline queue with image preview, pause capture/uploads, push spool
- Forms use Visual Studio designer triads: `Form.cs` + `Form.Designer.cs` + `Form.resx`
- Paths: `%AppData%\Schnapp\` (config/token), `%LocalAppData%\Schnapp\` (spool/db/logs)
- Logs: `%LocalAppData%\Schnapp\logs\` (UI + unhandled exceptions); process-level handlers in `Program.cs`
- Platform: **x64** (required by SQLite native package)
- Pickup notes: `src/clients/Schnapp.Client.Windows/PLAN.md`

Build/run (**on Windows** with .NET 10 SDK + .NET Framework 4.8 targeting pack):

```powershell
dotnet build src/clients/Schnapp.Client.Windows/Schnapp.Client.Windows.csproj -c Debug
dotnet run --project src/clients/Schnapp.Client.Windows
# binary: schnapp-client-windows.exe
```

Open the project in **Visual Studio** on the Windows VM to edit form layouts in the designer.

Requires **.NET Framework 4.8** on the target machine (preinstalled on modern Windows). xcopy the publish output; no extra runtime for the framework-dependent layout.

## Documentation conventions (client code)

- Prefer **XML docs** (`///`) on public, internal, and private methods/properties that carry behavior.
- Non-trivial algorithms (especially scale math, spool push, tray filtering) should include **inline `// Because …` comments** that answer *why* a branch exists, not only *what* the next line does.
- Keep designer-generated `InitializeComponent` free of noise; document the partial class and hand-written logic instead.
