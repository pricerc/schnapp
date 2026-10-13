# Build Windows tray client kits: staging → publish + upgrade.
#
# Layout (default Artifacts root = <repo>/artifacts):
#   staging/windows-client/              raw dotnet publish
#   publish/windows-client/YYYYMMDD/     full install kit (deploy + INSTALL notes)
#   upgrade/windows-client/YYYYMMDD/     binary upgrade kit
#
# Run on Windows from repo root:
#   .\scripts\deployment\build-windows-client-deployment.ps1
#   .\scripts\deployment\build-windows-client-deployment.ps1 -Configuration Release -OutputDir artifacts
#   .\scripts\deployment\build-windows-client-deployment.ps1 -Zip   # also write dated archives
#
# Kit scripts (copied into publish/upgrade): scripts\deployment\clients\
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "",
    [string]$Project = "src/clients/Schnapp.Client.Windows/Schnapp.Client.Windows.csproj",
    [string]$Stamp = "",
    [switch]$Zip
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "../..")
Set-Location $Root

$ClientsDir = Join-Path $PSScriptRoot "clients"
$DeployScript = Join-Path $ClientsDir "deploy-windows-client.ps1"
$UpgradeScript = Join-Path $ClientsDir "upgrade-windows-client.ps1"
if (-not (Test-Path $DeployScript)) {
    throw "missing deploy script: $DeployScript"
}
if (-not (Test-Path $UpgradeScript)) {
    throw "missing upgrade script: $UpgradeScript"
}

