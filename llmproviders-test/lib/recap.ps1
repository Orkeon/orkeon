<#
.SYNOPSIS
  Rebuilds llmproviders-test/README.md from the campaign reports present on disk.
  PowerShell mirror of lib/recap.sh.
.DESCRIPTION
  Rebuild, not append. An index that grows by appending drifts the moment a report is
  deleted, re-run or renamed; one derived from what is actually on disk cannot. Running it
  twice in a row is therefore a no-op, which is exactly what makes it safe to call after
  every single campaign.
.PARAMETER Root
  Kit directory holding the per-provider report folders. Defaults to the kit itself.
#>
[CmdletBinding()]
param(
    [string]$Root = ''
)
$ErrorActionPreference = 'Stop'

$LibDir = $PSScriptRoot
if (-not $Root) { $Root = Split-Path -Parent $LibDir }
$Root = (Resolve-Path -LiteralPath $Root).Path
$JsonMarker = '<!-- orkeon-campaign-json -->'

# Pulls the embedded campaign JSON back out of a rendered report.
function Get-EmbeddedCampaign {
    param([string]$Path)

    $seen = $false; $inside = $false
    $buffer = [System.Collections.Generic.List[string]]::new()

    foreach ($line in [System.IO.File]::ReadLines($Path)) {
        if (-not $seen) { if ($line -eq $JsonMarker) { $seen = $true }; continue }
        if (-not $inside) { if ($line -eq '```json') { $inside = $true }; continue }
        if ($line -eq '```') { break }
        $buffer.Add($line)
    }

    if ($buffer.Count -eq 0) { return $null }
    return ($buffer -join "`n") | ConvertFrom-Json
}

$campaigns = [System.Collections.Generic.List[object]]::new()

# Sorted so the index is byte-stable across runs and across filesystems.
# `lib/` sits at the same depth as the provider directories, so it has to be excluded by name.
# Nothing in it has an embedded campaign JSON today, which is the only reason a stray Markdown
# file there has never been mistaken for a report — that is luck, not a rule.
$reports = Get-ChildItem -LiteralPath $Root -Directory |
    Where-Object { $_.Name -ne 'lib' } |
    ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Filter '*.md' -File } |
    Sort-Object FullName

foreach ($report in $reports) {
    $campaign = Get-EmbeddedCampaign -Path $report.FullName
    if (-not $campaign) { continue }

    $relative = $report.FullName.Substring($Root.Length).TrimStart('\', '/').Replace('\', '/')

    # ConvertFrom-Json turns an ISO-8601 string into a [DateTime], which would render in the
    # host's own format — and the index is versioned, so it must read the same everywhere.
    $stamp = if ($campaign.timestampUtc -is [DateTime]) {
        $campaign.timestampUtc.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    } else { [string]$campaign.timestampUtc }

    $campaigns.Add([pscustomobject]@{
        Provider = $campaign.provider
        Model    = $campaign.model
        Stamp    = $stamp
        Version  = $campaign.orkeonVersion
        Passed   = $campaign.passed
        Failed   = $campaign.failed
        Skipped  = $campaign.notApplicable
        Status   = if ($campaign.failed -gt 0) { '❌' } else { '✅' }
        Modes    = ($campaign.modes | ForEach-Object { $_.mode }) -join ' '
        Relative = $relative
    })
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add((Get-Content -Raw -LiteralPath (Join-Path $LibDir 'readme-header.md')).TrimEnd())
$lines.Add('')
$lines.Add('## Campaigns run')
$lines.Add('')
# Generated, not hand-written: the index is rebuilt from scratch on every run, so a note
# that only lived in the committed README would vanish at the next campaign.
$lines.Add('> The per-campaign reports dated before 2026-09-06 are written in **French**: they predate the')
$lines.Add('> switch of the report generator to English (2026-09-06), and they are timestamped evidence, so')
$lines.Add('> they are kept exactly as they were produced rather than re-rendered. Reports from 2026-09-06')
$lines.Add('> onwards are in English.')
$lines.Add('')

if ($campaigns.Count -eq 0) {
    $lines.Add('_No campaign archived yet._ Launch one — start with Ollama, whose cost is zero:')
    $lines.Add('')
    $lines.Add('```bash')
    $lines.Add('llmproviders-test/run-campaign.sh --provider ollama --model llama3.2')
    $lines.Add('```')
}
else {
    $lines.Add('| Timestamp (UTC) | Provider | Model | ✅/❌/➖ | Version | Report |')
    $lines.Add('|---|---|---|---|---|---|')

    # Newest first — then provider, then model, so a tie is broken the same way everywhere.
    # Two campaigns launched in parallel routinely land on the same second, and sorting on the
    # timestamp alone would leave the order to whatever the enumeration happened to produce.
    $ordered = $campaigns | Sort-Object `
        @{ Expression = 'Stamp'; Descending = $true },
        @{ Expression = 'Provider'; Descending = $false },
        @{ Expression = 'Model'; Descending = $false }

    foreach ($c in $ordered) {
        $lines.Add("| $($c.Stamp) | ``$($c.Provider)`` | ``$($c.Model)`` | $($c.Status) $($c.Passed)/$($c.Failed)/$($c.Skipped) | $($c.Version) | [report]($($c.Relative)) |")
    }

    $lines.Add('')
    $lines.Add('## Latest campaign per provider')
    $lines.Add('')
    $lines.Add('| Provider | Model | Date | Status | Modes exercised |')
    $lines.Add('|---|---|---|---|---|')

    foreach ($group in ($campaigns | Group-Object Provider | Sort-Object Name)) {
        $latest = $group.Group | Sort-Object Stamp | Select-Object -Last 1
        $date = $latest.Stamp.Split('T')[0]
        $lines.Add("| ``$($latest.Provider)`` | ``$($latest.Model)`` | $date | $($latest.Status) | $($latest.Modes) |")
    }
}

$lines.Add('')
$lines.Add('---')
$lines.Add('')
# Names both mirrors, never the one that happened to run: the index is versioned, and a
# footer that changed with the operator's platform would churn the file for nothing.
$lines.Add('_Index regenerated by `lib/recap.sh` or `lib/recap.ps1` from the reports present on disk._')
$lines.Add("_$($campaigns.Count) archived campaign(s)._")

$readme = Join-Path $Root 'README.md'

# Written aside then moved into place. Two campaigns running in parallel both rebuild the
# index when they finish; writing straight to README.md truncates before it writes, so the
# loser of that race can be read half-empty. A move on the same filesystem cannot be observed
# partially — the reader sees the old file or the new one, never a torn one.
$staging = Join-Path $Root ".README.md.$PID"
[System.IO.File]::WriteAllText(
    $staging, ($lines -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
Move-Item -LiteralPath $staging -Destination $readme -Force

Write-Output $readme
