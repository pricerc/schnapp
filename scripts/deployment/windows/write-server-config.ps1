# Write Production config for the Windows Schnapp API and Web services.
# Does not install services or create the database.
#
#   .\write-server-config.ps1 -DbPassword '…' -AdminEmail 'you@example.com' -AdminPassword '…'
#   .\write-server-config.ps1 -Provider PostgreSQL -DbPassword '…' -ApiBind 127.0.0.1
#
# Listen addresses become ASPNETCORE_URLS (http://host:port, semicolon-separated).
# The Web process calls the API at -ApiUrl, which defaults to http://127.0.0.1:<api port>
# so a bind of 0.0.0.0 is not used as an outbound address.
#
# Existing files are left in place unless -Force is set.
param(
    [string]$InstallDir = "$env:ProgramFiles\Schnapp\Server",
    [string]$DataRoot = "$env:ProgramData\Schnapp",
    [ValidateSet("Firebird", "PostgreSQL")]
    [string]$Provider = "Firebird",
    [string]$DbPassword = "",
    [string]$DbUser = "schnapp",
    [string]$DbName = "schnapp",
    [string]$DbHost = "127.0.0.1",
    [int]$DbPort = 0,
    [string]$AdminEmail = "",
    [string]$AdminPassword = "",
    [string]$ApiBind = "0.0.0.0",
    [int]$ApiPort = 5080,
    [string]$WebBind = "0.0.0.0",
    [int]$WebPort = 5081,
    [string]$ApiUrls = "",
    [string]$WebUrls = "",
    [string]$ApiUrl = "",
    [string]$BlobRoot = "",
    [string]$FirebirdDir = "",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function ConvertTo-ListenUrls {
    param([string]$Binds, [int]$Port)
    $urls = @()
    foreach ($raw in ($Binds -split ",")) {
        $hostName = $raw.Trim()
        if (-not $hostName) { continue }
        if ($hostName -match '^\[.*\]$') {
            $urls += "http://${hostName}:$Port"
        }
        elseif ($hostName.Contains(":")) {
            $urls += "http://[$hostName]:$Port"
        }
        else {
            $urls += "http://${hostName}:$Port"
        }
    }
    if ($urls.Count -eq 0) {
        throw "No listen addresses in '$Binds'."
    }
    return ($urls -join ";")
}

function Get-BffApiUrl {
    param([string]$ListenUrls)
    $first = ""
    $wildcard = ""
    foreach ($url in ($ListenUrls -split ";")) {
        $u = $url.Trim().TrimEnd("/")
        if (-not $u) { continue }
        if (-not $first) { $first = $u }
        if ($u -match '^https?://(127\.0\.0\.1|localhost|\[::1\])(:|/|$)') { return $u }
        if ($u -match '^https?://(\*|0\.0\.0\.0|\[::\]|::)(:|/|$)') {
            if ($u -match ':(\d+)$') { $wildcard = "http://127.0.0.1:$($Matches[1])" }
            else { $wildcard = "http://127.0.0.1" }
        }
    }
    if ($wildcard) { return $wildcard }
    if (-not $first) { throw "API URL list is empty." }
    return $first
}

function Write-Utf8NoBom {
    param([string]$Path, [string]$Text)
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    $enc = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($Path, $Text.TrimEnd() + "`n", $enc)
}

function Protect-ConfigFile {
    param([string]$Path)
    # SIDs: LocalSystem and BUILTIN\Administrators. Names differ on non-English Windows.
    & icacls $Path /inheritance:r /grant:r "*S-1-5-18:(F)" "*S-1-5-32-544:(F)" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Could not tighten ACL on $Path (icacls exit $LASTEXITCODE)."
    }
}

if (-not $DbPassword) { $DbPassword = [Environment]::GetEnvironmentVariable("SCHNAPP_DB_PASSWORD") }
if (-not $AdminEmail) { $AdminEmail = [Environment]::GetEnvironmentVariable("SCHNAPP_ADMIN_EMAIL") }
if (-not $AdminPassword) { $AdminPassword = [Environment]::GetEnvironmentVariable("SCHNAPP_ADMIN_PASSWORD") }
if (-not $DbPassword) { throw "Set -DbPassword or SCHNAPP_DB_PASSWORD." }
if ($DbPassword.Contains(";")) { throw "DbPassword cannot contain ';' because it is stored in a connection string." }

if (-not $BlobRoot) { $BlobRoot = Join-Path $DataRoot "blobs" }
if (-not $FirebirdDir) { $FirebirdDir = Join-Path $DataRoot "firebird" }
$ConfigDir = Join-Path $DataRoot "config"

if ($Provider -eq "Firebird") {
    if ($DbPort -le 0) { $DbPort = 3050 }
    $fdb = Join-Path $FirebirdDir ($DbName + ".fdb")
    $conn = "DataSource=$DbHost;Port=$DbPort;Database=$fdb;User=$DbUser;Password=$DbPassword;Charset=UTF8;Dialect=3"
}
else {
    if ($DbPort -le 0) { $DbPort = 5432 }
    $conn = "Host=$DbHost;Port=$DbPort;Database=$DbName;Username=$DbUser;Password=$DbPassword"
}

