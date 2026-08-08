<#
.SYNOPSIS
  Builds the per-user Orkeon MSI (WIN-07) from a package-installers.ps1
  -AppSet cli staging tree, via the WiX Toolset.
.DESCRIPTION
  Harvests installers\msi\Package.wxs against -StageDir (bin\, libexec\,
  README.md, LICENSE.md, VERSION, appsettings.sample.json — the exact tree
  package-installers.ps1 -AppSet cli zips up) and produces
  artifacts\installers\orkeon-<ver>-win-x64.msi, plus a SHA256SUMS.msi entry
  (kept separate from the main SHA256SUMS: that one is generated on the
  ubuntu `installers` job, before this MSI exists — see release.yml).

  Prefers a local `dotnet tool restore` (this repo already has a root
  .config\dotnet-tools.json) over a global `dotnet tool install`, so the wix
  version stays pinned per-repo like every other tool here.
.PARAMETER StageDir
  A staging tree with bin\ and libexec\ directly at its root — either the
  orkeon-cli-*-win-x64.zip already extracted (pass the archive's single inner
  folder, not the zip's own extraction root — see run-smoke.ps1 for the same
  pattern), or omit this parameter to have this script build one fresh via
  package-installers.ps1 -AppSet cli.
.PARAMETER Version
  Full version string (e.g. 0.9.2-beta). Default: StageDir\VERSION. MSI
  ProductVersion accepts no suffix, so it is truncated to x.y.z for
  -d OrkeonVersion; the full string survives in the .msi filename and in
  ARPCOMMENTS (see Package.wxs) — reformatted, never silently dropped.
.PARAMETER OutDir
  Output directory. Default: artifacts\installers.
.EXAMPLE
  pwsh scripts/build-msi.ps1
.EXAMPLE
  pwsh scripts/build-msi.ps1 -StageDir artifacts\installers\_extract\orkeon-cli-0.9.2-beta-win-x64
#>
[CmdletBinding()]
param(
    [string]$StageDir = '',
    [string]$Version = '',
    [string]$OutDir = ''
)
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$MsiSrc = Join-Path $RepoRoot 'installers\msi'
if (-not $OutDir) { $OutDir = Join-Path $RepoRoot 'artifacts\installers' }
# Absolutized *before* the Push-Location below: Get-FileHash at the very end
# runs after Pop-Location has restored the caller's own working directory, so
# a relative -OutDir (release.yml passes "artifacts\installers") would then
# resolve against the wrong directory.
if (-not [System.IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $RepoRoot $OutDir }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path -LiteralPath $OutDir).Path

# --- Staging tree ---------------------------------------------------------------
if (-not $StageDir) {
    Write-Host '==> No -StageDir given: building one via package-installers.ps1 -AppSet cli'
    $buildOut = Join-Path $OutDir '_msi-stage'
    if (Test-Path $buildOut) { Remove-Item -Recurse -Force $buildOut }
    # Hashtable splat, not an array: splatting an array binds POSITIONALLY,
    # which shoved '-Out' into -AppSet on the first real Windows run.
    $psSplat = @{ AppSet = 'cli'; Rids = @('win-x64'); Out = $buildOut }
    if ($Version) { $psSplat.Version = $Version }
    & (Join-Path $RepoRoot 'scripts\package-installers.ps1') @psSplat
    if ($LASTEXITCODE -ne 0) { throw 'package-installers.ps1 failed' }
    $inner = Get-ChildItem (Join-Path $buildOut '_stage') -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $inner) { throw "No staging tree produced under $buildOut\_stage" }
    $StageDir = $inner.FullName
}

$StageDir = (Resolve-Path -LiteralPath $StageDir).Path
if (-not (Test-Path (Join-Path $StageDir 'bin')) -or -not (Test-Path (Join-Path $StageDir 'libexec'))) {
    throw "StageDir '$StageDir' has no bin\ / libexec\ directly at its root -- pass the archive's inner folder (the one that directly contains bin\ and libexec\), not the zip's own extraction root."
}

# --- Version ----------------------------------------------------------------
if (-not $Version) {
    $versionFile = Join-Path $StageDir 'VERSION'
    if (Test-Path -LiteralPath $versionFile) {
        $Version = (Get-Content -LiteralPath $versionFile -Raw).Trim()
    }
}
if (-not $Version) { throw 'Could not resolve a version; pass -Version or ensure VERSION exists in StageDir.' }

