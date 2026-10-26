# Build Windows server kits: staging -> publish + upgrade.
#
# Layout (default Artifacts root = <repo>/artifacts):
#   staging/server-windows/           raw dotnet publish (api, web, utils)
#   publish/server-windows/YYYYMMDD/  full install kit
#   upgrade/server-windows/YYYYMMDD/  binaries + upgrade script only
#
# Run on a machine with the .NET 10 SDK (Windows, or a cross-publish host):
#   .\scripts\deployment\build-windows-server-deployment.ps1
#   .\scripts\deployment\build-windows-server-deployment.ps1 -SelfContained
#   .\scripts\deployment\build-windows-server-deployment.ps1 -OutputDir artifacts -Zip
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "",
    [string]$Stamp = "",
    [switch]$SelfContained,
    [switch]$Zip
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "../..")
Set-Location $Root

$WindowsDir = Join-Path $PSScriptRoot "windows"
$Required = @(
    "deploy-windows-server.ps1",
    "upgrade-windows-server.ps1",
    "write-server-config.ps1",
    "setup-database-firebird.ps1",
    "backup-firebird.ps1"
)
foreach ($name in $Required) {
    $path = Join-Path $WindowsDir $name
    if (-not (Test-Path $path)) { throw "missing $path" }
}

if (-not $OutputDir) { $OutputDir = Join-Path $Root "artifacts" }
if (-not [System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir = Join-Path $Root $OutputDir
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$OutputDir = (Resolve-Path $OutputDir).Path

if (-not $Stamp) { $Stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd") }

$StagingDir = Join-Path $OutputDir "staging/server-windows"
$PublishDir = Join-Path $OutputDir "publish/server-windows/$Stamp"
$UpgradeDir = Join-Path $OutputDir "upgrade/server-windows/$Stamp"

if (Test-Path $StagingDir) { Remove-Item -Recurse -Force $StagingDir }
New-Item -ItemType Directory -Force -Path $StagingDir | Out-Null

$projects = @(
    @{ Name = "api"; Project = "src/servers/Schnapp.Api/Schnapp.Api.csproj" },
    @{ Name = "web"; Project = "src/servers/Schnapp.Web/Schnapp.Web.csproj" },
    @{ Name = "utils"; Project = "src/clients/Schnapp.Utils/Schnapp.Utils.csproj" }
)
foreach ($p in $projects) {
    $out = Join-Path $StagingDir $p.Name
    Write-Host "Publishing $($p.Project) ($Configuration, $Runtime, self-contained=$SelfContained) -> $out"
    if ($SelfContained) {
        dotnet publish $p.Project -c $Configuration -r $Runtime --self-contained true -o $out
    }
    else {
        dotnet publish $p.Project -c $Configuration -r $Runtime --self-contained false -o $out
    }
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($p.Project)" }
}

$mode = "fd"
if ($SelfContained) { $mode = "sc" }
$manifest = @"
component: server-windows
self_contained: $(if ($SelfContained) { "1" } else { "0" })
rid: $Runtime
configuration: $Configuration
stamp: $Stamp
"@
Set-Content -LiteralPath (Join-Path $StagingDir "MANIFEST.txt") -Value $manifest.TrimEnd() -Encoding ascii

function Copy-StagingTo {
    param([string]$Dest)
    if (Test-Path $Dest) { Remove-Item -Recurse -Force $Dest }
    New-Item -ItemType Directory -Force -Path $Dest | Out-Null
    Copy-Item -Recurse -Force (Join-Path $StagingDir "*") $Dest
}

Write-Host "Assembling publish kit -> $PublishDir"
Copy-StagingTo $PublishDir
foreach ($name in $Required) {
    Copy-Item -Force (Join-Path $WindowsDir $name) (Join-Path $PublishDir $name)
}
$doc = Join-Path $Root "docs/deployment-windows.md"
if (Test-Path $doc) {
    Copy-Item -Force $doc (Join-Path $PublishDir "README-DEPLOY.md")
}

@"
Schnapp server — Windows publish kit
=====================================

Binaries, Firebird setup, config writer, and service install. Read README-DEPLOY.md.

1. Install Firebird 5 SuperServer (default SYSDBA password masterkey until you change it).
   The ASP.NET Core 10 runtime is required for a framework-dependent kit. A self-contained
   kit (folder name contains -sc-) does not need it.
2. From an elevated PowerShell in this folder:
     .\deploy-windows-server.ps1 ``
       -DbPassword '...' ``
       -AdminEmail 'you@example.com' ``
       -AdminPassword '...'
   API and Web listen on 0.0.0.0 (VM guests on this laptop can connect).
   Lock both to this machine with -ApiBind 127.0.0.1 -WebBind 127.0.0.1.
3. Health: http://127.0.0.1:5080/api/v1/health
   Web:    http://127.0.0.1:5081/Login
4. Schedule a consistent catalog dump into a folder the NAS backup already copies:
   .\backup-firebird.ps1 -BackupDir <folder-already-backed-up> -RegisterDaily
   Deploy also copies that script to C:\Program Files\Schnapp\Server\scripts.

PostgreSQL instead of Firebird: -Provider PostgreSQL -SkipDatabase, after the database
and role already exist. There is no Windows PostgreSQL setup script.

For an existing install, use the upgrade kit so config and data are not overwritten.
"@ | Set-Content -LiteralPath (Join-Path $PublishDir "INSTALL.txt") -Encoding ascii

Write-Host "Assembling upgrade kit -> $UpgradeDir"
Copy-StagingTo $UpgradeDir
Copy-Item -Force (Join-Path $WindowsDir "upgrade-windows-server.ps1") (Join-Path $UpgradeDir "upgrade-windows-server.ps1")
@"
Schnapp server — Windows upgrade kit
====================================

Replaces api\, web\, and utils\ under the install directory.
Does not change %ProgramData%\Schnapp or appsettings.Production.json.

From an elevated PowerShell in this folder:
  .\upgrade-windows-server.ps1
  .\upgrade-windows-server.ps1 -InstallDir "D:\Apps\Schnapp\Server"
"@ | Set-Content -LiteralPath (Join-Path $UpgradeDir "UPGRADE.txt") -Encoding ascii

if ($Zip) {
    $pubZip = Join-Path (Split-Path $PublishDir) "schnapp-server-windows-$mode-$Runtime-$Stamp.zip"
    $upZip = Join-Path (Split-Path $UpgradeDir) "schnapp-server-windows-$mode-$Runtime-$Stamp-upgrade.zip"
    if (Test-Path $pubZip) { Remove-Item -Force $pubZip }
    if (Test-Path $upZip) { Remove-Item -Force $upZip }
    Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $pubZip
    Compress-Archive -Path (Join-Path $UpgradeDir "*") -DestinationPath $upZip
    Write-Host "Wrote $pubZip"
    Write-Host "Wrote $upZip"
}

Write-Host "Publish kit: $PublishDir"
Write-Host "Upgrade kit: $UpgradeDir"
