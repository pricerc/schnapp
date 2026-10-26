# gbak the Schnapp Firebird catalog into a folder the laptop backup already covers.
# Reads the password from DataRoot\config\api.env (not from the command line).
#
#   .\backup-firebird.ps1
#   .\backup-firebird.ps1 -BackupDir D:\Backups\Schnapp
#   .\backup-firebird.ps1 -RegisterDaily -BackupDir D:\Backups\Schnapp
param(
    [string]$DataRoot = "$env:ProgramData\Schnapp",
    [string]$BackupDir = "",
    [string]$GbakPath = "",
    [int]$Keep = 14,
    [switch]$RegisterDaily,
    [switch]$NoElevate
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    if ($id.User.Value -eq "S-1-5-18") { return $true }
    $principal = New-Object Security.Principal.WindowsPrincipal($id)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Find-Gbak {
    if ($GbakPath) {
        if (Test-Path -LiteralPath $GbakPath) { return (Resolve-Path -LiteralPath $GbakPath).Path }
        throw "gbak not found at $GbakPath"
    }
    $roots = @("$env:ProgramFiles\Firebird")
    if (${env:ProgramFiles(x86)}) { $roots += "${env:ProgramFiles(x86)}\Firebird" }
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        $hit = Get-ChildItem -Path $root -Filter gbak.exe -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match 'Firebird_5' } |
            Select-Object -First 1
        if (-not $hit) {
            $hit = Get-ChildItem -Path $root -Filter gbak.exe -Recurse -ErrorAction SilentlyContinue |
                Select-Object -First 1
        }
        if ($hit) { return $hit.FullName }
    }
    throw "gbak.exe not found. Install Firebird 5, or pass -GbakPath."
}

function Read-EnvFile {
    param([string]$Path)
    $map = @{}
    Get-Content -LiteralPath $Path | ForEach-Object {
        $line = $_.Trim()
        if (-not $line -or $line.StartsWith("#")) { return }
        $eq = $line.IndexOf("=")
        if ($eq -lt 1) { return }
        $map[$line.Substring(0, $eq).Trim()] = $line.Substring($eq + 1)
    }
    return $map
}

function Format-CmdArg {
    param([string]$Value)
    if ($null -eq $Value) { $Value = "" }
    if ($Value -match '[\s"]') { return '"' + ($Value -replace '"', '\"') + '"' }
    return $Value
}

if (-not (Test-IsAdministrator)) {
    if ($NoElevate) { throw "Administrator rights required to read the Schnapp config and run gbak." }
    $arg = "-NoProfile -ExecutionPolicy Bypass -File $(Format-CmdArg $PSCommandPath) -NoElevate -DataRoot $(Format-CmdArg $DataRoot) -Keep $Keep"
    if ($BackupDir) { $arg += " -BackupDir $(Format-CmdArg $BackupDir)" }
    if ($GbakPath) { $arg += " -GbakPath $(Format-CmdArg $GbakPath)" }
    if ($RegisterDaily) { $arg += " -RegisterDaily" }
    $proc = Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $arg -Wait -PassThru
    if ($null -eq $proc -or $null -eq $proc.ExitCode) { exit 1 }
    exit $proc.ExitCode
}

if ($RegisterDaily) {
    if (-not $BackupDir) { throw "-RegisterDaily needs -BackupDir (a folder your NAS backup already copies)." }
    $arg = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -DataRoot `"$DataRoot`" -BackupDir `"$BackupDir`" -Keep $Keep"
    if ($GbakPath) { $arg += " -GbakPath `"$GbakPath`"" }
    $action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $arg
    $trigger = New-ScheduledTaskTrigger -Daily -At 2:15am
    Register-ScheduledTask -TaskName "Schnapp Firebird backup" -Action $action -Trigger $trigger -User "SYSTEM" -RunLevel Highest -Force | Out-Null
    Write-Host "Registered scheduled task 'Schnapp Firebird backup' (daily 02:15, SYSTEM)."
}

if (-not $BackupDir) { $BackupDir = Join-Path $DataRoot "backup" }
New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null

$envFile = Join-Path $DataRoot "config\api.env"
if (-not (Test-Path -LiteralPath $envFile)) {
    throw "Missing $envFile. Run deploy-windows-server.ps1 first."
}
$cfg = Read-EnvFile $envFile
$conn = $cfg["ConnectionStrings__Main"]
if (-not $conn) { throw "ConnectionStrings__Main is missing from api.env." }
if ($cfg["Schnapp__DatabaseProvider"] -and $cfg["Schnapp__DatabaseProvider"] -ne "Firebird") {
    throw "api.env is $($cfg['Schnapp__DatabaseProvider']), not Firebird. This backup uses gbak."
}

$parts = @{}
foreach ($piece in ($conn -split ";")) {
    $eq = $piece.IndexOf("=")
    if ($eq -lt 1) { continue }
    $parts[$piece.Substring(0, $eq).Trim()] = $piece.Substring($eq + 1)
}
$database = $parts["Database"]
$user = $parts["User"]
if (-not $user) { $user = $parts["Username"] }
$password = $parts["Password"]
$dataSource = $parts["DataSource"]
if (-not $dataSource) { $dataSource = "127.0.0.1" }
if (-not $database -or -not $user -or -not $password) {
    throw "api.env connection string is missing Database, User, or Password."
}

$gbak = Find-Gbak
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$dest = Join-Path $BackupDir "schnapp-$stamp.fbk"
$target = "${dataSource}:${database}"
Write-Host "gbak $target -> $dest"
$savedUser = $env:ISC_USER
$savedPass = $env:ISC_PASSWORD
$env:ISC_USER = $user
$env:ISC_PASSWORD = $password
try {
    # ISC_USER / ISC_PASSWORD keep the password off the gbak command line.
    & $gbak -b $target $dest
    if ($LASTEXITCODE -ne 0) { throw "gbak failed (exit $LASTEXITCODE)." }
}
finally {
    if ($null -eq $savedUser) { Remove-Item Env:ISC_USER -ErrorAction SilentlyContinue }
    else { $env:ISC_USER = $savedUser }
    if ($null -eq $savedPass) { Remove-Item Env:ISC_PASSWORD -ErrorAction SilentlyContinue }
    else { $env:ISC_PASSWORD = $savedPass }
}

Get-ChildItem -LiteralPath $BackupDir -Filter "schnapp-*.fbk" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -Skip $Keep |
    Remove-Item -Force
Write-Host "Wrote $dest"
