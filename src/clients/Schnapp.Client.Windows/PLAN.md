# Schnapp Windows Client — Plan & Pickup Notes

> Living notes for **Phase 10** work on this project. Open this file on the Windows VM to continue.
> Product/tech locks: `docs/goals.md`, `docs/mvp-tech-plan.md` (Phase 10), `docs/client.md`.
> Linux reference: `src/clients/Schnapp.Client.Linux/`.

**Last updated:** 2026-07-26 (session: exception handlers, menu icons, preview, monitor settings, deploy smoke)

---

## Goal

Tray client on **.NET Framework 4.8 + WinForms**, reusing **`Schnapp.Client.Core`** (netstandard2.0), approximating Linux UX, **xcopy** install (no extra runtime beyond OS .NET 4.8).

| Constraint | Decision |
|------------|----------|
| TFM / UI | **net48 + WinForms only** |
| Capture | **GDI first**; DXGI later if needed |
| Encode | **System.Drawing** JPEG/PNG; **WebP** via NuGet `Imazen.WebP` + `Imazen.WebP.NativeRuntime.win-x64` |
| Extra NuGet on this project | Prefer **minimal** — WebP packages above only (beyond Core transitive deps) |
| Paths | `%AppData%\Schnapp\` config · `%LocalAppData%\Schnapp\` data |
| Platform | **x64** (SQLite native package rejects AnyCPU) |
| Forms | **VS designer triads**: `Form.cs` + `Form.Designer.cs` + `Form.resx` |

---

## Build / run (Windows VM)

```powershell
# From repo root
dotnet build src/clients/Schnapp.Client.Windows/Schnapp.Client.Windows.csproj -c Debug
dotnet run --project src/clients/Schnapp.Client.Windows -- --verbose

# xcopy kit
.\scripts\deployment\build-windows-client-deployment.ps1
```

- Requires: .NET Framework 4.8 + .NET 10 SDK (or VS with targeting pack).
- Open `.csproj` in **Visual Studio** for Design view on forms.
- Binary: `schnapp-client-windows.exe`
- Logs: `%LocalAppData%\Schnapp\logs\schnapp-YYYY-MM-DD.log`

---

## Architecture (as implemented)

```
TrayShellForm (hidden host, Application.Run)
  ├── NotifyIcon + ContextMenuStrip (designer)
  ├── CaptureService → GdiScreenCapture + ImageEncoder → ScreenshotSpool
  ├── JournalSessionService → Core mutations / ref cache
  └── Dialogs: Login, Machines, Settings, Main, OfflineQueue, Status, TextPrompt
           └── Schnapp.Client.Core → HTTP API
```

**Do not** put ImageSharp, Avalonia, or Registry fingerprinting into Core. Windows fingerprint: `Machine/WindowsMachineIdentity.cs` (MachineGuid).

---

## Style conventions (locked during implementation)

### Designer forms

- Every interactive Form/UserControl: **code-behind + Designer.cs + resx**.
- Static chrome in designer; dynamic lists/menus filled in code.
- No business logic inside `InitializeComponent`.
- Tray uses pattern **A**: hidden host form owns designer `NotifyIcon` + menu (`TrayShellForm`).

### Form lifecycle — overrides, not `+=`

Prefer:

```csharp
protected override void OnLoad(EventArgs e)
{
    base.OnLoad(e);
    // ...
}

