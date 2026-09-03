<#
.SYNOPSIS
  Builds per-OS installer archives containing all Orkeon CLI executables,
  published per RID (some self-contained, some framework-dependent — see the
  $Apps table). PowerShell mirror of scripts/package-installers.sh, intended
  for local Windows use.
.PARAMETER Version
  Package version. Default: git describe (v-stripped), then src/Directory.Build.props.
.PARAMETER Rids
  RIDs to package. Default: win-x64 only. Unix RIDs are refused unless -Force:
  archives produced on Windows lose the executable bits — build those on
  Linux/WSL/CI with package-installers.sh instead.
.PARAMETER Out
  Output directory. Default: artifacts\installers.
.PARAMETER AppSet
  Which apps go in the archive. 'full' (default) keeps the historical every-app
  archive; 'cli' ships the `orkeon` onboarding binary plus the Orkeon Studio
  apps for the platform (win-x64: orkeon-studio; linux-*: orkeon-studio-config +
  orkeon-studio-run; osx-*: CLI only) as orkeon-cli-<ver>-<rid>.zip.
#>
[CmdletBinding()]
param(
    [string]$Version = '',
    [string[]]$Rids = @('win-x64'),
    [string]$Out = '',
    [string]$Configuration = 'Release',
    [ValidateSet('full', 'cli')]
    [string]$AppSet = 'full',
    [switch]$Force
)
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Assets = Join-Path $RepoRoot 'scripts\installer-assets'
if (-not $Out) { $Out = Join-Path $RepoRoot 'artifacts\installers' }

$unixRids = $Rids | Where-Object { $_ -notlike 'win-*' }
if ($unixRids -and -not $Force) {
    throw "Unix RIDs ($($unixRids -join ', ')) would lose executable bits when archived on Windows. Build them with scripts/package-installers.sh (Linux/WSL/CI), or pass -Force."
}

# --- Version -----------------------------------------------------------------
if (-not $Version) {
    $tag = git -C $RepoRoot describe --tags --abbrev=0 2>$null
    if ($LASTEXITCODE -eq 0 -and $tag) { $Version = $tag -replace '^v', '' }
}
if (-not $Version) {
    $props = Get-Content (Join-Path $RepoRoot 'src\Directory.Build.props') -Raw
    $prefix = [regex]::Match($props, '<VersionPrefix>(.*?)</VersionPrefix>').Groups[1].Value
    $suffix = [regex]::Match($props, '<VersionSuffix>(.*?)</VersionSuffix>').Groups[1].Value
    $Version = if ($suffix) { "$prefix-$suffix" } else { $prefix }
}
if (-not $Version) { throw 'Could not resolve a version; pass -Version.' }

# --- esbuild version from the lockfile ----------------------------------------
$EsbuildVersion = '0.25.12'
$lock = Join-Path $RepoRoot 'tools\scripting-esbuild\package-lock.json'
$lockJson = $null
if (Test-Path $lock) {
    # npm lockfiles key the root package on "" — ConvertFrom-Json only accepts
    # empty property names with -AsHashtable (PowerShell 7.3+ throws otherwise).
    $lockJson = Get-Content $lock -Raw | ConvertFrom-Json -AsHashtable
    $pkg = $lockJson['packages']['node_modules/esbuild']
    if ($pkg -and $pkg['version']) { $EsbuildVersion = $pkg['version'] }
}

# --- App table -----------------------------------------------------------------
# SelfContained = $true bundles the .NET runtime (no SDK/runtime needed at run
# time). The `orkeon` CLI ships in two flavours from the *same* csproj: `orkeon`
# (self-contained, onboarding channel) and `orkeon-slim` (framework-dependent, for
# devs with .NET 10). Both share the one bundled esbuild (fetched once per RID).
$Apps = @(
    @{ Name = 'orkeon';              Csproj = 'src/scripting/Orkeon.Scripting.Cli/Orkeon.Scripting.Cli.csproj';                                    Apphost = 'orkeon';                                       SelfContained = $true }
    @{ Name = 'orkeon-slim';         Csproj = 'src/scripting/Orkeon.Scripting.Cli/Orkeon.Scripting.Cli.csproj';                                    Apphost = 'orkeon';                                       SelfContained = $false }
    @{ Name = 'orkeon-repl';         Csproj = 'src/apps/Orkeon.ConsoleApp/Orkeon.ConsoleApp.csproj';                                               Apphost = 'Orkeon.ConsoleApp';                            SelfContained = $false }
    @{ Name = 'orkeon-trading';      Csproj = 'examples/runners/trading/Orkeon.Examples.Trading.Runner.csproj';                                    Apphost = 'Orkeon.Examples.Trading.Runner';               SelfContained = $true }
    # Rids (optional) is a RID filter: absent/empty = publish for every RID; the
    # WPF orkeon-studio can only target Windows RIDs, hence the filter.
    @{ Name = 'orkeon-studio';        Csproj = 'src/apps/Orkeon.Studio.Wpf/Orkeon.Studio.Wpf.csproj';       Apphost = 'Orkeon.Studio';        SelfContained = $true; Rids = @('win-x64') }
    @{ Name = 'orkeon-studio-config'; Csproj = 'src/apps/Orkeon.Studio.Config/Orkeon.Studio.Config.csproj'; Apphost = 'Orkeon.Studio.Config'; SelfContained = $true }
    @{ Name = 'orkeon-studio-run';    Csproj = 'src/apps/Orkeon.Studio.Run/Orkeon.Studio.Run.csproj';       Apphost = 'Orkeon.Studio.Run';    SelfContained = $true }
    # The service host (GATE-05). Self-contained: a daemon supervised by systemd or the
    # Windows SCM must not depend on a runtime someone may upgrade underneath it.
    @{ Name = 'orkeon-host';          Csproj = 'src/hosting/Orkeon.Host/Orkeon.Host.csproj';                Apphost = 'orkeon-host';          SelfContained = $true }
)

