<#
.SYNOPSIS
  Orkeon installer (Windows).
.DESCRIPTION
  Copies the archive contents to the install directory (default:
  %LOCALAPPDATA%\Programs\Orkeon), adds its bin\ folder to the user PATH, and
  registers an "Apps & features" (ARP) entry. -Uninstall removes all three and
  never touches %APPDATA%\Orkeon, which is where user configuration lives --
  the InstallDir itself is never a safe place for it, since every (re)install
  deletes it outright.
.PARAMETER InstallDir
  Target directory. Default: $env:LOCALAPPDATA\Programs\Orkeon
.PARAMETER Uninstall
  Removes the install directory, the PATH entry and the ARP entry.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\Orkeon'),
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'

$binDir = Join-Path $InstallDir 'bin'
$configDir = Join-Path $env:APPDATA 'Orkeon'
$arpKeyPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Orkeon'

# --- User PATH (preserves REG_SZ vs REG_EXPAND_SZ) ----------------------------
# [Environment]::SetEnvironmentVariable(...,'User') always rewrites the value
# as REG_SZ, silently destroying any %VAR% expansion other entries relied on.
# Read/write the raw value through the registry directly instead, keeping
# whatever RegistryValueKind was already there (defaulting to ExpandString,
# the kind Windows itself uses, only when the value doesn't exist yet).

function Get-UserPathRaw {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $false)
    if (-not $key) {
        return [PSCustomObject]@{ Value = ''; Kind = [Microsoft.Win32.RegistryValueKind]::ExpandString; Exists = $false }
    }
    try {
        $raw = $key.GetValue('Path', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        if ($null -eq $raw) {
            return [PSCustomObject]@{ Value = ''; Kind = [Microsoft.Win32.RegistryValueKind]::ExpandString; Exists = $false }
        }
        return [PSCustomObject]@{ Value = $raw; Kind = $key.GetValueKind('Path'); Exists = $true }
    } finally {
        $key.Close()
    }
}

function Set-UserPathRaw([string]$Value, [Microsoft.Win32.RegistryValueKind]$Kind) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Environment', $true)
    try {
        $key.SetValue('Path', $Value, $Kind)
    } finally {
        $key.Close()
    }
}

function Add-UserPathEntry([string]$Entry) {
    $current = Get-UserPathRaw
    $parts = @()
    if ($current.Value) { $parts = $current.Value -split ';' | Where-Object { $_ } }
    if ($parts -contains $Entry) { return $false }
    $newValue = if ($current.Value) { $current.Value.TrimEnd(';') + ';' + $Entry } else { $Entry }
    Set-UserPathRaw -Value $newValue -Kind $current.Kind
    return $true
}

function Remove-UserPathEntry([string]$Entry) {
    $current = Get-UserPathRaw
    if (-not $current.Exists -or -not $current.Value) { return }
    $parts = $current.Value -split ';' | Where-Object { $_ -and ($_ -ne $Entry) }
    Set-UserPathRaw -Value ($parts -join ';') -Kind $current.Kind
}

# --- "Apps & features" (ARP) entry --------------------------------------------

function Set-ArpEntry([string]$Dir, [string]$Version, [string]$InstallPs1Path) {
    $files = Get-ChildItem -LiteralPath $Dir -Recurse -File -ErrorAction SilentlyContinue
    $totalBytes = ($files | Measure-Object -Property Length -Sum).Sum
    if (-not $totalBytes) { $totalBytes = 0 }
    $estimatedSizeKb = [int][math]::Round($totalBytes / 1KB)
    # -InstallDir must travel with -Uninstall: a custom install directory
    # would otherwise make Add/Remove Programs target the *default* directory
    # on removal, deleting the wrong folder, stripping the wrong PATH entry,
    # and orphaning the real install.
    $uninstallString = "powershell -ExecutionPolicy Bypass -File `"$InstallPs1Path`" -Uninstall -InstallDir `"$Dir`""

    New-Item -Path $arpKeyPath -Force | Out-Null
    New-ItemProperty -Path $arpKeyPath -Name 'DisplayName' -Value 'Orkeon' -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $arpKeyPath -Name 'DisplayVersion' -Value $Version -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $arpKeyPath -Name 'Publisher' -Value 'Orkeon' -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $arpKeyPath -Name 'InstallLocation' -Value $Dir -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $arpKeyPath -Name 'UninstallString' -Value $uninstallString -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $arpKeyPath -Name 'NoModify' -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $arpKeyPath -Name 'NoRepair' -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $arpKeyPath -Name 'EstimatedSize' -Value $estimatedSizeKb -PropertyType DWord -Force | Out-Null
}

