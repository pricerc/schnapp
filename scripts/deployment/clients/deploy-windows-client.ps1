# Install / update the Schnapp Windows tray client from a deployment kit.
# Run from the unpacked kit directory (elevated — writes under Program Files):
#   .\deploy-windows-client.ps1
#   .\deploy-windows-client.ps1 -InstallDir "D:\Apps\Schnapp"
#   .\deploy-windows-client.ps1 -ApiBaseUrl "http://schnapp:5080" -WebUrl "https://schnapp/"
#   .\deploy-windows-client.ps1 -NoStartMenu
#
# Pre-configure server URLs (any of these, first non-empty wins per field):
#   1) Parameters: -ApiBaseUrl / -WebUrl / -LoginIdentity
#   2) Environment: SCHNAPP_API_BASE_URL, SCHNAPP_WEB_URL, SCHNAPP_LOGIN_IDENTITY
#   3) Kit file next to this script: client-defaults.json  (or client-defaults.env)
#
# Writes/merges %AppData%\Schnapp\settings.json (current user). Does not overwrite
# an existing ApiBaseUrl unless -ForceApiBaseUrl is set.
param(
    [string]$InstallDir = "C:\Program Files\Schnapp",
    [string]$ApiBaseUrl = "",
    [string]$WebUrl = "",
    [string]$LoginIdentity = "",
    [switch]$ForceApiBaseUrl,
    [switch]$NoStartMenu,
    [switch]$NoWebShortcut,
    [switch]$NoElevate
)

$ErrorActionPreference = "Stop"

$KitDir = $PSScriptRoot
$ExeName = "schnapp-client-windows.exe"
$ShortcutName = "Schnapp.lnk"
$WebShortcutName = "Schnapp Web.lnk"