# -AppSet cli ships the onboarding binary plus the Orkeon Studio apps for the
# platform: win-* adds the WPF orkeon-studio, linux-* adds the two TUIs,
# osx-* stays CLI-only (V1). Mirrors cli_set_includes in package-installers.sh.
function Test-CliSetIncludes([string]$AppName, [string]$Rid) {
    switch ($AppName) {
        'orkeon'              { return $true }
        'orkeon-studio'       { return $Rid -like 'win-*' }
        'orkeon-studio-config' { return $Rid -like 'linux-*' }
        'orkeon-studio-run'   { return $Rid -like 'linux-*' }
        default               { return $false }
    }
}

if ($AppSet -eq 'cli') {
    $orkeonEntries = @($Apps | Where-Object { $_.Name -eq 'orkeon' })
    if ($orkeonEntries.Count -ne 1) { throw "Expected exactly one 'orkeon' entry in the app table, found $($orkeonEntries.Count)." }
    $PkgPrefix = 'orkeon-cli'
} else {
    $PkgPrefix = 'orkeon'
}

$EsbuildNpmRid = @{
    'linux-x64' = 'linux-x64'; 'linux-arm64' = 'linux-arm64'
    'win-x64' = 'win32-x64'; 'osx-x64' = 'darwin-x64'; 'osx-arm64' = 'darwin-arm64'
}

$Stage = Join-Path $Out '_stage'
$Cache = Join-Path $Out '_esbuild-cache'
New-Item -ItemType Directory -Force -Path $Out, $Stage, $Cache | Out-Null

Write-Host "==> Packaging Orkeon $Version ($AppSet set, esbuild $EsbuildVersion) for: $($Rids -join ' ')"

