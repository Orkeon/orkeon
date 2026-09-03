<#
.SYNOPSIS
  Builds the per-machine Orkeon Service Host MSI (WINSVC-02 P3) from an
  extracted FULL win-x64 archive tree, via the WiX Toolset.
.DESCRIPTION
  Compiles installers\msi\PackageService.wxs against -StageDir and produces
  artifacts\installers\orkeon-host-<ver>-win-x64.msi, then APPENDS its line to
  SHA256SUMS.msi — one checksum file covering both MSIs, generated in order by
  the same `msi` job (build-msi.ps1 writes the file, this script appends).

  Deliberately a separate script from build-msi.ps1 rather than a
  parameterization of it: the -StageDir contract differs (full-archive tree
  with libexec\orkeon-host\ vs cli tree), the product and filename differ, and
  the checksum behavior differs (append vs write). The ~40 lines of wix
  plumbing are duplicated knowingly; the version pin and the v5-not-v6/v7
  reasoning live in build-msi.ps1's header and .config\dotnet-tools.json.
.PARAMETER StageDir
  The extracted FULL archive's inner folder — the one that directly contains
  libexec\orkeon-host\orkeon-host.exe, LICENSE.md and VERSION. Mandatory: there
  is no "build one fresh" fallback here, the CI job always has the tree.
.PARAMETER Version
  Full version string. Default: StageDir\VERSION. Truncated to x.y.z for the
  MSI ProductVersion; the full string survives in the filename and ARPCOMMENTS.
.PARAMETER OutDir
  Output directory. Default: artifacts\installers.
.EXAMPLE
  pwsh scripts/build-msi-service.ps1 -StageDir artifacts\installers\_extract_full\orkeon-1.0.0-rc.3-win-x64
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$StageDir,
    [string]$Version = '',
    [string]$OutDir = ''
)
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$MsiSrc = Join-Path $RepoRoot 'installers\msi'
if (-not $OutDir) { $OutDir = Join-Path $RepoRoot 'artifacts\installers' }
if (-not [System.IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $RepoRoot $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path

# --- Staging tree ---------------------------------------------------------------
$StageDir = (Resolve-Path -LiteralPath $StageDir).Path
$hostPublishDir = Join-Path $StageDir 'libexec\orkeon-host'
if (-not (Test-Path (Join-Path $hostPublishDir 'orkeon-host.exe'))) {
    throw "StageDir '$StageDir' has no libexec\orkeon-host\orkeon-host.exe -- pass the FULL archive's inner folder (the cli archive carries no service host)."
}
foreach ($required in @('LICENSE.md', 'VERSION', 'appsettings.sample.json')) {
    if (-not (Test-Path (Join-Path $StageDir $required))) { throw "StageDir '$StageDir' has no $required." }
}

# --- Version ----------------------------------------------------------------
if (-not $Version) {
    $Version = (Get-Content -LiteralPath (Join-Path $StageDir 'VERSION') -Raw).Trim()
}
if ($Version -notmatch '^(\d+\.\d+\.\d+)') {
    throw "Version '$Version' does not start with x.y.z -- cannot derive an MSI ProductVersion from it."
}
$MsiVersion = $Matches[1]
if ($MsiVersion -ne $Version) {
    Write-Host "==> MSI ProductVersion truncated from '$Version' to '$MsiVersion' (MSI carries no suffix); the full string is kept in the .msi filename and ARPCOMMENTS."
}

# --- wix CLI -------------------------------------------------------------------
# Same pin-resolution as build-msi.ps1: the local tool manifest wins, the
# literal fallback only fires outside a checkout. v5, not v6/v7 (OSMF EULA
# gate WIX7015 on fresh runners) — full reasoning in build-msi.ps1.
$manifestPath = Join-Path $RepoRoot '.config\dotnet-tools.json'
$useLocalManifest = Test-Path -LiteralPath $manifestPath
if ($useLocalManifest) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $WixVersion = $manifest.tools.wix.version
    if (-not $WixVersion) { throw "No tools.wix.version in $manifestPath" }
} else {
    $WixVersion = '5.0.2'
}

Push-Location $RepoRoot
try {
    if ($useLocalManifest) {
        Write-Host '==> dotnet tool restore (local manifest)'
        dotnet tool restore
        if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed' }
        function Invoke-Wix {
            & dotnet tool run wix -- @args
        }
    } else {
        Write-Host "==> dotnet tool install --global wix --version $WixVersion"
        dotnet tool install --global wix --version $WixVersion
        if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 1) { throw 'dotnet tool install --global wix failed' }
        function Invoke-Wix {
            & wix @args
        }
    }

    # UI for the license page, Util for ServiceConfig/PermissionEx/EventSource.
    foreach ($ext in @('WixToolset.UI.wixext', 'WixToolset.Util.wixext')) {
        Write-Host "==> wix extension add $ext/$WixVersion"
        Invoke-Wix extension add --global "$ext/$WixVersion"
        if ($LASTEXITCODE -ne 0) { throw "wix extension add $ext failed" }
    }

    $msiName = "orkeon-host-$Version-win-x64.msi"
    $msiPath = Join-Path $OutDir $msiName
    if (Test-Path -LiteralPath $msiPath) { Remove-Item -LiteralPath $msiPath -Force }

    Write-Host "==> wix build -> $msiPath"
    # -bindpath installers\msi: License.rtf is referenced with no folder prefix
    # (same sharing as Package.wxs — one license, two products).
    Invoke-Wix build `
        -arch x64 `
        -d "OrkeonVersion=$MsiVersion" `
        -d "OrkeonVersionFull=$Version" `
        -d "HostPublishDir=$hostPublishDir" `
        -d "StageRoot=$StageDir" `
        -ext WixToolset.UI.wixext `
        -ext WixToolset.Util.wixext `
        -bindpath $MsiSrc `
        -o $msiPath `
        (Join-Path $MsiSrc 'PackageService.wxs')
    if ($LASTEXITCODE -ne 0) { throw 'wix build failed' }
    if (-not (Test-Path -LiteralPath $msiPath)) { throw "wix build reported success but $msiPath is missing" }
} finally {
    Pop-Location
}

# --- Checksum ------------------------------------------------------------------
# APPEND to SHA256SUMS.msi: build-msi.ps1 wrote the CLI MSI's line first in the
# same job, and one file must cover the two MSIs. LF-terminated for
# `sha256sum -c` on Linux.
$hash = (Get-FileHash $msiPath -Algorithm SHA256).Hash.ToLower()
$sumsLine = "{0}  {1}" -f $hash, $msiName
$sumsFile = Join-Path $OutDir 'SHA256SUMS.msi'
[IO.File]::AppendAllText($sumsFile, "$sumsLine`n")
Write-Host "==> Done. $msiPath"
Write-Host "    $sumsLine"
