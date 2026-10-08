<#
.SYNOPSIS
  The version of a build that is not a release. PowerShell twin of
  scripts/resolve-version.sh: same inputs, same answer, same exit codes
  (scripts/test-resolve-version.sh compares the two on every case).
.DESCRIPTION
  With -Run, prints the dev version of that CI run: <prefix>-<suffix>.dev.<n>.
  Without it, prints the version of a build made from the checkout that holds -Props:
    HEAD is the commit a v* tag points to   the tag's version, v stripped;
    any other commit                        <prefix>-<suffix>.local.<stamp>;
    no git checkout (a source archive)      the props version, as it is written.
  <stamp> is the committer date of HEAD, yyyyMMddHHmm in UTC. Changes that are not
  committed do not move it: two builds of one commit carry one version.

  A props version without a suffix moves its dev and local builds to the next patch
  (1.0.0 gives 1.0.1-dev.<n> and 1.0.1-local.<stamp>): <prefix>-dev.<n> would sort below
  the release.

  Written for Windows PowerShell 5.1 as well as PowerShell 7: the script that installs
  from the sources calls it before it has checked which PowerShell runs it.
.PARAMETER Props
  The Directory.Build.props that carries VersionPrefix and VersionSuffix.
  Default: src\Directory.Build.props of this checkout.
.PARAMETER Run
  The number of the CI run that validated the commit.
.PARAMETER Format
  semver (the default) or deb, the Debian Version of the same build (`-` becomes `~`).
.NOTES
  Exit: 0 and the version on the output; 1 when the props or a tag give no version; 2 on
  a usage error.
#>
[CmdletBinding()]
param(
    [string]$Props = '',
    [string]$Run = '',
    [string]$Format = 'semver'
)
# Not 'Stop': under Windows PowerShell 5.1 a native command that writes to a redirected
# stderr would then end the script, and git says on stderr that a commit carries no tag.
$ErrorActionPreference = 'Continue'

function Stop-Script([int]$Code, [string]$Message) {
    [Console]::Error.WriteLine("resolve-version: $Message")
    exit $Code
}
function Write-Version([string]$Version) {
    if ($Format -ceq 'deb') {
        $dash = $Version.IndexOf('-')
        if ($dash -ge 0) { $Version = $Version.Substring(0, $dash) + '~' + $Version.Substring($dash + 1) }
    }
    Write-Output $Version
    exit 0
}

$hasRun = $PSBoundParameters.ContainsKey('Run')
if (-not $Props) { $Props = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Directory.Build.props' }
if (-not (Test-Path -LiteralPath $Props -PathType Leaf)) { Stop-Script 2 '-Props names no file' }
if ($Format -cne 'semver' -and $Format -cne 'deb') { Stop-Script 2 "-Format takes semver or deb, got '$Format'" }
if ($hasRun -and $Run -notmatch '^[1-9][0-9]*$') { Stop-Script 2 "-Run takes the CI run number, got '$Run'" }

# The first line that carries each element, as the shell script reads them.
$prefix = ''
$suffix = ''
$seenPrefix = $false
$seenSuffix = $false
foreach ($line in [IO.File]::ReadAllLines((Resolve-Path -LiteralPath $Props).ProviderPath)) {
    if (-not $seenPrefix -and $line -match '<VersionPrefix>(.*)</VersionPrefix>') { $prefix = $Matches[1]; $seenPrefix = $true }
    if (-not $seenSuffix -and $line -match '<VersionSuffix>(.*)</VersionSuffix>') { $suffix = $Matches[1]; $seenSuffix = $true }
}
if ($prefix -notmatch '^([0-9]+)\.([0-9]+)\.([0-9]+)$') {
    Stop-Script 1 "${Props}: VersionPrefix '$prefix' is not Major.Minor.Patch"
}
$nextPatch = '{0}.{1}.{2}' -f $Matches[1], $Matches[2], ([long]$Matches[3] + 1)
# One alphabet for both forms: a Debian version takes no `-` after the `~`, SemVer no `_`.
if ($suffix -and $suffix -notmatch '^[0-9A-Za-z.]+$') {
    Stop-Script 1 "${Props}: VersionSuffix '$suffix' holds a character neither a SemVer prerelease nor a Debian version carries"
}

if ($hasRun) {
    $label = "dev.$Run"
}
else {
    $tree = Split-Path -Parent (Resolve-Path -LiteralPath $Props).ProviderPath
    # A machine without git reads a source archive like any other tree that is no checkout.
    $inCheckout = $false
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $null = git -C $tree rev-parse --verify --quiet HEAD 2>$null
        $inCheckout = ($LASTEXITCODE -eq 0)
    }
    if (-not $inCheckout) {
        [Console]::Error.WriteLine("resolve-version: $tree is no git checkout: the version is the one $Props states")
        if ($suffix) { Write-Version "$prefix-$suffix" } else { Write-Version $prefix }
    }
    $tag = git -C $tree describe --tags --match 'v*' --exact-match HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and $tag) {
        $tag = "$tag".Trim()
        $version = $tag -replace '^v', ''
        if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$') {
            Stop-Script 1 "tag $tag on HEAD names no version: pass the version yourself"
        }
        Write-Version $version
    }
    # The committer date in seconds since the epoch, formatted here: no time zone of the
    # machine, no calendar of its culture, takes part.
    $seconds = git -C $tree log -1 --format=%ct
    $epoch = New-Object DateTime 1970, 1, 1, 0, 0, 0, ([DateTimeKind]::Utc)
    $stamp = $epoch.AddSeconds([long]"$seconds".Trim()).ToString('yyyyMMddHHmm', [Globalization.CultureInfo]::InvariantCulture)
    $label = "local.$stamp"
}

if ($suffix) { Write-Version "$prefix-$suffix.$label" } else { Write-Version "$nextPatch-$label" }