function Remove-ArpEntry {
    if (Test-Path -LiteralPath $arpKeyPath) { Remove-Item -LiteralPath $arpKeyPath -Recurse -Force }
}

# "One channel at a time" (WIN-00-PLAN.md, decision #5), reciprocal half:
# the MSI's own Launch Condition refuses to install next to this script's ARP
# key (a literal "Orkeon" key name); this is the other direction -- refuse to
# install the ZIP over a live MSI install before the delete-and-replace below
# overwrites its file tree and orphans its Windows Installer registration. MSI
# products register under a ProductCode GUID key, never under a literal name,
# so any GUID-named sibling with DisplayName "Orkeon" is the MSI channel.
function Test-MsiChannelInstalled {
    $uninstallRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
    if (-not (Test-Path -LiteralPath $uninstallRoot)) { return $false }
    $guidPattern = '^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$'
    $productKeys = Get-ChildItem -LiteralPath $uninstallRoot -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match $guidPattern }
    foreach ($productKey in $productKeys) {
        $displayName = (Get-ItemProperty -LiteralPath $productKey.PSPath -ErrorAction SilentlyContinue).DisplayName
        if ($displayName -eq 'Orkeon') { return $true }
    }
    return $false
}

# --- Legacy user-config migration guard ---------------------------------------
# Installs that predate this change may have written appsettings.json straight
# into the InstallDir. Rescue it into %APPDATA%\Orkeon before the
# delete-and-replace below wipes the old InstallDir out.

function Move-LegacyUserConfig([string]$OldInstallDir, [string]$ConfigDir) {
    $legacyConfig = Join-Path $OldInstallDir 'appsettings.json'
    if (-not (Test-Path -LiteralPath $legacyConfig)) { return }

    New-Item -ItemType Directory -Force -Path $ConfigDir | Out-Null
    $dest = Join-Path $ConfigDir 'appsettings.json'
    if (Test-Path -LiteralPath $dest) {
        $dest = Join-Path $ConfigDir 'appsettings.json.migrated'
        $suffix = 1
        while (Test-Path -LiteralPath $dest) {
            $dest = Join-Path $ConfigDir "appsettings.json.migrated.$suffix"
            $suffix++
        }
    }
    Move-Item -LiteralPath $legacyConfig -Destination $dest -Force
    Write-Host "Found a user config in the previous install directory; moved it to $dest"
}

# --- Uninstall ------------------------------------------------------------------

if ($Uninstall) {
    if (Test-Path -LiteralPath $InstallDir) { Remove-Item -LiteralPath $InstallDir -Recurse -Force }
    Remove-UserPathEntry $binDir
    Remove-ArpEntry
    Write-Host "Orkeon uninstalled from $InstallDir."
    Write-Host "Your configuration in $configDir was left untouched."
    return
}

# --- Install ----------------------------------------------------------------

$src = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not (Test-Path (Join-Path $src 'bin')) -or -not (Test-Path (Join-Path $src 'libexec'))) {
    throw "Run this script from the extracted archive root (bin\ and libexec\ not found)."
}