if (-not $OutputDir) { $OutputDir = Join-Path $Root "artifacts" }
if (-not [System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir = Join-Path $Root $OutputDir
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$OutputDir = (Resolve-Path $OutputDir).Path

# UTC calendar day for output folders (rebuild same day overwrites that day only).
if (-not $Stamp) { $Stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd") }

$StagingDir = Join-Path $OutputDir "staging/windows-client"
$PublishDir = Join-Path $OutputDir "publish/windows-client/$Stamp"
$UpgradeDir = Join-Path $OutputDir "upgrade/windows-client/$Stamp"

if (Test-Path $StagingDir) { Remove-Item -Recurse -Force $StagingDir }
New-Item -ItemType Directory -Force -Path $StagingDir | Out-Null

Write-Host "Publishing $Project ($Configuration, x64) -> $StagingDir"
dotnet publish $Project -c $Configuration -p:Platform=x64 -o $StagingDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$KitBase = "schnapp-client-windows-x64-$Stamp"

function Copy-StagingTo {
    param([string]$Dest)
    if (Test-Path $Dest) { Remove-Item -Recurse -Force $Dest }
    New-Item -ItemType Directory -Force -Path $Dest | Out-Null
    Copy-Item -Recurse -Force (Join-Path $StagingDir "*") $Dest
}

# --- publish kit ---
Write-Host "Assembling publish kit → $PublishDir"
Copy-StagingTo $PublishDir
Copy-Item -Force $DeployScript (Join-Path $PublishDir "deploy-windows-client.ps1")
$DefaultsExample = Join-Path $ClientsDir "client-defaults.json.example"
$DefaultsEnvExample = Join-Path $ClientsDir "client-defaults.env.example"
if (Test-Path $DefaultsExample) {
    Copy-Item -Force $DefaultsExample (Join-Path $PublishDir "client-defaults.json.example")
}
if (Test-Path $DefaultsEnvExample) {
    Copy-Item -Force $DefaultsEnvExample (Join-Path $PublishDir "client-defaults.env.example")
}

@"
Schnapp Windows client — publish (install) kit (net48 WinForms)
==============================================================

Requirements
- Windows 10/11 x64
- .NET Framework 4.8 (usually preinstalled)

Install (recommended)
1. Unpack this folder.
2. (Optional) Pre-configure the appliance/server URL — either:
     a) Edit client-defaults.json (copy from client-defaults.json.example), or
     b) Pass parameters when installing (see below).
3. Right-click deploy-windows-client.ps1 → Run with PowerShell
   (or from an elevated PowerShell: .\deploy-windows-client.ps1)
4. Accept the UAC prompt. Files go to C:\Program Files\Schnapp
   and a "Schnapp" shortcut is added to the Start menu.

Pre-configure server (tray client + optional web shortcut)
  The tray app talks to the **API** (not the browser port). On the QNAP appliance
  that is typically http://<host>:5080 while the web UI is http(s)://<host>/.

  .\deploy-windows-client.ps1 ``
      -ApiBaseUrl "http://schnapp:5080" ``
      -WebUrl "https://schnapp/"

  Or place client-defaults.json next to the deploy script:
    { "apiBaseUrl": "http://schnapp:5080", "webUrl": "https://schnapp/" }

  This writes/merges %AppData%\Schnapp\settings.json (apiBaseUrl, optional loginIdentity).
  Existing non-default apiBaseUrl is left alone unless -ForceApiBaseUrl is set.
  -WebUrl also adds a Start menu "Schnapp Web" shortcut (browser); use -NoWebShortcut to skip.

Manual / xcopy install
1. Copy this folder anywhere. Keep Assets\ next to the .exe (tray/menu icons).
2. Run schnapp-client-windows.exe
3. Login to your Schnapp API, register/use a machine, start capture from the tray.

deploy-windows-client.ps1 options
- -InstallDir "D:\Apps\Schnapp"   custom install path
- -ApiBaseUrl "http://host:5080" pre-seed API URL in settings.json
- -WebUrl "https://host/"        Start menu link to web UI; can imply API :5080
- -LoginIdentity "user@org"      optional pre-fill for login
- -ForceApiBaseUrl               overwrite existing apiBaseUrl
- -NoStartMenu                   skip Start menu shortcuts
- -NoWebShortcut                 skip "Schnapp Web" browser shortcut
- -NoElevate                     do not re-launch elevated (fails if rights missing)

Environment (optional overrides)
- SCHNAPP_API_BASE_URL, SCHNAPP_WEB_URL, SCHNAPP_LOGIN_IDENTITY

Single instance
- Only one copy per Windows session. A second launch shows a notice and exits.

Paths
- Config / token:  %AppData%\Schnapp\
- Spool / DB / logs: %LocalAppData%\Schnapp\
- Logs: %LocalAppData%\Schnapp\logs\schnapp-YYYY-MM-DD.log

Optional flags
- --verbose or --log-console  write log lines to the console as well as the file

Notes
- Requires e_sqlite3.dll beside the executable (included in this kit).
- Capture uses GDI; image formats are JPEG and PNG (no WebP).

For an existing install, use the upgrade kit (artifacts/upgrade/windows-client/<YYYYMMDD>).
"@ | Set-Content -Path (Join-Path $PublishDir "INSTALL.txt") -Encoding UTF8

# --- upgrade kit ---
Write-Host "Assembling upgrade kit → $UpgradeDir"
Copy-StagingTo $UpgradeDir
Copy-Item -Force $UpgradeScript (Join-Path $UpgradeDir "upgrade-windows-client.ps1")

@"
Schnapp Windows client — upgrade kit
====================================

Updates binaries under an existing install. Does not modify
%AppData%\Schnapp\ (config) or %LocalAppData%\Schnapp\ (data).

1. Unpack this folder.
2. Right-click upgrade-windows-client.ps1 → Run with PowerShell
   (or elevated: .\upgrade-windows-client.ps1)
3. Default target: C:\Program Files\Schnapp
   Custom: .\upgrade-windows-client.ps1 -InstallDir "D:\Apps\Schnapp"

The install directory must already exist (use the publish kit first).
"@ | Set-Content -Path (Join-Path $UpgradeDir "UPGRADE.txt") -Encoding UTF8

function Write-KitZip {
    param(
        [string]$Dir,
        [string]$Kind
    )
    if (-not $Zip) { return }
    # Place archive beside the dated kit folder (publish/windows-client/, upgrade/windows-client/).
    $zipDir = Split-Path -Parent $Dir
    New-Item -ItemType Directory -Force -Path $zipDir | Out-Null
    $zip = Join-Path $zipDir "$KitBase-$Kind.zip"
    if (Test-Path $zip) { Remove-Item -Force $zip }
    Compress-Archive -Path (Join-Path $Dir "*") -DestinationPath $zip
    Write-Host "Wrote $zip"
}

Write-KitZip -Dir $PublishDir -Kind "publish"
Write-KitZip -Dir $UpgradeDir -Kind "upgrade"

Write-Host
Write-Host "staging: $StagingDir"
Write-Host "publish: $PublishDir"
Write-Host "upgrade: $UpgradeDir"
