# Create a Firebird 5 user and an empty schnapp.fdb (PAGE_SIZE 16384, UTF8).
# Firebird 5 SuperServer must already be installed. The catalog stays on this
# machine; VM guests use the Schnapp API, not port 3050.
#
#   .\setup-database-firebird.ps1 -DbPassword '...'
#   .\setup-database-firebird.ps1 -DataDir D:\Schnapp\firebird -DbPassword '...' -SysdbaPassword '...'
#
# Unless -AllowRemoteFirebird is set, an unset RemoteBindAddress in firebird.conf
# is set to 127.0.0.1 and the Firebird service is restarted.
param(
    [string]$DataDir = "$env:ProgramData\Schnapp\firebird",
    [string]$DbName = "schnapp",
    [string]$DbUser = "schnapp",
    [string]$DbPassword = "",
    [string]$SysdbaUser = "SYSDBA",
    [string]$SysdbaPassword = "masterkey",
    [string]$IsqlPath = "",
    [string]$RemoteBindAddress = "127.0.0.1",
    [string]$SecretFile = "",
    [switch]$AllowRemoteFirebird,
    [switch]$NoElevate
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    if ($id.User.Value -eq "S-1-5-18") { return $true }
    $principal = New-Object Security.Principal.WindowsPrincipal($id)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Format-CmdArg {
    param([string]$Value)
    if ($null -eq $Value) { $Value = "" }
    if ($Value -match '[\s"]') { return '"' + ($Value -replace '"', '\"') + '"' }
    return $Value
}

function Write-SecretPayload {
    param([hashtable]$Values)
    $path = Join-Path $env:TEMP ("schnapp-" + [guid]::NewGuid().ToString("n") + ".json")
    $enc = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($path, ($Values | ConvertTo-Json -Compress), $enc)
    $sid = ([Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
    & icacls $path /inheritance:r /grant:r "*${sid}:(F)" | Out-Null
    return $path
}

function Request-Elevation {
    $payload = Write-SecretPayload @{
        DbPassword     = $DbPassword
        SysdbaPassword = $SysdbaPassword
    }
    $arg = "-NoProfile -ExecutionPolicy Bypass -File $(Format-CmdArg $PSCommandPath) -NoElevate -SecretFile $(Format-CmdArg $payload)"
    $arg += " -DataDir $(Format-CmdArg $DataDir) -DbName $(Format-CmdArg $DbName) -DbUser $(Format-CmdArg $DbUser)"
    $arg += " -SysdbaUser $(Format-CmdArg $SysdbaUser) -RemoteBindAddress $(Format-CmdArg $RemoteBindAddress)"
    if ($IsqlPath) { $arg += " -IsqlPath $(Format-CmdArg $IsqlPath)" }
    if ($AllowRemoteFirebird) { $arg += " -AllowRemoteFirebird" }
    $proc = Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $arg -Wait -PassThru
    if (Test-Path -LiteralPath $payload) { Remove-Item -LiteralPath $payload -Force -ErrorAction SilentlyContinue }
    if ($null -eq $proc -or $null -eq $proc.ExitCode) { exit 1 }
    exit $proc.ExitCode
}

function Find-Isql {
    if ($IsqlPath) {
        if (Test-Path -LiteralPath $IsqlPath) { return (Resolve-Path -LiteralPath $IsqlPath).Path }
        throw "isql not found at $IsqlPath"
    }
    $roots = @("$env:ProgramFiles\Firebird")
    if (${env:ProgramFiles(x86)}) { $roots += "${env:ProgramFiles(x86)}\Firebird" }
    $hits = @()
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        $hits += @(Get-ChildItem -Path $root -Filter isql.exe -Recurse -ErrorAction SilentlyContinue)
    }
    $preferred = $hits | Where-Object { $_.FullName -match 'Firebird_5' } | Select-Object -First 1
    if ($preferred) { return $preferred.FullName }
    $any = $hits | Select-Object -First 1
    if ($any) {
        Write-Warning "Using $($any.FullName). Schnapp migrations need Firebird 5, not 3 or 4."
        return $any.FullName
    }
    throw "isql.exe not found. Install Firebird 5 SuperServer, or pass -IsqlPath."
}

function Find-BesideIsql {
    param([string]$Isql, [string]$FileName)
    $dir = Split-Path -Parent $Isql
    $direct = Join-Path $dir $FileName
    if (Test-Path -LiteralPath $direct) { return $direct }
    $parent = Join-Path (Split-Path -Parent $dir) $FileName
    if (Test-Path -LiteralPath $parent) { return $parent }
    return $null
}

function Invoke-Isql {
    param([string]$Isql, [string]$Sql, [string]$Database, [string]$User, [string]$Password)
    $tmp = Join-Path $env:TEMP ("schnapp-" + [guid]::NewGuid().ToString("n") + ".sql")
    $savedUser = $env:ISC_USER
    $savedPass = $env:ISC_PASSWORD
    try {
        $enc = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText($tmp, $Sql.TrimEnd() + "`n", $enc)
        $env:ISC_USER = $User
        $env:ISC_PASSWORD = $Password
        $argList = @("-input", $tmp)
        if ($Database) { $argList = @($Database) + $argList }
        $out = & $Isql @argList 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) {
            throw "isql failed (exit $LASTEXITCODE):`n$out"
        }
        return $out
    }
    finally {
        if ($null -eq $savedUser) { Remove-Item Env:ISC_USER -ErrorAction SilentlyContinue }
        else { $env:ISC_USER = $savedUser }
        if ($null -eq $savedPass) { Remove-Item Env:ISC_PASSWORD -ErrorAction SilentlyContinue }
        else { $env:ISC_PASSWORD = $savedPass }
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }
}

function ConvertTo-SqlLiteral {
    param([string]$Value)
    return ($Value -replace "'", "''")
}

function Update-RemoteBindAddress {
    param([string]$ConfPath, [string]$Address)
    $lines = @(Get-Content -LiteralPath $ConfPath)
    foreach ($line in $lines) {
        if ($line -match '^\s*RemoteBindAddress\s*=') {
            Write-Host "firebird.conf already sets RemoteBindAddress; leaving it ($ConfPath)."
            return $false
        }
    }
    $enc = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::AppendAllText($ConfPath, "`r`nRemoteBindAddress = $Address`r`n", $enc)
    Write-Host "Set RemoteBindAddress = $Address in $ConfPath"
    return $true
}

if ($SecretFile) {
    if (-not (Test-Path -LiteralPath $SecretFile)) { throw "Elevation payload is missing ($SecretFile)." }
    $payload = [System.IO.File]::ReadAllText($SecretFile) | ConvertFrom-Json
    Remove-Item -LiteralPath $SecretFile -Force
    if ($payload.DbPassword) { $DbPassword = [string]$payload.DbPassword }
    if ($payload.SysdbaPassword) { $SysdbaPassword = [string]$payload.SysdbaPassword }
}

if (-not $DbPassword) { $DbPassword = [Environment]::GetEnvironmentVariable("SCHNAPP_DB_PASSWORD") }
if (-not $DbPassword) { throw "Set -DbPassword or SCHNAPP_DB_PASSWORD." }

if (-not (Test-IsAdministrator)) {
    if ($NoElevate) { throw "Administrator rights required to create the Firebird catalog and edit firebird.conf." }
    Write-Host "Requesting elevation..."
    Request-Elevation
}

$fb = Get-Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "FirebirdServer*" } | Select-Object -First 1
$isql = Find-Isql
$restart = $false
if (-not $AllowRemoteFirebird -and $RemoteBindAddress) {
    $conf = Find-BesideIsql -Isql $isql -FileName "firebird.conf"
    if ($conf) { $restart = Update-RemoteBindAddress -ConfPath $conf -Address $RemoteBindAddress }
    else { Write-Warning "firebird.conf not found next to isql. Bind Firebird to 127.0.0.1 yourself so port 3050 is not reachable from VM guests." }
}