# Anti-self-destruction guard: install.ps1 copies itself into $InstallDir (see
# below) precisely so it can be re-run from there later with -Uninstall. But
# running it from there WITHOUT -Uninstall would have the delete-and-replace
# step erase its own source before the copy step could read from it, leaving
# behind a silently empty install.
if ([IO.Path]::GetFullPath($src).TrimEnd('\') -ieq [IO.Path]::GetFullPath($InstallDir).TrimEnd('\')) {
    throw "This script is running from the install directory itself ($InstallDir). Extract the archive somewhere else (e.g. a Downloads folder) and run install.ps1 from there -- or use -Uninstall if that is what you meant."
}

if (Test-MsiChannelInstalled) {
    throw "Orkeon is already installed via the MSI channel (see 'Orkeon' in Add/Remove Programs). Uninstall it first, then run this installer again -- only one Orkeon channel can be active at a time."
}

if (Test-Path -LiteralPath $InstallDir) {
    Move-LegacyUserConfig -OldInstallDir $InstallDir -ConfigDir $configDir
}

# Delete-and-replace for clean upgrades. install.ps1 is copied alongside the
# rest so -Uninstall keeps working later even if the extracted archive is
# long gone (the ARP UninstallString points at this copy). appsettings.sample
# .json (when the archive ships one) is reference-only -- real user config
# always lives under %APPDATA%\Orkeon, never here.
if (Test-Path -LiteralPath $InstallDir) { Remove-Item -LiteralPath $InstallDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
foreach ($item in 'bin', 'libexec', 'README.md', 'LICENSE.md', 'install.ps1', 'VERSION', 'appsettings.sample.json') {
    $p = Join-Path $src $item
    if (Test-Path -LiteralPath $p) { Copy-Item -LiteralPath $p -Destination $InstallDir -Recurse -Force }
}

Write-Host "Orkeon installed to $InstallDir"
Write-Host ("Commands: " + ((Get-ChildItem -LiteralPath $binDir -Filter '*.cmd').BaseName -join ' '))

# .NET 10 runtime check -- only meaningful for framework-dependent binaries.
# Self-contained publishes bundle the runtime, hostfxr.dll included. The
# multi-app archive mixes both kinds (`orkeon` is self-contained; orkeon-slim
# and orkeon-repl are not), so every app directory under libexec\
# is checked -- framework-dependent as soon as ONE of them lacks hostfxr.dll.
# esbuild-bin holds no .NET app and is skipped; no app directories at all
# means nothing to warn about. POSIX mirror: needs_dotnet_runtime in install.sh.
$libexecRoot = Join-Path $InstallDir 'libexec'
$isFrameworkDependent = $false
if (Test-Path -LiteralPath $libexecRoot) {
    $appDirs = Get-ChildItem -LiteralPath $libexecRoot -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne 'esbuild-bin' }
    foreach ($app in $appDirs) {
        if (-not (Test-Path -LiteralPath (Join-Path $app.FullName 'hostfxr.dll'))) {
            $isFrameworkDependent = $true
            break
        }
    }
}
if ($isFrameworkDependent) {
    $hasRuntime = $false
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        # Under $ErrorActionPreference = 'Stop', a native command that writes
        # to stderr while redirected (2>$null redirects, does not silence, in
        # Windows PowerShell 5.1) raises a terminating NativeCommandError.
        # Relax the preference locally for this one call, as run-smoke.ps1's
        # Invoke-Orkeon does for the same reason.
        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $hasRuntime = (dotnet --list-runtimes 2>$null) -match 'Microsoft\.NETCore\.App 10\.'
        } catch {
            $hasRuntime = $false
        } finally {
            $ErrorActionPreference = $previousPreference
        }
    }
    if (-not $hasRuntime) {
        Write-Warning ".NET 10 runtime not found. These binaries require it. Install it from https://dotnet.microsoft.com/download/dotnet/10.0"
    }
}

# Idempotent user PATH update.
if (Add-UserPathEntry $binDir) {
    Write-Host "Added $binDir to the user PATH (open a new terminal to use it)."
}

# "Apps & features" entry -- written last, only once the install above has
# fully succeeded (the script would already have thrown otherwise).
$versionFile = Join-Path $InstallDir 'VERSION'
$version = 'unknown'
if (Test-Path -LiteralPath $versionFile) {
    $fileVersion = (Get-Content -LiteralPath $versionFile -Raw).Trim()
    if ($fileVersion) { $version = $fileVersion }
}
Set-ArpEntry -Dir $InstallDir -Version $version -InstallPs1Path (Join-Path $InstallDir 'install.ps1')

Write-Host ""
Write-Host "Open a new terminal, then:"
Write-Host "  orkeon init"
Write-Host "  orkeon doctor"
Write-Host "  orkeon run <crew.yaml>"
