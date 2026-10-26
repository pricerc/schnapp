# Install Schnapp API + Web as Windows services, from an unpacked publish kit.
# Run from the kit directory (elevated):
#   .\deploy-windows-server.ps1 -DbPassword '...' -AdminEmail 'you@example.com' -AdminPassword '...'
#
# The API and Web listen on 0.0.0.0 by default so a VM guest on this laptop can
# reach them. Firebird stays on 127.0.0.1. Pass -ApiBind 127.0.0.1 -WebBind 127.0.0.1
# to keep both on the laptop only.
#
# Binaries go under InstallDir (default C:\Program Files\Schnapp\Server), not the
# tray client's C:\Program Files\Schnapp folder. Config and data stay in DataRoot.
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
    [string]$SysdbaPassword = "masterkey",
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
    [string]$FirewallProfile = "Private,Domain",
    [string]$SecretFile = "",
    [switch]$AllowRemoteFirebird,
    [switch]$NoFirewall,
    [switch]$SkipDatabase,
    [switch]$ForceConfig,
    [switch]$NoStart,
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
        AdminEmail     = $AdminEmail
        AdminPassword  = $AdminPassword
    }
    $arg = "-NoProfile -ExecutionPolicy Bypass -File $(Format-CmdArg $PSCommandPath) -NoElevate -SecretFile $(Format-CmdArg $payload)"
    $arg += " -InstallDir $(Format-CmdArg $InstallDir) -DataRoot $(Format-CmdArg $DataRoot) -Provider $Provider"
    $arg += " -DbUser $(Format-CmdArg $DbUser) -DbName $(Format-CmdArg $DbName) -DbHost $(Format-CmdArg $DbHost)"
    $arg += " -ApiBind $(Format-CmdArg $ApiBind) -ApiPort $ApiPort -WebBind $(Format-CmdArg $WebBind) -WebPort $WebPort"
    $arg += " -FirewallProfile $(Format-CmdArg $FirewallProfile)"
    if ($DbPort -gt 0) { $arg += " -DbPort $DbPort" }
    if ($ApiUrls) { $arg += " -ApiUrls $(Format-CmdArg $ApiUrls)" }
    if ($WebUrls) { $arg += " -WebUrls $(Format-CmdArg $WebUrls)" }
    if ($ApiUrl) { $arg += " -ApiUrl $(Format-CmdArg $ApiUrl)" }
    if ($BlobRoot) { $arg += " -BlobRoot $(Format-CmdArg $BlobRoot)" }
    if ($FirebirdDir) { $arg += " -FirebirdDir $(Format-CmdArg $FirebirdDir)" }
    if ($AllowRemoteFirebird) { $arg += " -AllowRemoteFirebird" }
    if ($NoFirewall) { $arg += " -NoFirewall" }
    if ($SkipDatabase) { $arg += " -SkipDatabase" }
    if ($ForceConfig) { $arg += " -ForceConfig" }
    if ($NoStart) { $arg += " -NoStart" }
    $proc = Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $arg -Wait -PassThru
    if (Test-Path -LiteralPath $payload) { Remove-Item -LiteralPath $payload -Force -ErrorAction SilentlyContinue }
    if ($null -eq $proc -or $null -eq $proc.ExitCode) { exit 1 }
    exit $proc.ExitCode
}

function Copy-AppTree {
    param([string]$Name, [string]$Dll)
    $src = Join-Path $KitDir $Name
    $dllPath = Join-Path $src $Dll
    if (-not (Test-Path -LiteralPath $dllPath)) {
        throw "Kit folder '$Name' is missing $Dll. Unpack the publish kit and run this script from that folder."
    }
    $dest = Join-Path $InstallDir $Name
    if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item -Path (Join-Path $src "*") -Destination $dest -Recurse -Force
}

function Get-ServiceBinary {
    param([string]$Dir, [string]$DllName, [string]$ExeName)
    $exe = Join-Path $Dir $ExeName
    if (Test-Path -LiteralPath $exe) {
        # win-x64 publish emits an apphost exe for both framework-dependent and self-contained kits.
        return '"' + $exe + '"'
    }
    $dotnet = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
    if (-not (Test-Path -LiteralPath $dotnet)) {
        throw "This kit has no $ExeName and $dotnet is missing. Install the ASP.NET Core 10 runtime, or build with -SelfContained."
    }
    return '"' + $dotnet + '" "' + (Join-Path $Dir $DllName) + '"'
}

