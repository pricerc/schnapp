# Upgrade the Schnapp Windows tray client on an existing install.
# Replaces binaries under the install directory; does not touch
# %AppData%\Schnapp\ (config) or %LocalAppData%\Schnapp\ (data/logs).
#
# Run from the upgrade kit directory:
#   .\upgrade-windows-client.ps1
#   .\upgrade-windows-client.ps1 -InstallDir "D:\Apps\Schnapp"
param(
    [string]$InstallDir = "C:\Program Files\Schnapp",
    [switch]$NoElevate
)

$ErrorActionPreference = "Stop"

$KitDir = $PSScriptRoot
$ExeName = "schnapp-client-windows.exe"

function Test-IsAdministrator {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($id)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Request-Elevation {
    $args = @(
        "-NoProfile"
        "-ExecutionPolicy", "Bypass"
        "-File", $PSCommandPath
    )
    if ($InstallDir -ne "C:\Program Files\Schnapp") {
        $args += "-InstallDir", $InstallDir
    }
    $args += "-NoElevate"

    Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $args -Wait
    exit $LASTEXITCODE
}

if (-not (Test-Path (Join-Path $KitDir $ExeName))) {
    Write-Error "Missing $ExeName next to this script. Unpack the full upgrade kit and run from that folder."
}

if (-not (Test-Path $InstallDir)) {
    Write-Error "Install directory does not exist: $InstallDir. Use the publish kit (deploy-windows-client.ps1) for a first-time install."
}

$needsAdmin = $InstallDir -match '^[A-Za-z]:\\Program Files'
if ($needsAdmin -and -not (Test-IsAdministrator)) {
    if ($NoElevate) {
        Write-Error "Administrator rights required to upgrade '$InstallDir'."
    }
    Write-Host "Requesting elevation to upgrade $InstallDir ..."
    Request-Elevation
}

Write-Host "kit:  $KitDir"
Write-Host "to:   $InstallDir (upgrade in place)"
Write-Host

$excludeNames = @(
    "upgrade-windows-client.ps1"
    "deploy-windows-client.ps1"
    "INSTALL.txt"
    "UPGRADE.txt"
    "README.txt"
)

Get-ChildItem -LiteralPath $KitDir -Force | Where-Object {
    $excludeNames -notcontains $_.Name
} | ForEach-Object {
    $dest = Join-Path $InstallDir $_.Name
    if ($_.PSIsContainer) {
        Copy-Item -LiteralPath $_.FullName -Destination $dest -Recurse -Force
    }
    else {
        Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
    }
}

$InstalledExe = Join-Path $InstallDir $ExeName
if (-not (Test-Path $InstalledExe)) {
    Write-Error "Upgrade failed: $InstalledExe not found after copy."
}

Write-Host
Write-Host "upgraded: $InstalledExe"
Write-Host
Write-Host "Config untouched: %AppData%\Schnapp\"
Write-Host "Data untouched:   %LocalAppData%\Schnapp\"
