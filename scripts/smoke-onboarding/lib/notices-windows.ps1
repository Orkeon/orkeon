<#
.SYNOPSIS
  Notices assertions for the Windows channels (GAP-52).

.DESCRIPTION
  Dot-sourced, never executed. It is the PowerShell twin of lib\smoke-common.sh's
  smoke_assert_notices, and the single place the four Windows channels share the
  check: the CLI archive and the tree install.ps1 makes of it (run-smoke.ps1), the
  CLI MSI (the `msi` job in .github\workflows\release.yml), the full archive
  (run-smoke-service.ps1) and the service MSI (run-smoke-service-msi.ps1). The
  four lay out the same tree, so the assertions must not be written four times
  and allowed to drift -- same doctrine as lib\studio-windows.ps1.

  What every payload must carry (GAP-45), under its root:
    <root>\LICENSE.md
    <root>\THIRD-PARTY-NOTICES.md
    <root>\licenses\<pack>\      one per .NET runtime an application bundles, not empty

  Nothing is listed here. Which runtimes a payload bundles is read from the
  payload: each <root>\libexec\<app>\*.runtimeconfig.json names the frameworks
  its application carries (`includedFrameworks`, absent from a framework-dependent
  one), and the pack is `<framework>.Runtime.<rid>`, the RID being the one the
  *.deps.json beside it was published for. So the WPF Studio, which bundles
  Microsoft.WindowsDesktop.App beside Microsoft.NETCore.App, is expected to bring
  both folders without a name written in a smoke. A root whose libexec\ holds no
  *.runtimeconfig.json at all is a problem, never an empty pass: the caller
  pointed at the wrong place.

  The caller decides what to do with the result; nothing here writes to the
  console or exits. Compatible with Windows PowerShell 5.1 and PowerShell 7, and
  with PowerShell 7 under Linux, where scripts\test-smoke-notices.ps1 proves it
  in CI: every path goes through Join-Path, every name is spelled in its case.
#>

# Get-OrkeonJsonMember -- a member of a ConvertFrom-Json object, $null when the
# object or the member is absent (never an error, whatever the strict mode).
function Get-OrkeonJsonMember {
    param($Object, [string]$Name)

    if ($null -eq $Object) { return $null }
    $member = $Object.PSObject.Properties[$Name]
    if ($null -eq $member) { return $null }
    return $member.Value
}

# Invoke-OrkeonNoticesAssertions -- asserts the notices of a payload.
#
#   -Root  the directory holding libexec\ and the notices (an extracted archive's
#          top-level folder, or an install directory)
#
# Returns [pscustomobject]@{ Problems = @(...); Notes = @(...) } -- an empty
# Problems array is the pass, and Notes then names every file and folder seen.
function Invoke-OrkeonNoticesAssertions {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Root
    )

    $problems = New-Object System.Collections.ArrayList
    $seen = New-Object System.Collections.ArrayList

    foreach ($name in @('LICENSE.md', 'THIRD-PARTY-NOTICES.md')) {
        $path = Join-Path $Root $name
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            [void]$seen.Add($name)
        } else {
            [void]$problems.Add("$path is missing")
        }
    }

    $libexec = Join-Path $Root 'libexec'
    $configs = New-Object System.Collections.ArrayList
    if (Test-Path -LiteralPath $libexec -PathType Container) {
        foreach ($app in @(Get-ChildItem -LiteralPath $libexec -Directory | Sort-Object Name)) {
            foreach ($config in @(Get-ChildItem -LiteralPath $app.FullName -File -Filter '*.runtimeconfig.json' | Sort-Object Name)) {
                [void]$configs.Add($config)
            }
        }
    }
    if ($configs.Count -eq 0) {
        [void]$problems.Add("no *.runtimeconfig.json to read the bundled runtimes from (the application folders of $libexec)")
    }

    # The packs expected, in the order met, and the applications that bundle each.
    $packs = New-Object System.Collections.ArrayList
    $bundlers = @{}
    $frameworkDependent = 0
    foreach ($config in $configs) {
        $appDir = $config.Directory
        try {
            $json = Get-Content -LiteralPath $config.FullName -Raw | ConvertFrom-Json
        } catch {
            [void]$problems.Add("$($config.FullName): unreadable ($($_.Exception.Message))")
            continue
        }
        $options = Get-OrkeonJsonMember $json 'runtimeOptions'
        $frameworks = @(@(Get-OrkeonJsonMember $options 'includedFrameworks') |
            ForEach-Object { Get-OrkeonJsonMember $_ 'name' } |
            Where-Object { $_ })
        if ($frameworks.Count -eq 0) {
            $frameworkDependent++
            continue
        }

        # runtimeTarget.name is "<framework>/<rid>". Read with a pattern, not with
        # ConvertFrom-Json: a deps.json names every file of the closure, and
        # ConvertFrom-Json refuses a document in which two keys differ by case
        # alone -- one such pair of assets would fail the smoke for no notice lost.
        $rid = ''
        foreach ($deps in @(Get-ChildItem -LiteralPath $appDir.FullName -File -Filter '*.deps.json' | Sort-Object Name)) {
            $text = [string](Get-Content -LiteralPath $deps.FullName -Raw)
            $match = [regex]::Match($text, '"runtimeTarget"\s*:\s*\{\s*"name"\s*:\s*"[^"/]*/([^"]+)"')
            if ($match.Success) {
                $rid = $match.Groups[1].Value
                break
            }
        }
        if (-not $rid) {
            [void]$problems.Add("$($appDir.Name) bundles $($frameworks -join ', ') but no *.deps.json beside $($config.Name) names the runtime identifier it was published for")
            continue
        }

        foreach ($framework in $frameworks) {
            $pack = "$framework.Runtime.$rid"
            if (-not $bundlers.ContainsKey($pack)) {
                [void]$packs.Add($pack)
                $bundlers[$pack] = New-Object System.Collections.ArrayList
            }
            if (-not $bundlers[$pack].Contains($appDir.Name)) { [void]$bundlers[$pack].Add($appDir.Name) }
        }
    }

    foreach ($pack in $packs) {
        $folder = Join-Path (Join-Path $Root 'licenses') $pack
        $apps = $bundlers[$pack] -join ', '
        if (-not (Test-Path -LiteralPath $folder -PathType Container)) {
            [void]$problems.Add("licenses/$pack/ is missing from $Root (the runtime $apps bundles)")
            continue
        }
        $files = @(Get-ChildItem -LiteralPath $folder -File | Sort-Object Name | ForEach-Object { $_.Name })
        if ($files.Count -eq 0) {
            [void]$problems.Add("licenses/$pack/ is empty in $Root (the runtime $apps bundles)")
        } else {
            [void]$seen.Add("licenses/$pack/ ($($files -join ', '))")
        }
    }

    if ($problems.Count -gt 0) {
        return [pscustomobject]@{ Problems = @($problems); Notes = @() }
    }

    $note = "$($seen -join ', ') in $Root"
    if ($packs.Count -eq 0) {
        $note = "$note; no bundled runtime ($frameworkDependent framework-dependent application(s))"
    }
    return [pscustomobject]@{ Problems = @(); Notes = @($note) }
}