protected override void OnFormClosing(FormClosingEventArgs e)
{
    // ...
    base.OnFormClosing(e);
}
```

**Do not** use `Load +=` / `FormClosing +=` for form lifecycle (style; and for the shell, wrong show-suppression can prevent Load entirely).

Fine as events: designer `Click`, tray menu rebuild, `CaptureService.StatusChanged`, `JournalSessionService.Changed`.

### Tray shell visibility

- `Application.Run` must be allowed to show the form once so **OnLoad** runs.
- Never force `SetVisibleCore(false)` on every call — that **skips Load**.
- After load: `Hide()` + opacity/taskbar tricks when not debugging.
- With debugger attached, shell may stay visible for inspection (`_showShellForDebug`).

---

## Status: done (MVP path working)

Verified on Windows VM: **app starts, captures, spool enqueues, upload succeeds**.

| Area | Status |
|------|--------|
| Project scaffold, x64, solutions (`Schnapp.Clients.slnx`, `Schnapp.slnx`) | Done |
| `TrayShellForm` + tray menu structure (Linux-like order) | Done |
| GDI capture (monitors / full desktop / active window) | Done |
| JPEG/PNG/WebP encode + scale / max-edge (Imazen.WebP NuGet) | Done |
| `CaptureService` (timer, pause, uploads pause, spool, push) | Done |
| Screensaver / lock skip (`WinScreensaverDetector`) | Done |
| Login / Machines / Settings / Main / Offline queue / Status / TextPrompt | Done |
| Journal session + tray Customer/Project/Task / MRU | Done |
| `WindowsMachineIdentity` (MachineGuid) | Done |
| Lifecycle as `OnLoad` / `OnFormClosing` overrides | Done |
| Deploy script `scripts/deployment/build-windows-client-deployment.ps1` | Done |
| Docs: `docs/client.md`, `development.md`, `architecture.md` (partial) | Done |

### Layout (source)

```
Schnapp.Client.Windows/
  Program.cs
  app.manifest                    # Per-Monitor V2 DPI
  PLAN.md                         # this file
  Capture/   Gdi*, interfaces, geometry, screensaver
  Encode/    ImageEncoder.cs
  Forms/     *Form + Designer + resx, TrayShellForm, WindowPlacement
  Machine/   WindowsMachineIdentity.cs
  Services/  CaptureService, JournalSessionService, AppLog, TrayStatus
  Assets/    app + tray icons, menu/*.png (copied from Linux; not all wired into menus)
```

---

## Deferred / incomplete (pick up here)

Priority is informal — adjust as needed on the VM.

### P1 — Stability & polish

1. **Unexpected process exit** — **Done (logging + audit)**  
   - `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`, process exit log in `Program.cs`.  
   - Quit / startup failure use deferred shell `Close()` (`RequestExit`) — not `Application.Exit` from menu click (that races menu dispose). MainForm X still hides.  
   - Capture/upload loops already catch; unobserved tasks logged + observed.  
   - If exit still happens: check `%LocalAppData%\Schnapp\logs\` for the stack.

2. **Unhandled exception UX** — **Done**  
   - UI thread: MessageBox + log (full exception via `AppLog.Error(msg, ex)`).  
   - Fatal terminating: MessageBox once + log.

3. **VS designer pass**  
   Open each form in Design view on the VM; fix layout/anchors; let VS refresh resx if needed.  
   Confirm `TrayShellForm` / new `ImagePreviewForm` / `MonitorSettingsRow` remain editable.

4. **Menu / tray icons** — **Done**  
   - `Services/MenuIcons.cs` loads `Assets/menu/*.png`; static + dynamic (pause/play, MRU, add/none).  
   - Tray running/paused/stopped on all status paths.

### P2 — Linux parity gaps

5. **`ImagePreviewForm`** — **Done**  
   - Preview button + double-click; zoom in/out/100%/fit.

6. **`MonitorSettingsRow` UserControl** — **Done**  
   - Per-monitor capture / discrete scale TrackBar / quality override.  
   - Scales from `Capture/MonitorScaling.cs` (Linux port); tick index → `Dictionary` (WinForms TrackBar is integer-only).  
   - GDI geometry `DetectedScale` always 1; user scale only. No Xlibre override UI.

7. **Desktop scale UI** — **Done**  
   - Desktop row under Full desktop checkbox (`DesktopSettings` output % from TrackBar).

8. **Offline queue richness** — **Done (gallery)**  
   - Day filter + batch rows (same-second groups).  
   - Thumbnail columns per window/desktop/monitor.  
   - Pending mutations count in summary.  
   - Double-click thumbnail / Preview button.

9. **MainForm UX polish**  
   Placeholders for tags/notes; filter Project/Task combos when Customer changes (Linux filters more tightly in UI); tooltips matching Linux.

10. **Tray journal menus**  
    Cap list length (done ~40); optional icons; ensure Opening rebuild doesn’t thrash; match Linux “immediate apply” semantics (largely done).

### P3 — Capture / encode stretch

11. **DXGI Desktop Duplication** (`Capture/DxgiScreenCapture.cs`)  
    Optional behind `IScreenCapture`; fallback to GDI. Not MVP-blocking if GDI is good enough.

12. **DPI accuracy**  
    Current scale uses rough Graphics DPI, not true per-monitor `GetDpiForMonitor`. Improve multi-monitor mixed-DPI.

13. **WebP**  
    Explicitly out unless a single native DLL is accepted later. Settings already JPEG/PNG only; WebP in settings falls back to JPEG in encoder if ever selected via raw JSON.

14. **Active window quality**  
    `PrintWindow` + BitBlt fallback — edge cases (elevated UAC, exclusive fullscreen, some Electron apps). Document; improve only if users hit it.

### P4 — Deploy / docs / ops

15. **Publish script smoke on VM** — **Done (2026-07-26)**  
    `build-windows-client-deployment.ps1` produced zip with exe, `e_sqlite3.dll`, Assets, updated INSTALL.txt.

16. **README root blurb**  
    Short Windows xcopy section if not already complete.

17. **Single-instance mutex** — **Done**  
    MessageBox when second instance starts (`Local\SchnappClientWindows`).

18. **Startup with Windows**  
    Out of scope (Startup folder shortcut later).

19. **Code signing / MSI**  
    Out of scope.

### P5 — Explicit non-goals (this phase)

- Shared UI with Avalonia  
- Automated WinForms UI tests  
- Offline list conflict policy (global TBD)  
- Auto-start, MSI, DXGI as hard requirement  

---

## Manual validation checklist (Windows)

Use after each meaningful change:

- [ ] First run → Login → Machines register/use  
- [ ] Capture now → gallery shows shot for that user only  
- [ ] Interval capture; pause/resume capture; pause/resume uploads  
- [ ] Journal Apply with C/P/T; captures get linked journal id  
- [ ] Kill API → spool grows; restore → push drains  
- [ ] Offline create customer from tray → push → Web lists  
- [ ] Multi-monitor (if available): per-monitor images  
- [ ] Active window + full desktop targets  
- [ ] Offline queue: list, delete, push  
- [ ] Restart: token, machine, CaptureRunning restored  
- [ ] Main window X hides app; tray remains; Quit exits  
- [ ] Process stays up under idle + several capture ticks (watch for spontaneous exit)

---

## Known pitfalls

| Pitfall | Mitigation |
|---------|------------|
| SQLite “does not support Any CPU” | Project forces `Platform=x64` / `PlatformTarget=x64` |
| `SetVisibleCore(false)` always | Kills `OnLoad`; do not reintroduce |
| `async void OnLoad` | Exceptions need try/catch (TrayShellForm has try/catch + Exit) |
| Background task exceptions | Log + consider TaskScheduler handler |
| Core package versions | If restore fails on net48, fix in Core — don’t fork APIs |
| RDP / multi-session | Per-user profile config; one process per session expected |

---

## Suggested next session

1. Manual validation checklist (especially multi-monitor scale + preview + idle stability / log after any exit).  
2. VS designer pass on new forms (`ImagePreviewForm`, `MonitorSettingsRow`, enlarged Settings).  
3. Offline queue day/batch grouping (remaining P2).  
4. MainForm UX polish (tags/notes placeholders, tighter C/P/T filters).  
5. Optional: better DPI via `GetDpiForMonitor` (P3).

---

## Reference files

| Path | Why |
|------|-----|
| `Forms/TrayShellForm.cs` | Lifecycle, tray, bootstrap |
| `Services/CaptureService.cs` | Capture timer / spool / push |
| `Capture/GdiScreenCapture.cs` | GDI grab |
| `Encode/ImageEncoder.cs` | JPEG/PNG |
| `src/clients/Schnapp.Client.Linux/` | UX/feature parity source |
| `docs/mvp-tech-plan.md` § Phase 10 | Locked MVP wording |
| `scripts/deployment/build-windows-client-deployment.ps1` | xcopy zip |