if ($Version -notmatch '^(\d+\.\d+\.\d+)') {
    throw "Version '$Version' does not start with x.y.z -- cannot derive an MSI ProductVersion from it."
}
$MsiVersion = $Matches[1]
if ($MsiVersion -ne $Version) {
    Write-Host "==> MSI ProductVersion truncated from '$Version' to '$MsiVersion' (MSI carries no suffix); the full string is kept in the .msi filename and ARPCOMMENTS."
}

# --- wix CLI -------------------------------------------------------------------
# Local manifest (.config\dotnet-tools.json, already used for dotnet-ef) wins
# when present, so wix stays version-pinned per-repo like every other tool
# here; falls back to a global install otherwise. Extensions are cached per
# user profile (~/.wix) either way, so `wix extension add` is unconditional.
#
# WiX v5 pinned, not v6/v7: verified empirically that `wix extension add` and
# `wix build` on v7 both fail with WIX7015 ("You must accept the Open Source
# Maintenance Fee (OSMF) EULA") on a fresh runner that has never accepted it --
# a CI machine has no prior acceptance file, so the `msi` job would die before
# ever building anything, and `release` never publishes. Rather than have this
# script accept a licence on the project's behalf inside a CI job,
# .config\dotnet-tools.json pins the last v5.x release (5.0.2), which predates
# the OSMF gate entirely -- matches the fiche's own "WiX v5/v6" wording.
# Moving to v7 is possible later (either `-acceptEula` on the CLI or
# `<AcceptEula>` in a project file) but that is a licensing call for the team,
# not something to bake into an unattended build script.
#
# The version below is read from the manifest rather than hardcoded again
# here, so there is exactly one place (.config\dotnet-tools.json) that pins
# it; the literal '5.0.2' fallback only fires if that manifest is missing
# (e.g. this script run outside a checkout of this repo).
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
        # exit 1 here commonly means "already installed" -- not fatal, wix on
        # PATH is what actually matters below.
        function Invoke-Wix {
            & wix @args
        }
    }

    Write-Host "==> wix extension add WixToolset.UI.wixext/$WixVersion"
    Invoke-Wix extension add --global "WixToolset.UI.wixext/$WixVersion"
    if ($LASTEXITCODE -ne 0) { throw 'wix extension add WixToolset.UI.wixext failed' }

    $msiName = "orkeon-$Version-win-x64.msi"
    $msiPath = Join-Path $OutDir $msiName
    if (Test-Path -LiteralPath $msiPath) { Remove-Item -LiteralPath $msiPath -Force }

    Write-Host "==> wix build -> $msiPath"
    # -bindpath installers\msi: Package.wxs is compiled with the repo root as
    # the working directory (this Push-Location), but its WixVariable
    # WixUILicenseRtf points at "License.rtf" with no folder prefix -- that
    # relative reference resolves against bindpaths first, and without this
    # one wix looks for it at the repo root instead of installers\msi\.
    Invoke-Wix build `
        -arch x64 `
        -d "OrkeonVersion=$MsiVersion" `
        -d "OrkeonVersionFull=$Version" `
        -d "PublishDir=$StageDir" `
        -ext WixToolset.UI.wixext `
        -bindpath $MsiSrc `
        -o $msiPath `
        (Join-Path $MsiSrc 'Package.wxs')
    if ($LASTEXITCODE -ne 0) { throw 'wix build failed' }
    if (-not (Test-Path -LiteralPath $msiPath)) { throw "wix build reported success but $msiPath is missing" }
} finally {
    Pop-Location
}

# --- Checksum ------------------------------------------------------------------
# Separate SHA256SUMS.msi rather than appending to the ubuntu job's SHA256SUMS:
# this script runs later, on windows-latest, after that file already exists
# and has been uploaded as a job artefact (see release.yml, job `msi`).
$hash = (Get-FileHash $msiPath -Algorithm SHA256).Hash.ToLower()
$sumsLine = "{0}  {1}" -f $hash, $msiName
$sumsFile = Join-Path $OutDir 'SHA256SUMS.msi'
# LF-terminated, no CRLF: `sha256sum -c` on Linux treats a trailing \r as part
# of the file name and reports "no such file".
[IO.File]::WriteAllText($sumsFile, "$sumsLine`n")
Write-Host "==> Done. $msiPath"
Write-Host "    $sumsLine"
