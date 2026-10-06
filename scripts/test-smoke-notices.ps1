<#
.SYNOPSIS
  Regression test of Invoke-OrkeonNoticesAssertions (scripts\smoke-onboarding\lib\notices-windows.ps1).

.DESCRIPTION
  The PowerShell twin of scripts/test-smoke-notices.sh: the same rules, proved on the
  same throwaway trees, for the check the four Windows release smokes run on the
  notices of the payload they installed. No publish, no archive, no network.

  The Debian layout of the shell test has no twin here: no Windows channel installs
  one, and the function reads one layout only, <root>\libexec\<app>\.

  CI runs it with PowerShell 7 on a Linux runner (ci.yml), which is why every path
  goes through Join-Path. Run: pwsh scripts/test-smoke-notices.ps1
#>
$ErrorActionPreference = 'Stop'

. (Join-Path (Join-Path (Join-Path $PSScriptRoot 'smoke-onboarding') 'lib') 'notices-windows.ps1')

$netcore = 'Microsoft.NETCore.App'
$desktop = 'Microsoft.WindowsDesktop.App'

$work = Join-Path ([IO.Path]::GetTempPath()) ('orkeon-test-smoke-notices-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null

$script:failed = 0
$script:result = $null
$script:text = ''

# New-App -- what `dotnet publish` leaves beside an apphost: with no framework named,
# a framework-dependent application.
function New-App([string]$Root, [string]$Folder, [string]$Name, [string]$Rid, [string[]]$Frameworks = @()) {
    $dir = Join-Path (Join-Path $Root 'libexec') $Folder
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    if ($Frameworks.Count -gt 0) {
        $included = ($Frameworks | ForEach-Object { '{ "name": "' + $_ + '", "version": "10.0.9" }' }) -join ', '
        $config = '{ "runtimeOptions": { "tfm": "net10.0", "includedFrameworks": [ ' + $included + ' ] } }'
    } else {
        $config = '{ "runtimeOptions": { "tfm": "net10.0", "framework": { "name": "' + $netcore + '", "version": "10.0.0" } } }'
    }
    Set-Content -LiteralPath (Join-Path $dir "$Name.runtimeconfig.json") -Value $config
    $deps = '{ "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0/' + $Rid + '", "signature": "" }, "libraries": {} }'
    Set-Content -LiteralPath (Join-Path $dir "$Name.deps.json") -Value $deps
}

# New-Notices -- the license, the third-party notices and one license file per runtime
# pack named.
function New-Notices([string]$Root, [string[]]$Packs = @()) {
    New-Item -ItemType Directory -Force -Path $Root | Out-Null
    Set-Content -LiteralPath (Join-Path $Root 'LICENSE.md') -Value 'license'
    Set-Content -LiteralPath (Join-Path $Root 'THIRD-PARTY-NOTICES.md') -Value 'notices'
    foreach ($pack in $Packs) {
        $dir = Join-Path (Join-Path $Root 'licenses') $pack
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        Set-Content -LiteralPath (Join-Path $dir 'LICENSE.TXT') -Value 'runtime license'
    }
}

function Assert-Notices([string]$Root) {
    $script:result = Invoke-OrkeonNoticesAssertions -Root $Root
    $script:text = (@($script:result.Problems) + @($script:result.Notes)) -join "`n"
}

function Check([string]$Description, [bool]$Ok) {
    if ($Ok) {
        Write-Host "ok    $Description"
    } else {
        Write-Host "FAIL  $Description"
        $script:failed++
    }
}

function Test-Passes { return (@($script:result.Problems).Count -eq 0 -and @($script:result.Notes).Count -gt 0) }
function Test-Refuses { return (@($script:result.Problems).Count -gt 0) }
function Test-Says([string]$Fragment) { return $script:text.Contains($Fragment) }
function Get-Occurrences([string]$Fragment) { return ([regex]::Matches($script:text, [regex]::Escape($Fragment))).Count }

try {
    Write-Host '# an archive: one self-contained application, one framework-dependent, esbuild'
    $tree = Join-Path $work 'archive'
    New-Notices $tree @("$netcore.Runtime.linux-x64")
    New-App $tree 'orkeon' 'orkeon' 'linux-x64' @($netcore)
    New-App $tree 'orkeon-slim' 'orkeon' 'linux-x64'
    $esbuild = Join-Path (Join-Path $tree 'libexec') 'esbuild-bin'
    New-Item -ItemType Directory -Force -Path $esbuild | Out-Null
    Set-Content -LiteralPath (Join-Path $esbuild 'esbuild') -Value ''
    Assert-Notices $tree
    Check 'passes' (Test-Passes)
    Check "names the runtime's folder" (Test-Says "licenses/$netcore.Runtime.linux-x64/")
    Check 'names the file it holds' (Test-Says 'LICENSE.TXT')
    Check 'names the license and the notices' (Test-Says 'LICENSE.md, THIRD-PARTY-NOTICES.md')

    Remove-Item -LiteralPath (Join-Path $tree 'licenses') -Recurse -Force
    Assert-Notices $tree
    Check 'without licenses/: fails' (Test-Refuses)
    Check '... and names the missing folder' (Test-Says "licenses/$netcore.Runtime.linux-x64/ is missing")
    Check '... and the application that bundles the runtime' (Test-Says 'the runtime orkeon bundles')

    Write-Host '# a WPF application bundles two runtimes'
    $tree = Join-Path $work 'wpf'
    New-Notices $tree @("$netcore.Runtime.win-x64")
    New-App $tree 'orkeon' 'orkeon' 'win-x64' @($netcore)
    New-App $tree 'orkeon-studio' 'Orkeon.Studio' 'win-x64' @($netcore, $desktop)
    Assert-Notices $tree
    Check 'the .NET folder alone: fails' (Test-Refuses)
    Check '... and names the Windows Desktop pack' (Test-Says "licenses/$desktop.Runtime.win-x64/ is missing")
    Check '... and not the folder that is there' (-not (Test-Says "licenses/$netcore.Runtime.win-x64/ is"))

    New-Notices $tree @("$netcore.Runtime.win-x64", "$desktop.Runtime.win-x64")
    Assert-Notices $tree
    Check 'both folders: passes' (Test-Passes)
    Check '... and names both' (Test-Says "licenses/$desktop.Runtime.win-x64/")
    Check '... each one once' ((Get-Occurrences "licenses/$netcore.Runtime.win-x64/") -eq 1)

    Write-Host '# what a packaging script can lose'
    $tree = Join-Path $work 'losses'
    $pack = "$netcore.Runtime.linux-x64"
    New-Notices $tree @($pack)
    New-App $tree 'orkeon' 'orkeon' 'linux-x64' @($netcore)
    Remove-Item -LiteralPath (Join-Path (Join-Path (Join-Path $tree 'licenses') $pack) 'LICENSE.TXT') -Force
    Assert-Notices $tree
    Check 'an empty runtime folder: fails' (Test-Refuses)
    Check '... and says it is empty' (Test-Says "licenses/$pack/ is empty")

    New-Notices $tree @($pack)
    Remove-Item -LiteralPath (Join-Path $tree 'THIRD-PARTY-NOTICES.md') -Force
    Assert-Notices $tree
    Check 'no THIRD-PARTY-NOTICES.md: fails' (Test-Refuses)
    Check '... and names it' (Test-Says 'THIRD-PARTY-NOTICES.md is missing')

    New-Notices $tree @($pack)
    Remove-Item -LiteralPath (Join-Path $tree 'LICENSE.md') -Force
    Assert-Notices $tree
    Check 'no license: fails' (Test-Refuses)
    Check '... and names it' (Test-Says 'LICENSE.md is missing')

    New-Notices $tree @($pack)
    Remove-Item -LiteralPath (Join-Path (Join-Path (Join-Path $tree 'libexec') 'orkeon') 'orkeon.deps.json') -Force
    Assert-Notices $tree
    Check 'a bundled runtime and no runtime identifier: fails' (Test-Refuses)
    Check '... and says what it could not read' (Test-Says 'runtime identifier')

    Write-Host '# nothing to read is never a pass'
    $tree = Join-Path $work 'empty'
    New-Notices $tree
    New-Item -ItemType Directory -Force -Path (Join-Path (Join-Path $tree 'libexec') 'esbuild-bin') | Out-Null
    Assert-Notices $tree
    Check 'no *.runtimeconfig.json under libexec: fails' (Test-Refuses)
    Check '... and says so' (Test-Says 'no *.runtimeconfig.json')
    $tree = Join-Path $work 'bare'
    New-Notices $tree
    Assert-Notices $tree
    Check 'no libexec at all: fails' (Test-Refuses)

    Write-Host '# framework-dependent applications only'
    $tree = Join-Path $work 'slim'
    New-Notices $tree
    New-App $tree 'orkeon-slim' 'orkeon' 'linux-x64'
    New-App $tree 'orkeon-repl' 'Orkeon.ConsoleApp' 'linux-x64'
    Assert-Notices $tree
    Check 'no licenses/ and no bundled runtime: passes' (Test-Passes)
    Check '... and says no runtime is bundled' (Test-Says 'no bundled runtime')
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($script:failed -gt 0) {
    Write-Host "test-smoke-notices: $($script:failed) check(s) failed"
    exit 1
}
Write-Host 'test-smoke-notices passed'