function Remove-SchnappService {
    param([string]$Name)
    $svc = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if (-not $svc) { return }
    if ($svc.Status -ne "Stopped") {
        Stop-Service -Name $Name -Force -ErrorAction SilentlyContinue
        $svc.WaitForStatus("Stopped", "00:00:20")
    }
    & sc.exe delete $Name | Out-Null
    Start-Sleep -Seconds 1
}

function Install-SchnappService {
    param(
        [string]$Name,
        [string]$Display,
        [string]$Binary,
        [string[]]$EnvironmentLines,
        [string]$DependsOn
    )
    Remove-SchnappService -Name $Name
    New-Service -Name $Name -BinaryPathName $Binary -DisplayName $Display -StartupType Automatic | Out-Null
    if ($DependsOn) {
        cmd.exe /c "sc config $Name depend= $DependsOn" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "sc config depend= $DependsOn failed for $Name (exit $LASTEXITCODE)." }
    }
    cmd.exe /c "sc failure $Name reset= 86400 actions= restart/5000/restart/5000/restart/5000" | Out-Null
    cmd.exe /c "sc description $Name `"$Display`"" | Out-Null
    $reg = "HKLM:\SYSTEM\CurrentControlSet\Services\$Name"
    New-ItemProperty -Path $reg -Name Environment -PropertyType MultiString -Value $EnvironmentLines -Force | Out-Null
}

function Read-EnvLines {
    param([string]$Path)
    # foreach stays in this function's scope. ForEach-Object would not (+= would be lost).
    $lines = @()
    foreach ($raw in @(Get-Content -LiteralPath $Path)) {
        $line = $raw.Trim()
        if ($line -and -not $line.StartsWith("#")) { $lines += $line }
    }
    return ,$lines
}

function Test-PublicBind {
    param([string]$Binds)
    foreach ($raw in ($Binds -split ",")) {
        $hostName = $raw.Trim()
        if (-not $hostName) { continue }
        if ($hostName -notin @("127.0.0.1", "localhost", "::1", "[::1]")) { return $true }
    }
    return $false
}

function Test-PublicListen {
    param([string]$Binds, [string]$Urls)
    if ($Urls) {
        foreach ($url in ($Urls -split ";")) {
            $u = $url.Trim()
            if (-not $u) { continue }
            if ($u -match '^https?://(127\.0\.0\.1|localhost|\[::1\])(:|/|$)') { continue }
            return $true
        }
        return $false
    }
    return Test-PublicBind $Binds
}

function Get-ListenPorts {
    param([string]$Urls, [int]$Fallback)
    $ports = @()
    if ($Urls) {
        foreach ($url in ($Urls -split ";")) {
            if ($url -match ':(\d+)\s*$') { $ports += [int]$Matches[1] }
        }
    }
    if ($ports.Count -eq 0) { return @($Fallback) }
    return @($ports | Select-Object -Unique)
}

function Add-SchnappFirewall {
    param([string]$Name, [int]$Port, [string]$Profiles)
    $existing = Get-NetFirewallRule -Name $Name -ErrorAction SilentlyContinue
    if ($existing) { Remove-NetFirewallRule -Name $Name }
    $profileList = @($Profiles -split '[,\s]+' | Where-Object { $_ })
    New-NetFirewallRule -Name $Name -DisplayName $Name -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -Profile $profileList | Out-Null
    Write-Host "Firewall: $Name TCP $Port ($Profiles)"
}

function Add-VirtualSwitchRule {
    param([string]$Name, [int]$Port, [string[]]$Aliases)
    $old = Get-NetFirewallRule -Name $Name -ErrorAction SilentlyContinue
    if ($old) { Remove-NetFirewallRule -Name $Name }
    New-NetFirewallRule -Name $Name -DisplayName $Name -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -InterfaceAlias $Aliases -Profile Any | Out-Null
    Write-Host "Firewall: $Name on $($Aliases -join ', ')"
}

if ($SecretFile) {
    if (-not (Test-Path -LiteralPath $SecretFile)) { throw "Elevation payload is missing ($SecretFile)." }
    $payload = [System.IO.File]::ReadAllText($SecretFile) | ConvertFrom-Json
    Remove-Item -LiteralPath $SecretFile -Force
    if ($payload.DbPassword) { $DbPassword = [string]$payload.DbPassword }
    if ($payload.SysdbaPassword) { $SysdbaPassword = [string]$payload.SysdbaPassword }
    if ($payload.AdminEmail) { $AdminEmail = [string]$payload.AdminEmail }
    if ($payload.AdminPassword) { $AdminPassword = [string]$payload.AdminPassword }
}

if (-not $DbPassword) { $DbPassword = [Environment]::GetEnvironmentVariable("SCHNAPP_DB_PASSWORD") }
if (-not $AdminEmail) { $AdminEmail = [Environment]::GetEnvironmentVariable("SCHNAPP_ADMIN_EMAIL") }
if (-not $AdminPassword) { $AdminPassword = [Environment]::GetEnvironmentVariable("SCHNAPP_ADMIN_PASSWORD") }

if (-not (Test-IsAdministrator)) {
    if ($NoElevate) { throw "Administrator rights required." }
    Write-Host "Requesting elevation..."
    Request-Elevation
}

foreach ($name in @("setup-database-firebird.ps1", "write-server-config.ps1", "backup-firebird.ps1")) {
    if (-not (Test-Path -LiteralPath (Join-Path $KitDir $name))) { throw "Kit is missing $name." }
}

Write-Host "Stopping existing Schnapp services..."
foreach ($name in @("SchnappWeb", "SchnappApi")) {
    $svc = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne "Stopped") {
        Stop-Service -Name $name -Force
        $svc.WaitForStatus("Stopped", "00:00:20")
    }
}

Write-Host "Copying binaries to $InstallDir"
Copy-AppTree "api" "Schnapp.Api.dll"
Copy-AppTree "web" "Schnapp.Web.dll"
Copy-AppTree "utils" "schnapp-utils.dll"

$scriptDir = Join-Path $InstallDir "scripts"
New-Item -ItemType Directory -Force -Path $scriptDir | Out-Null
foreach ($name in @("setup-database-firebird.ps1", "write-server-config.ps1", "backup-firebird.ps1")) {
    Copy-Item -Force -LiteralPath (Join-Path $KitDir $name) -Destination (Join-Path $scriptDir $name)
}

if ($Provider -eq "Firebird" -and -not $SkipDatabase) {
    $setup = Join-Path $KitDir "setup-database-firebird.ps1"
    if (-not $FirebirdDir) { $FirebirdDir = Join-Path $DataRoot "firebird" }
    $setupArgs = @{
        DataDir         = $FirebirdDir
        DbName          = $DbName
        DbUser          = $DbUser
        DbPassword      = $DbPassword
        SysdbaPassword  = $SysdbaPassword
        NoElevate       = $true
    }
    if ($AllowRemoteFirebird) { $setupArgs.AllowRemoteFirebird = $true }
    & $setup @setupArgs
    if ($LASTEXITCODE -ne 0) { throw "Firebird setup failed (exit $LASTEXITCODE)." }
}

$write = Join-Path $KitDir "write-server-config.ps1"
$writeArgs = @{
    InstallDir   = $InstallDir
    DataRoot     = $DataRoot
    Provider     = $Provider
    DbPassword   = $DbPassword
    DbUser       = $DbUser
    DbName       = $DbName
    DbHost       = $DbHost
    ApiBind      = $ApiBind
    ApiPort      = $ApiPort
    WebBind      = $WebBind
    WebPort      = $WebPort
}
if ($DbPort -gt 0) { $writeArgs.DbPort = $DbPort }
if ($AdminEmail) { $writeArgs.AdminEmail = $AdminEmail }
if ($AdminPassword) { $writeArgs.AdminPassword = $AdminPassword }
if ($ApiUrls) { $writeArgs.ApiUrls = $ApiUrls }
if ($WebUrls) { $writeArgs.WebUrls = $WebUrls }
if ($ApiUrl) { $writeArgs.ApiUrl = $ApiUrl }
if ($BlobRoot) { $writeArgs.BlobRoot = $BlobRoot }
if ($FirebirdDir) { $writeArgs.FirebirdDir = $FirebirdDir }
if ($ForceConfig) { $writeArgs.Force = $true }
& $write @writeArgs

$apiEnv = Join-Path $DataRoot "config\api.env"
$webEnv = Join-Path $DataRoot "config\web.env"
if (-not (Test-Path $apiEnv) -or -not (Test-Path $webEnv)) {
    throw "Config was not written under $DataRoot\config."
}

$apiBin = Get-ServiceBinary -Dir (Join-Path $InstallDir "api") -DllName "Schnapp.Api.dll" -ExeName "Schnapp.Api.exe"
$webBin = Get-ServiceBinary -Dir (Join-Path $InstallDir "web") -DllName "Schnapp.Web.dll" -ExeName "Schnapp.Web.exe"

$fbDepend = ""
if ($Provider -eq "Firebird") {
    $fb = Get-Service -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "FirebirdServer*" } | Select-Object -First 1
    if ($fb) { $fbDepend = $fb.Name }
    else { Write-Warning "No FirebirdServer* service found. SchnappApi will not wait for Firebird at boot." }
}

Write-Host "Installing services..."
Install-SchnappService -Name "SchnappApi" -Display "Schnapp API" -Binary $apiBin -EnvironmentLines (Read-EnvLines $apiEnv) -DependsOn $fbDepend
Install-SchnappService -Name "SchnappWeb" -Display "Schnapp Web" -Binary $webBin -EnvironmentLines (Read-EnvLines $webEnv) -DependsOn "SchnappApi"

$vmAliases = @()
if (-not $NoFirewall) {
    $vmAliases = @(Get-NetAdapter -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like "vEthernet*" -or $_.InterfaceDescription -like "*Hyper-V*" -or $_.InterfaceDescription -like "*Virtual*" } |
            Select-Object -ExpandProperty Name)
}

if (-not $NoFirewall -and (Test-PublicListen $ApiBind $ApiUrls)) {
    foreach ($port in (Get-ListenPorts $ApiUrls $ApiPort)) {
        Add-SchnappFirewall -Name "Schnapp API $port" -Port $port -Profiles $FirewallProfile
        if ($vmAliases.Count -gt 0) {
            Add-VirtualSwitchRule -Name "Schnapp API virtual $port" -Port $port -Aliases $vmAliases
        }
    }
}
if (-not $NoFirewall -and (Test-PublicListen $WebBind $WebUrls)) {
    foreach ($port in (Get-ListenPorts $WebUrls $WebPort)) {
        Add-SchnappFirewall -Name "Schnapp Web $port" -Port $port -Profiles $FirewallProfile
        if ($vmAliases.Count -gt 0) {
            Add-VirtualSwitchRule -Name "Schnapp Web virtual $port" -Port $port -Aliases $vmAliases
        }
    }
}

if (-not $NoStart) {
    Start-Service SchnappApi
    Start-Service SchnappWeb
    Write-Host "Started SchnappApi and SchnappWeb."
}

Write-Host ""
Write-Host "On this laptop:  http://127.0.0.1:$WebPort/Login"
Write-Host "API health:      http://127.0.0.1:$ApiPort/api/v1/health"
if ((Test-PublicListen $ApiBind $ApiUrls) -or (Test-PublicListen $WebBind $WebUrls)) {
    Write-Host "VM guests and other machines use this host's address, for example:"
    Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike "127.*" -and $_.PrefixOrigin -ne "WellKnown" } |
        ForEach-Object { Write-Host ("  http://{0}:{1}/api/v1/health    web http://{0}:{2}/Login" -f $_.IPAddress, $ApiPort, $WebPort) }
    Write-Host "There is no TLS. A guest uses the host address above, not 127.0.0.1."
    Write-Host "Firewall profile for ordinary adapters: $FirewallProfile. Virtual-switch adapters are allowed on any profile."
}
Write-Host "Catalog and images: $DataRoot"
Write-Host "Maintenance scripts: $scriptDir"
Write-Host "Include blobs and gbak output in the existing laptop-to-NAS backup. A raw copy of the live .fdb can tear; use backup-firebird.ps1."