function Test-IsAdministrator {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($id)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Request-Elevation {
    $argList = @(
        "-NoProfile"
        "-ExecutionPolicy", "Bypass"
        "-File", $PSCommandPath
    )
    if ($InstallDir -ne "C:\Program Files\Schnapp") {
        $argList += "-InstallDir", $InstallDir
    }
    if ($ApiBaseUrl) { $argList += "-ApiBaseUrl", $ApiBaseUrl }
    if ($WebUrl) { $argList += "-WebUrl", $WebUrl }
    if ($LoginIdentity) { $argList += "-LoginIdentity", $LoginIdentity }
    if ($ForceApiBaseUrl) { $argList += "-ForceApiBaseUrl" }
    if ($NoStartMenu) { $argList += "-NoStartMenu" }
    if ($NoWebShortcut) { $argList += "-NoWebShortcut" }
    $argList += "-NoElevate"

    Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $argList -Wait
    exit $LASTEXITCODE
}

function Get-KitDefaults {
    $result = @{
        ApiBaseUrl    = ""
        WebUrl        = ""
        LoginIdentity = ""
    }

    $jsonPath = Join-Path $KitDir "client-defaults.json"
    if (Test-Path $jsonPath) {
        try {
            $j = Get-Content -LiteralPath $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($j.apiBaseUrl) { $result.ApiBaseUrl = [string]$j.apiBaseUrl }
            if ($j.ApiBaseUrl) { $result.ApiBaseUrl = [string]$j.ApiBaseUrl }
            if ($j.webUrl) { $result.WebUrl = [string]$j.webUrl }
            if ($j.WebUrl) { $result.WebUrl = [string]$j.WebUrl }
            if ($j.loginIdentity) { $result.LoginIdentity = [string]$j.loginIdentity }
            if ($j.LoginIdentity) { $result.LoginIdentity = [string]$j.LoginIdentity }
        }
        catch {
            Write-Warning "Could not parse client-defaults.json: $_"
        }
    }

    $envPath = Join-Path $KitDir "client-defaults.env"
    if (Test-Path $envPath) {
        Get-Content -LiteralPath $envPath -Encoding UTF8 | ForEach-Object {
            $line = $_.Trim()
            if (-not $line -or $line.StartsWith("#")) { return }
            $eq = $line.IndexOf("=")
            if ($eq -lt 1) { return }
            $k = $line.Substring(0, $eq).Trim()
            $v = $line.Substring($eq + 1).Trim().Trim('"').Trim("'")
            switch -Regex ($k) {
                '^(ApiBaseUrl|SCHNAPP_API_BASE_URL|API_BASE_URL)$' { if ($v) { $result.ApiBaseUrl = $v } }
                '^(WebUrl|SCHNAPP_WEB_URL|WEB_URL)$' { if ($v) { $result.WebUrl = $v } }
                '^(LoginIdentity|SCHNAPP_LOGIN_IDENTITY)$' { if ($v) { $result.LoginIdentity = $v } }
            }
        }
    }

    return $result
}

function Resolve-ServerSettings {
    $kit = Get-KitDefaults
    $api = $ApiBaseUrl
    if (-not $api) { $api = $env:SCHNAPP_API_BASE_URL }
    if (-not $api) { $api = $kit.ApiBaseUrl }

    $web = $WebUrl
    if (-not $web) { $web = $env:SCHNAPP_WEB_URL }
    if (-not $web) { $web = $kit.WebUrl }

    $id = $LoginIdentity
    if (-not $id) { $id = $env:SCHNAPP_LOGIN_IDENTITY }
    if (-not $id) { $id = $kit.LoginIdentity }

    # If only web URL given, derive API as same host :5080 when path is root (appliance default)
    if (-not $api -and $web) {
        try {
            $u = [Uri]$web
            $port = if ($u.Port -gt 0 -and $u.Port -ne 80 -and $u.Port -ne 443) { $u.Port } else { 5080 }
            # Appliance: web on 80/443, API on 5080
            if ($u.Port -eq 80 -or $u.Port -eq 443 -or $u.IsDefaultPort) {
                $api = "{0}://{1}:5080" -f $u.Scheme.Replace("https", "http"), $u.Host
                if ($u.Scheme -eq "https") {
                    # Prefer http to API on LAN appliance unless they reverse-proxy API too
                    $api = "http://{0}:5080" -f $u.Host
                }
            }
            else {
                $api = $web.TrimEnd("/")
            }
        }
        catch {
            # leave api empty
        }
    }

    return @{
        ApiBaseUrl    = ($api -as [string]).Trim()
        WebUrl        = ($web -as [string]).Trim()
        LoginIdentity = ($id -as [string]).Trim()
    }
}

function Merge-ClientSettings {
    param(
        [string]$ApiBaseUrlValue,
        [string]$LoginIdentityValue,
        [switch]$ForceApi
    )

    $configDir = Join-Path $env:APPDATA "Schnapp"
    New-Item -ItemType Directory -Force -Path $configDir | Out-Null
    $settingsPath = Join-Path $configDir "settings.json"

    # Hashtable merge keeps Client.Core camelCase property names
    $map = [ordered]@{}
    if (Test-Path $settingsPath) {
        try {
            $raw = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8
            $obj = $raw | ConvertFrom-Json
            foreach ($p in $obj.PSObject.Properties) {
                $name = $p.Name
                # Normalize known keys to camelCase
                if ($name -eq "ApiBaseUrl") { $name = "apiBaseUrl" }
                if ($name -eq "LoginIdentity") { $name = "loginIdentity" }
                $map[$name] = $p.Value
            }
        }
        catch {
            Write-Warning "Existing settings.json is not valid JSON; replacing server fields only via new file."
            $map = [ordered]@{}
        }
    }

    $existingApi = ""
    if ($map.Contains("apiBaseUrl")) { $existingApi = [string]$map["apiBaseUrl"] }

    $wroteApi = $false
    if ($ApiBaseUrlValue) {
        $isDefault = [string]::IsNullOrWhiteSpace($existingApi) -or $existingApi -eq "http://127.0.0.1:5080"
        if ($ForceApi -or $isDefault) {
            $map["apiBaseUrl"] = $ApiBaseUrlValue
            $wroteApi = $true
        }
        else {
            Write-Host "settings.json already has apiBaseUrl=$existingApi (use -ForceApiBaseUrl to replace)"
        }
    }

    if ($LoginIdentityValue) {
        $map["loginIdentity"] = $LoginIdentityValue
    }

    if (-not $wroteApi -and -not $LoginIdentityValue) {
        return
    }

    # Ensure defaults the client expects if file is new/minimal
    if (-not $map.Contains("apiBaseUrl") -or [string]::IsNullOrWhiteSpace([string]$map["apiBaseUrl"])) {
        $map["apiBaseUrl"] = "http://127.0.0.1:5080"
    }

    $json = $map | ConvertTo-Json -Depth 8
    Set-Content -LiteralPath $settingsPath -Value $json -Encoding UTF8
    Write-Host "config:  $settingsPath"
    if ($wroteApi) { Write-Host "         apiBaseUrl = $($map['apiBaseUrl'])" }
    if ($LoginIdentityValue) { Write-Host "         loginIdentity = $LoginIdentityValue" }
}

if (-not (Test-Path (Join-Path $KitDir $ExeName))) {
    Write-Error "Missing $ExeName next to this script. Unpack the full deployment kit and run from that folder."
}

# Writing under Program Files (or other protected paths) needs elevation.
$needsAdmin = $InstallDir -match '^[A-Za-z]:\\Program Files'
if ($needsAdmin -and -not (Test-IsAdministrator)) {
    if ($NoElevate) {
        Write-Error "Administrator rights required to install to '$InstallDir'."
    }
    Write-Host "Requesting elevation to install to $InstallDir ..."
    Request-Elevation
}

$server = Resolve-ServerSettings

Write-Host "kit:  $KitDir"
Write-Host "to:   $InstallDir"
if ($server.ApiBaseUrl) { Write-Host "api:  $($server.ApiBaseUrl)" }
if ($server.WebUrl) { Write-Host "web:  $($server.WebUrl)" }
Write-Host

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

# Copy/update payload; skip kit docs and this installer.
$excludeNames = @(
    "deploy-windows-client.ps1"
    "INSTALL.txt"
    "README.txt"
    "client-defaults.json.example"
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
    Write-Error "Install failed: $InstalledExe not found after copy."
}

if (-not $NoStartMenu) {
    # Machine install → All Users Start Menu; otherwise current user.
    if ($needsAdmin -or (Test-IsAdministrator)) {
        $startMenuDir = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs"
    }
    else {
        $startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
    }
    New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null

    $lnkPath = Join-Path $startMenuDir $ShortcutName
    $iconPath = Join-Path $InstallDir "Assets\app-icon.ico"
    if (-not (Test-Path $iconPath)) {
        $iconPath = $InstalledExe
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($lnkPath)
    $shortcut.TargetPath = $InstalledExe
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.IconLocation = "$iconPath,0"
    $shortcut.Description = "Schnapp Client for Windows"
    $shortcut.Save()
    Write-Host "Start menu: $lnkPath"

    # Optional browser shortcut to the Razor web UI (not used by the tray API client)
    if (-not $NoWebShortcut -and $server.WebUrl) {
        $webLnk = Join-Path $startMenuDir $WebShortcutName
        $webSc = $shell.CreateShortcut($webLnk)
        $webSc.TargetPath = $server.WebUrl
        $webSc.Description = "Schnapp Web UI"
        $webSc.Save()
        Write-Host "Start menu: $webLnk"
    }

    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
}

# Pre-seed per-user settings (same account that ran the elevated install after UAC)
if ($server.ApiBaseUrl -or $server.LoginIdentity) {
    Merge-ClientSettings -ApiBaseUrlValue $server.ApiBaseUrl -LoginIdentityValue $server.LoginIdentity -ForceApi:$ForceApiBaseUrl
}
elseif (-not $server.ApiBaseUrl) {
    Write-Host "No ApiBaseUrl set -- user will enter the API URL on first login."
    Write-Host "  Tip: .\deploy-windows-client.ps1 -ApiBaseUrl 'http://schnapp:5080' -WebUrl 'https://schnapp/'"
    Write-Host "  Or add client-defaults.json next to this script (see client-defaults.json.example)."
}

Write-Host
Write-Host "installed: $InstalledExe"
Write-Host "run:       $InstalledExe"
Write-Host
Write-Host "Config: %AppData%\Schnapp\settings.json"
Write-Host "Data:   %LocalAppData%\Schnapp\"
if ($server.WebUrl) {
    Write-Host "Web UI: $($server.WebUrl)  (browser; tray client uses ApiBaseUrl)"
}
