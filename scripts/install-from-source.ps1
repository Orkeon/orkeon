<#
.SYNOPSIS
  Installs Orkeon from this clone, in one command (Windows;
  scripts/install-from-source.sh is the same contract for Linux and macOS).
.DESCRIPTION
    git clone https://github.com/Orkeon/orkeon.git
    cd orkeon
    .\scripts\install-from-source.ps1

  What it does, in this order:
    1. checks what the build needs -- every missing tool is named at once, with
       where to get it, before anything is compiled;
    2. computes the version of this checkout (scripts\resolve-version.ps1):
       <props version>.local.<commit date> off a tag, never the name of a release;
    3. builds the tree of the Windows archive, without writing the archive
       (scripts\package-installers.ps1 -NoArchive);
    4. installs that tree with the install.ps1 every archive carries, and marks the
       installation `source`, the channel `orkeon doctor` then names.

  It installs what the released zip installs -- same layout, same launchers, Orkeon
  Studio and esbuild included -- and nothing else: no runtime, no tool, no package is
  installed on the machine for you.

  Run it again after a `git pull`: the installation is replaced.

  This file is written for Windows PowerShell 5.1 as well as PowerShell 7, and stays
  ASCII, for one reason: under 5.1 it has to get as far as saying that the build
  needs PowerShell 7, and where to get it. A #Requires line would stop it without
  that second half.
.PARAMETER AppSet
  cli (the default): the `orkeon` CLI and Orkeon Studio. full: every launcher (the
  REPL, the service host).
.PARAMETER InstallDir
  Passed to install.ps1. Default: %LOCALAPPDATA%\Programs\Orkeon.
.PARAMETER Uninstall
  Removes what is installed in -InstallDir. Builds nothing.
.PARAMETER WhatIf
  Checks the prerequisites, prints the version and the commands it would run, and
  stops there.
.NOTES
  Exit: 0 installed (or would be, with -WhatIf); 1 a prerequisite is missing, or the
  build or the install failed.
#>
[CmdletBinding()]
param(
    [ValidateSet('cli', 'full')]
    [string]$AppSet = 'cli',
    [string]$InstallDir = '',
    [switch]$Uninstall,
    [switch]$WhatIf
)
$ErrorActionPreference = 'Stop'

function Write-Problem([string]$Message) {
    [Console]::Error.WriteLine($Message)
}

# --- Which PowerShell ---------------------------------------------------------------
# By code, before anything else: the packaging reads a lockfile in a way Windows
# PowerShell 5.1 cannot (ConvertFrom-Json -AsHashtable).
if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Problem "install-from-source: building Orkeon needs PowerShell 7 (pwsh). This is Windows PowerShell $($PSVersionTable.PSVersion), the one installed with Windows."
    Write-Problem '  Get PowerShell 7 from https://aka.ms/powershell-release?tag=stable  (or: winget install --id Microsoft.PowerShell)'
    Write-Problem '  then, from this clone:  pwsh -File .\scripts\install-from-source.ps1'
    Write-Problem 'Nothing was built. (Installing a released zip needs neither: its install.cmd runs on this PowerShell.)'
    exit 1
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    Write-Problem 'install-from-source: this script installs on Windows. On Linux and macOS, run scripts/install-from-source.sh.'
    exit 1
}

