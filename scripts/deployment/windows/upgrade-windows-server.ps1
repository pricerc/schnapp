# Replace Schnapp API, Web, and utils binaries. Does not change
# %ProgramData%\Schnapp (config, Firebird file, blobs) or appsettings.Production.json.
#
# Run from the unpacked upgrade kit:
#   .\upgrade-windows-server.ps1
#   .\upgrade-windows-server.ps1 -InstallDir "D:\Apps\Schnapp\Server"
param(
    [string]$InstallDir = "$env:ProgramFiles\Schnapp\Server",
    [switch]$NoElevate
)

$ErrorActionPreference = "Stop"
$KitDir = $PSScriptRoot

function Test-IsAdministrator {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    if ($id.User.Value -eq "S-1-5-18") { return $true }
    $principal = New-Object Security.Principal.WindowsPrincipal($id)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Request-Elevation {
    $argList = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $PSCommandPath, "-NoElevate")
    if ($InstallDir -ne "$env:ProgramFiles\Schnapp\Server") {
        $argList += @("-InstallDir", $InstallDir)
    }
    $proc = Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $argList -Wait -PassThru
    exit $proc.ExitCode
}

function Copy-AppUpgrade {
    param([string]$Name)
    $src = Join-Path $KitDir $Name
    $dest = Join-Path $InstallDir $Name
    if (-not (Test-Path $src)) { throw "Upgrade kit is missing $Name\." }
    if (-not (Test-Path $dest)) {
        throw "Install directory is missing $dest. Use deploy-windows-server.ps1 for a first install."
    }
    Get-ChildItem -LiteralPath $src -Recurse -File | ForEach-Object {
        if ($_.Name -eq "appsettings.Production.json") { return }
        $rel = $_.FullName.Substring($src.Length).TrimStart('\', '/')
        $target = Join-Path $dest $rel
        $parent = Split-Path -Parent $target
        if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
        Copy-Item -LiteralPath $_.FullName -Destination $target -Force
    }
}

$needsAdmin = $InstallDir -match '^[A-Za-z]:\\Program Files\\'
if ($needsAdmin -and -not (Test-IsAdministrator)) {
    if ($NoElevate) { throw "Administrator rights required to upgrade '$InstallDir'." }
    Write-Host "Requesting elevation..."
    Request-Elevation
}

foreach ($name in @("SchnappWeb", "SchnappApi")) {
    $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne "Stopped") {
        Write-Host "Stopping $name..."
        Stop-Service -Name $name -Force
        $svc.WaitForStatus("Stopped", "00:00:30")
    }
}

Write-Host "Updating binaries in $InstallDir"
Copy-AppUpgrade "api"
Copy-AppUpgrade "web"
Copy-AppUpgrade "utils"

foreach ($name in @("SchnappApi", "SchnappWeb")) {
    $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($svc) {
        Start-Service -Name $name
        Write-Host "Started $name."
    }
}
Write-Host "Upgrade finished. Config, database, and blobs were not changed."
