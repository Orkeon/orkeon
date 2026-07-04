<#
.SYNOPSIS
  Orkeon installer (Windows).
.DESCRIPTION
  Copies the archive contents to the install directory (default:
  %LOCALAPPDATA%\Programs\Orkeon) and adds its bin\ folder to the user PATH.
.PARAMETER InstallDir
  Target directory. Default: $env:LOCALAPPDATA\Programs\Orkeon
.PARAMETER Uninstall
  Removes the install directory and the PATH entry.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\Orkeon'),
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'

$binDir = Join-Path $InstallDir 'bin'

function Remove-UserPathEntry([string]$entry) {
    $path = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (-not $path) { return }
    $parts = $path -split ';' | Where-Object { $_ -and ($_ -ne $entry) }
    [Environment]::SetEnvironmentVariable('Path', ($parts -join ';'), 'User')
}

if ($Uninstall) {
    if (Test-Path $InstallDir) { Remove-Item -Recurse -Force $InstallDir }
    Remove-UserPathEntry $binDir
    Write-Host "Orkeon uninstalled from $InstallDir."
    return
}

$src = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not (Test-Path (Join-Path $src 'bin')) -or -not (Test-Path (Join-Path $src 'libexec'))) {
    throw "Run this script from the extracted archive root (bin\ and libexec\ not found)."
}

# Delete-and-replace for clean upgrades.
if (Test-Path $InstallDir) { Remove-Item -Recurse -Force $InstallDir }
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
foreach ($item in 'bin', 'libexec', 'README.md', 'LICENSE.md') {
    $p = Join-Path $src $item
    if (Test-Path $p) { Copy-Item -Recurse -Force $p $InstallDir }
}

Write-Host "Orkeon installed to $InstallDir"
Write-Host ("Commands: " + ((Get-ChildItem $binDir -Filter '*.cmd').BaseName -join ' '))

# .NET 10 runtime check (framework-dependent binaries).
$hasRuntime = $false
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $hasRuntime = (dotnet --list-runtimes 2>$null) -match 'Microsoft\.NETCore\.App 10\.'
}
if (-not $hasRuntime) {
    Write-Warning ".NET 10 runtime not found. These binaries require it. Install it from https://dotnet.microsoft.com/download/dotnet/10.0"
}

# Idempotent user PATH update.
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if (($userPath -split ';') -notcontains $binDir) {
    [Environment]::SetEnvironmentVariable('Path', ($userPath.TrimEnd(';') + ';' + $binDir), 'User')
    Write-Host "Added $binDir to the user PATH (open a new terminal to use it)."
}