$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not $InstallDir) { $InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\Orkeon' }

# --- Uninstall: nothing to build ------------------------------------------------------
# The installed tree carries its own install.ps1 for this (it travels with the rest).
if ($Uninstall) {
    $installed = Join-Path $InstallDir 'install.ps1'
    if (-not (Test-Path -LiteralPath $installed)) {
        Write-Problem "Nothing to uninstall: no Orkeon installation in $InstallDir (looked for $installed)."
        exit 1
    }
    if ($WhatIf) {
        Write-Host "Would run: $installed -InstallDir `"$InstallDir`" -Uninstall"
        exit 0
    }
    try {
        & $installed -InstallDir $InstallDir -Uninstall
    } catch {
        # install.ps1's own sentence, as it wrote it.
        Write-Problem $_.Exception.Message
        exit 1
    }
    exit 0
}

# --- Prerequisites, all of them, before anything is compiled ------------------------------
# One list of what is missing, each line with where to get it -- not one error per run.

# The exit code of a native command, its output dropped. The preference is relaxed
# around it: before PowerShell 7.2, a native command that writes to a redirected
# stderr raises under 'Stop'.
function Get-NativeExitCode([scriptblock]$Command) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Command *> $null
        return $LASTEXITCODE
    } catch {
        return -1
    } finally {
        $ErrorActionPreference = $previous
    }
}

$sdkWanted = '10.0.300'
try {
    $pinned = (Get-Content -LiteralPath (Join-Path $RepoRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
    if ($pinned) { $sdkWanted = [string]$pinned }
} catch {
    $sdkWanted = '10.0.300'
}
$sdkLine = ($sdkWanted -split '\.')[0..1] -join '.'
$sdkPage = "https://dotnet.microsoft.com/download/dotnet/$sdkLine"

$missing = New-Object System.Collections.Generic.List[string]

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $missing.Add("the .NET SDK $sdkWanted or a later $sdkLine.x (global.json pins it) -- $sdkPage")
} else {
    # dotnet itself judges global.json: from the clone it answers --version only when
    # an installed SDK satisfies the pin. A runtime without an SDK fails here too.
    Push-Location -LiteralPath $RepoRoot
    try { $sdkFits = (Get-NativeExitCode { dotnet --version }) -eq 0 } finally { Pop-Location }
    if (-not $sdkFits) {
        $have = ''
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            Push-Location -LiteralPath ([IO.Path]::GetPathRoot($RepoRoot))
            try { $have = (@(dotnet --list-sdks 2>$null) | ForEach-Object { ([string]$_ -split ' ')[0] }) -join ' ' } finally { Pop-Location }
        } catch {
            $have = ''
        } finally {
            $ErrorActionPreference = $previous
        }
        if (-not $have) { $have = 'none' }
        $missing.Add("a .NET SDK that satisfies global.json: $sdkWanted or a later $sdkLine.x (installed: $have) -- $sdkPage")
    }
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    $missing.Add('git, which dates the version of this build -- https://git-scm.com/download/win')
}

# Run, not just found: the Microsoft Store alias named python only prints a hint.
$pythonFits = $false
if (Get-Command python -ErrorAction SilentlyContinue) {
    $pythonFits = (Get-NativeExitCode { python -c 'import sys; sys.exit(sys.version_info[0] != 3)' }) -eq 0
}
if (-not $pythonFits) {
    $missing.Add("Python 3 as 'python' on the PATH (the Microsoft Store alias of that name does not count), which copies the .NET runtime's license and notices into the tree -- https://www.python.org/downloads/")
}

if (-not (Get-Command tar -ErrorAction SilentlyContinue)) {
    $missing.Add('tar, which unpacks the esbuild binary the tree bundles -- it ships with Windows 10 (1803) and later as tar.exe; Git for Windows carries one too')
}

if ($missing.Count -gt 0) {
    Write-Problem "install-from-source: this machine lacks $($missing.Count) thing(s) the build needs. Nothing was built."
    foreach ($item in $missing) { Write-Problem "  - $item" }
    Write-Problem 'Install what is listed, then run this script again. It installs none of it for you.'
    exit 1
}

# --- Version ---------------------------------------------------------------------------
$version = & (Join-Path $PSScriptRoot 'resolve-version.ps1') -Props (Join-Path $RepoRoot 'src\Directory.Build.props')
if ($LASTEXITCODE -ne 0 -or -not $version) {
    Write-Problem 'install-from-source: scripts\resolve-version.ps1 gave no version for this checkout (see its message above).'
    exit 1
}
$version = ([string]$version).Trim()

$rid = 'win-x64'
if ($AppSet -eq 'cli') { $packagePrefix = 'orkeon-cli' } else { $packagePrefix = 'orkeon' }
$out = Join-Path $RepoRoot 'artifacts\installers'
$tree = Join-Path $out "_stage\$packagePrefix-$version-$rid"
$packager = Join-Path $PSScriptRoot 'package-installers.ps1'
$installer = Join-Path $tree 'install.ps1'

Write-Host "Orkeon $version, from the sources in $RepoRoot ($AppSet set, $rid)."

if ($WhatIf) {
    Write-Host 'Prerequisites: all present. -WhatIf: nothing is built, nothing is installed. Would run:'
    Write-Host "  $packager -AppSet $AppSet -Rids $rid -Version $version -Out `"$out`" -NoArchive"
    Write-Host "  $installer -InstallDir `"$InstallDir`""
    exit 0
}

# --- Build the tree ----------------------------------------------------------------------
Write-Host ''
Write-Host "==> Building the $rid tree (several minutes: the CLI and Orkeon Studio are each published self-contained)"
Write-Host 'The first run restores NuGet packages from nuget.org and fetches the esbuild binary from'
Write-Host 'registry.npmjs.org; a later run downloads nothing that is already cached.'
try {
    & $packager -AppSet $AppSet -Rids $rid -Version $version -Out $out -NoArchive
} catch {
    Write-Problem "install-from-source: the build stopped -- $($_.Exception.Message)"
    exit 1
}
if (-not (Test-Path -LiteralPath $installer)) {
    Write-Problem "install-from-source: package-installers.ps1 ended without the tree it was asked for ($tree)."
    exit 1
}

# The channel is said by whoever packs (Orkeon.Constants.FileSystem.InstallChannels):
# package-installers.ps1 wrote `zip`, the word of the archive this tree would have
# become. It never became one: this installation comes from a clone.
[IO.File]::WriteAllText((Join-Path $tree 'INSTALL-CHANNEL'), "source`n")

# --- Install it ----------------------------------------------------------------------------
Write-Host ''
Write-Host "==> Installing to $InstallDir"
try {
    & $installer -InstallDir $InstallDir
} catch {
    # install.ps1's own sentence, as it wrote it: the MSI channel is in place, Orkeon is
    # running from the install directory, ...
    Write-Problem $_.Exception.Message
    exit 1
}

Write-Host ''
Write-Host "Orkeon $version is installed from the sources (channel: source)."
Write-Host 'Open a new terminal, then: orkeon --version'
Write-Host "After a 'git pull', run this script again: it replaces the installation."