foreach ($rid in $Rids) {
    $pkgName = "$PkgPrefix-$Version-$rid"
    $root = Join-Path $Stage $pkgName
    if (Test-Path $root) { Remove-Item -Recurse -Force $root }
    New-Item -ItemType Directory -Force -Path (Join-Path $root 'bin'), (Join-Path $root 'libexec') | Out-Null
    Write-Host "==> $rid"

    foreach ($app in $Apps) {
        if ($app.Rids -and $app.Rids -notcontains $rid) {
            Write-Host "    skip $($app.Name) (RID filter: $($app.Rids -join ' '))"
            continue
        }
        if ($AppSet -eq 'cli' -and -not (Test-CliSetIncludes $app.Name $rid)) { continue }
        $selfContained = if ($app.SelfContained) { 'true' } else { 'false' }
        Write-Host "    publish $($app.Name) (self-contained=$selfContained)"
        dotnet publish (Join-Path $RepoRoot $app.Csproj) -c $Configuration -r $rid --self-contained $selfContained `
            -p:PublishTrimmed=false `
            -p:Version=$Version -p:SkipScriptingNpmInstall=true `
            -p:ErrorOnDuplicatePublishOutputFiles=false `
            -o (Join-Path $root "libexec\$($app.Name)") --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($app.Name) ($rid)" }

        if ($rid -like 'win-*') {
            (Get-Content (Join-Path $Assets 'wrapper.cmd.tmpl') -Raw).
                Replace('{{APP}}', $app.Name).Replace('{{APPHOST}}', $app.Apphost) |
                Set-Content -NoNewline (Join-Path $root "bin\$($app.Name).cmd")
        } else {
            $sh = (Get-Content (Join-Path $Assets 'wrapper.sh.tmpl') -Raw).
                Replace('{{APP}}', $app.Name).Replace('{{APPHOST}}', $app.Apphost)
            [IO.File]::WriteAllText((Join-Path $root "bin/$($app.Name)"), $sh.Replace("`r`n", "`n"))
        }
    }

    # esbuild per RID, straight from the npm registry
    $npmRid = $EsbuildNpmRid[$rid]
    if (-not $npmRid) { throw "No esbuild mapping for RID $rid" }
    $pkgDir = Join-Path $Cache "$npmRid-$EsbuildVersion"
    if (-not (Test-Path $pkgDir)) {
        $tgz = Join-Path $Cache "$npmRid-$EsbuildVersion.tgz"
        Write-Host "    fetching @esbuild/$npmRid@$EsbuildVersion"
        Invoke-WebRequest "https://registry.npmjs.org/@esbuild/$npmRid/-/$npmRid-$EsbuildVersion.tgz" -OutFile $tgz
        # Verify the tarball against the sha512 pinned in package-lock.json before
        # trusting anything inside it (mirrors fetch_esbuild in package-installers.sh).
        $lockEntry = if ($lockJson) { $lockJson['packages']["node_modules/@esbuild/$npmRid"] } else { $null }
        $expected = if ($lockEntry) { $lockEntry['integrity'] } else { $null }
        if (-not $expected -or -not $expected.StartsWith('sha512-')) {
            Remove-Item -Force $tgz
            throw "No sha512 integrity for @esbuild/$npmRid in tools/scripting-esbuild/package-lock.json - refusing to package an unverifiable esbuild."
        }
        $hashHex = (Get-FileHash -Algorithm SHA512 $tgz).Hash
        $hashBytes = [byte[]]::new($hashHex.Length / 2)
        for ($i = 0; $i -lt $hashBytes.Length; $i++) { $hashBytes[$i] = [Convert]::ToByte($hashHex.Substring($i * 2, 2), 16) }
        $actual = 'sha512-' + [Convert]::ToBase64String($hashBytes)
        if ($actual -ne $expected) {
            Remove-Item -Force $tgz
            throw "Integrity mismatch for @esbuild/$npmRid@${EsbuildVersion}: expected $expected, got $actual"
        }
        New-Item -ItemType Directory -Force -Path $pkgDir | Out-Null
        tar -xzf $tgz -C $pkgDir
        if ($LASTEXITCODE -ne 0) { throw "tar extraction failed for $tgz" }
    }
    $esbuildDest = Join-Path $root 'libexec\esbuild-bin'
    New-Item -ItemType Directory -Force -Path $esbuildDest | Out-Null
    if ($rid -like 'win-*') {
        Copy-Item (Join-Path $pkgDir 'package\esbuild.exe') (Join-Path $esbuildDest 'esbuild.exe')
    } else {
        Copy-Item (Join-Path $pkgDir 'package\bin\esbuild') (Join-Path $esbuildDest 'esbuild')
    }

    # Docs + installer
    (Get-Content (Join-Path $Assets 'README.archive.md.tmpl') -Raw).
        Replace('{{VERSION}}', $Version).Replace('{{RID}}', $rid) |
        Set-Content (Join-Path $root 'README.md')
    Copy-Item (Join-Path $RepoRoot 'LICENSE.md') (Join-Path $root 'LICENSE.md')
    # Plain-text version marker: install.ps1 reads it for the Add/Remove Programs
    # entry, and it lets a user identify an already-extracted tree. LF-terminated
    # to stay byte-identical with the archive package-installers.sh produces.
    [IO.File]::WriteAllText((Join-Path $root 'VERSION'), "$Version`n")
    # Reference config only. The live one lives in %APPDATA%\Orkeon; this copy is
    # here to be read, not loaded.
    Copy-Item (Join-Path $RepoRoot 'examples/appsettings/appsettings.json') (Join-Path $root 'appsettings.sample.json')
    # Deployment assets (GATE-05/WINSVC-01): the systemd unit and the SCM
    # registration script ship with the daemon they install. Full set only (the
    # cli set has no orkeon-host; the MSI harvests the cli tree).
    if ($AppSet -eq 'full') {
        Copy-Item -Recurse (Join-Path $RepoRoot 'deploy') (Join-Path $root 'deploy')
    }
    if ($rid -like 'win-*') {
        Copy-Item (Join-Path $Assets 'install.ps1') (Join-Path $root 'install.ps1')
    } else {
        Copy-Item (Join-Path $Assets 'install.sh') (Join-Path $root 'install.sh')
    }

    # Archive
    if ($rid -like 'win-*') {
        $zip = Join-Path $Out "$pkgName.zip"
        if (Test-Path $zip) { Remove-Item $zip }
        Compress-Archive -Path $root -DestinationPath $zip
        Write-Host "    -> $zip"
    } else {
        $tarball = Join-Path $Out "$pkgName.tar.gz"
        tar -czf $tarball -C $Stage $pkgName
        if ($LASTEXITCODE -ne 0) { throw "tar failed for $pkgName" }
        Write-Host "    -> $tarball  (WARNING: exec bits not preserved from Windows)"
    }
}

# Checksums
$artifacts = Get-ChildItem $Out -File | Where-Object { $_.Extension -in '.zip', '.gz', '.deb' }
$lines = foreach ($f in $artifacts) { "{0}  {1}" -f (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLower(), $f.Name }
Set-Content -Path (Join-Path $Out 'SHA256SUMS') -Value $lines
Write-Host "==> Done. Artifacts in $Out"