if ($fb) {
    if ($restart) {
        Write-Host "Restarting $($fb.Name)..."
        Restart-Service -Name $fb.Name -Force
        (Get-Service -Name $fb.Name).WaitForStatus("Running", "00:00:30")
    }
    elseif ((Get-Service -Name $fb.Name).Status -ne "Running") {
        Write-Host "Starting $($fb.Name)..."
        Start-Service -Name $fb.Name
    }
}
else {
    Write-Warning "No FirebirdServer* service found. isql will still try localhost."
}

New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
& icacls $DataDir /grant "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" | Out-Null

$fdb = Join-Path $DataDir ($DbName + ".fdb")
$userLit = ConvertTo-SqlLiteral $DbUser
$passLit = ConvertTo-SqlLiteral $DbPassword
$fdbLit = ConvertTo-SqlLiteral $fdb

Write-Host "isql: $isql"
Write-Host "database: $fdb"

$createUser = "CREATE USER $DbUser PASSWORD '$passLit';`nCOMMIT;"
$security = Find-BesideIsql -Isql $isql -FileName "security5.fdb"
$securityTarget = $null
if ($security) { $securityTarget = "localhost:${security}" }
else { Write-Warning "security5.fdb not found next to isql. CREATE USER will run without a database attachment." }
try {
    Invoke-Isql -Isql $isql -Sql $createUser -Database $securityTarget -User $SysdbaUser -Password $SysdbaPassword | Out-Null
    Write-Host "Created Firebird user $DbUser."
}
catch {
    Write-Host "Firebird user $DbUser already exists, or SYSDBA rejected CREATE USER. Continuing."
}

if (Test-Path -LiteralPath $fdb) {
    Write-Host "Database file already exists."
}
else {
    $createDb = @"
CREATE DATABASE '$fdbLit'
  USER '$userLit' PASSWORD '$passLit'
  PAGE_SIZE 16384
  DEFAULT CHARACTER SET UTF8;
"@
    Write-Host "Creating $fdb ..."
    Invoke-Isql -Isql $isql -Sql $createDb -User $SysdbaUser -Password $SysdbaPassword | Out-Null
}

$pageSql = "SELECT MON`$PAGE_SIZE FROM MON`$DATABASE;"
$pageOut = Invoke-Isql -Isql $isql -Sql $pageSql -Database "localhost:${fdb}" -User $DbUser -Password $DbPassword
if ($pageOut -notmatch '\b16384\b') {
    throw "Expected PAGE_SIZE 16384 but isql returned:`n$pageOut`nA 4 KB database cannot store Schnapp's UTF8 indexes. Create a new file; page size cannot be altered in place."
}

Write-Host "Firebird catalog is ready (PAGE_SIZE 16384)."
Write-Host "Connection string:"
Write-Host "  DataSource=127.0.0.1;Port=3050;Database=$fdb;User=$DbUser;Password=***;Charset=UTF8;Dialect=3"
if (-not $AllowRemoteFirebird) {
    Write-Host "Firebird is for this laptop. VM guests should call the Schnapp API, not port 3050."
}