if (-not $ApiUrls) { $ApiUrls = ConvertTo-ListenUrls -Binds $ApiBind -Port $ApiPort }
if (-not $WebUrls) { $WebUrls = ConvertTo-ListenUrls -Binds $WebBind -Port $WebPort }
if (-not $ApiUrl) { $ApiUrl = Get-BffApiUrl -ListenUrls $ApiUrls }

New-Item -ItemType Directory -Force -Path $ConfigDir, $BlobRoot | Out-Null
if ($Provider -eq "Firebird") {
    New-Item -ItemType Directory -Force -Path $FirebirdDir | Out-Null
}

$apiJsonPath = Join-Path $ConfigDir "api.appsettings.Production.json"
$webJsonPath = Join-Path $ConfigDir "web.appsettings.Production.json"
$apiEnvPath = Join-Path $ConfigDir "api.env"
$webEnvPath = Join-Path $ConfigDir "web.env"

$apiObj = [ordered]@{
    ConnectionStrings = [ordered]@{ Main = $conn }
    Schnapp           = [ordered]@{
        DatabaseProvider = $Provider
        BlobRoot         = $BlobRoot
        MigrateOnStartup = $true
    }
    Logging           = [ordered]@{
        LogLevel = [ordered]@{
            Default              = "Information"
            "Microsoft.AspNetCore" = "Warning"
        }
    }
    AllowedHosts      = "*"
}
if ($AdminEmail) { $apiObj.Schnapp.AdminEmail = $AdminEmail }
if ($AdminPassword) { $apiObj.Schnapp.AdminPassword = $AdminPassword }

$webObj = [ordered]@{
    Schnapp      = [ordered]@{
        ApiBaseUrl   = $ApiUrl
        ShowDevTools = $false
    }
    Logging      = [ordered]@{
        LogLevel = [ordered]@{
            Default              = "Information"
            "Microsoft.AspNetCore" = "Warning"
        }
    }
    AllowedHosts = "*"
}

$apiEnv = @(
    "ASPNETCORE_ENVIRONMENT=Production"
    "ASPNETCORE_URLS=$ApiUrls"
    "ConnectionStrings__Main=$conn"
    "Schnapp__DatabaseProvider=$Provider"
    "Schnapp__BlobRoot=$BlobRoot"
    "Schnapp__MigrateOnStartup=true"
)
if ($AdminEmail) { $apiEnv += "SCHNAPP_ADMIN_EMAIL=$AdminEmail" }
if ($AdminPassword) { $apiEnv += "SCHNAPP_ADMIN_PASSWORD=$AdminPassword" }

$webEnv = @(
    "ASPNETCORE_ENVIRONMENT=Production"
    "ASPNETCORE_URLS=$WebUrls"
    "Schnapp__ApiBaseUrl=$ApiUrl"
)

$script:ConfigSkipped = $false

function Write-ConfigFile {
    param([string]$Path, [string]$Text)
    if ((Test-Path $Path) -and -not $Force) {
        $script:ConfigSkipped = $true
        Write-Host "skip (exists): $Path  (use -Force to overwrite)"
        return
    }
    Write-Utf8NoBom -Path $Path -Text $Text
    Protect-ConfigFile -Path $Path
    Write-Host "wrote $Path"
}

Write-ConfigFile $apiJsonPath ($apiObj | ConvertTo-Json -Depth 6)
Write-ConfigFile $webJsonPath ($webObj | ConvertTo-Json -Depth 6)
Write-ConfigFile $apiEnvPath ($apiEnv -join "`n")
Write-ConfigFile $webEnvPath ($webEnv -join "`n")

# Drop Production json next to the binaries when this machine already has them.
foreach ($pair in @(
        @{ Src = $apiJsonPath; Dir = Join-Path $InstallDir "api" },
        @{ Src = $webJsonPath; Dir = Join-Path $InstallDir "web" }
    )) {
    if (-not (Test-Path $pair.Dir)) { continue }
    $dest = Join-Path $pair.Dir "appsettings.Production.json"
    if ((Test-Path $dest) -and -not $Force) {
        $script:ConfigSkipped = $true
        Write-Host "skip (exists): $dest"
        continue
    }
    Copy-Item -Force -LiteralPath $pair.Src -Destination $dest
    Protect-ConfigFile -Path $dest
    Write-Host "copied $dest"
}

Write-Host ""
Write-Host "API ASPNETCORE_URLS=$ApiUrls"
Write-Host "Web ASPNETCORE_URLS=$WebUrls"
Write-Host "Web Schnapp:ApiBaseUrl=$ApiUrl"
Write-Host "Config: $ConfigDir"
if ($script:ConfigSkipped) {
    Write-Warning "Left existing config files in place, so listen addresses and passwords were not updated. Re-run with -Force to replace them."
}
