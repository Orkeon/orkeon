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
$KitDir = (Resolve-Path -LiteralPath (Split-Path -Parent $LibDir)).Path
$JsonMarker = '<!-- orkeon-campaign-json -->'
$CatalogPath = Join-Path $LibDir 'catalog.json'
$DocsDir = Join-Path $KitDir '../docs'
# The documentation links a report by its published URL: the docs site does not carry the kit.
$ReportUrl = 'https://github.com/Orkeon/orkeon/blob/main/llmproviders-test'

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
        Commit   = if ([string]::IsNullOrWhiteSpace($campaign.commit)) { '—' } else { $campaign.commit }
        Passed   = $campaign.passed
        Failed   = $campaign.failed
        Skipped  = $campaign.notApplicable
        Status   = if ($campaign.failed -gt 0) { '❌' } else { '✅' }
        Modes    = ($campaign.modes | ForEach-Object { $_.mode }) -join ' '
        Results  = @($campaign.modes | ForEach-Object { [pscustomobject]@{ Mode = [string]$_.mode; Outcome = [string]$_.outcome } })
        Relative = $relative
    })
}

# The latest result of each model, one row per model. A row merges every report of the model's
# latest campaign day, the latest result of each mode winning: reports of the same model
# (for example a D8 run made separately) are merged, a vision companion is a row of its own, and
# a day older than the model's latest never mixes in. The report linked is that day's with the most modes. Sorted ordinally, the
# same order as the code-point sort of lib/recap.sh.
function Get-ModeKey([string]$Mode) { return [pscustomobject]@{ Letter = $Mode.Substring(0, 1); Number = [int]$Mode.Substring(1) } }

function Compare-Mode([string]$A, [string]$B) {
    $ka = Get-ModeKey $A; $kb = Get-ModeKey $B
    $c = [string]::CompareOrdinal($ka.Letter, $kb.Letter)
    if ($c -ne 0) { return $c }
    return $ka.Number.CompareTo($kb.Number)
}

function Get-ModelRows {
    $catalog = Get-Content -Raw -LiteralPath $CatalogPath | ConvertFrom-Json
    $defaults = @{}; $labels = @{}
    foreach ($entry in $catalog.providers.PSObject.Properties) {
        if ($null -ne $entry.Value.aliasOf) { continue }
        $defaults[$entry.Name] = [string]$entry.Value.defaultModel
        $labels[$entry.Name] = [string]$entry.Value.label
    }

    $groups = @{}
    foreach ($c in $campaigns) {
        $key = "$($c.Provider)`t$($c.Model)"
        if (-not $groups.ContainsKey($key)) { $groups[$key] = [System.Collections.Generic.List[object]]::new() }
        $groups[$key].Add($c)
    }

    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($group in $groups.Values) {
        $day = ($group | ForEach-Object { $_.Stamp.Substring(0, 10) } | Sort-Object | Select-Object -Last 1)
        $sameDay = @($group | Where-Object { $_.Stamp.Substring(0, 10) -eq $day } | Sort-Object Stamp)

        $results = [ordered]@{}
        foreach ($c in $sameDay) { foreach ($r in $c.Results) { $results[$r.Mode] = $r.Outcome } }

        $failedModes = [System.Collections.Generic.List[string]]::new()
        foreach ($m in $results.Keys) { if ($results[$m] -eq 'failed') { $failedModes.Add($m) } }
        $failedModes.Sort([Comparison[string]] { param($a, $b) Compare-Mode $a $b })

        $best = $sameDay | Sort-Object `
            @{ Expression = { $_.Results.Count } },
            @{ Expression = 'Stamp' },
            @{ Expression = { [string]$_.Relative } } |
            Select-Object -Last 1

        $provider = $group[0].Provider
        $label = if ($labels.ContainsKey($provider) -and $labels[$provider]) { $labels[$provider] } else { $provider }
        $rows.Add([pscustomobject]@{
            Provider = $provider
            Label    = $label
            Model    = $group[0].Model
            Default  = if ($defaults.ContainsKey($provider) -and $defaults[$provider] -ceq $group[0].Model) { 0 } else { 1 }
            Day      = $day
            Passed   = @($results.Values | Where-Object { $_ -eq 'passed' }).Count
            Failed   = @($results.Values | Where-Object { $_ -eq 'failed' }).Count
            Na       = @($results.Values | Where-Object { $_ -eq 'not-applicable' }).Count
            FailedModes = if ($failedModes.Count -gt 0) { $failedModes -join ', ' } else { '—' }
            Relative = $best.Relative
        })
    }

    # Provider, then the default first, then the newest day, then the model, all ordinal.
    $rows.Sort([Comparison[object]] {
        param($a, $b)
        $c = [string]::CompareOrdinal($a.Provider, $b.Provider); if ($c -ne 0) { return $c }
        $c = $a.Default.CompareTo($b.Default); if ($c -ne 0) { return $c }
        $c = [string]::CompareOrdinal($b.Day, $a.Day); if ($c -ne 0) { return $c }
        return [string]::CompareOrdinal($a.Model, $b.Model)
    })
    return , $rows
}

# Renders the per-model table into $Target. $Lang: en or fr; $Prefix: the prefix of a report link.
function Add-ModelTable($Target, [string]$Lang, [string]$Prefix) {
    if ($Lang -eq 'fr') { $Target.Add('| Fournisseur | Modèle | Date | ✅/❌/➖ | Modes en échec | Rapport |') }
    else { $Target.Add('| Provider | Model | Date | ✅/❌/➖ | Failed modes | Report |') }
    $Target.Add('|---|---|---|---|---|---|')
    $previous = ''
    foreach ($row in $modelRows) {
        $cell = if ($row.Provider -ceq $previous) { '' } else { $row.Label }
        $previous = $row.Provider
        $mark = ''
        if ($row.Default -eq 0) { $mark = if ($Lang -eq 'fr') { ' *(défaut)*' } else { ' *(default)*' } }
        $word = if ($Lang -eq 'fr') { 'rapport' } else { 'report' }
        $Target.Add("| $cell | ``$($row.Model)``$mark | $($row.Day) | $($row.Passed)/$($row.Failed)/$($row.Na) | $($row.FailedModes) | [$word]($Prefix$($row.Relative)) |")
    }
}

$modelRows = if ($campaigns.Count -gt 0) { Get-ModelRows } else { [System.Collections.Generic.List[object]]::new() }
$latestDay = ($modelRows | ForEach-Object { $_.Day } | Sort-Object | Select-Object -Last 1)

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
    $lines.Add('| Timestamp (UTC) | Provider | Model | ✅/❌/➖ | Commit | Report |')
    $lines.Add('|---|---|---|---|---|---|')

    # Newest first — then provider, then model, so a tie is broken the same way everywhere.
    # Two campaigns launched in parallel routinely land on the same second, and sorting on the
    # timestamp alone would leave the order to whatever the enumeration happened to produce.
    $ordered = $campaigns | Sort-Object `
        @{ Expression = 'Stamp'; Descending = $true },
        @{ Expression = 'Provider'; Descending = $false },
        @{ Expression = 'Model'; Descending = $false }

    foreach ($c in $ordered) {
        $lines.Add("| $($c.Stamp) | ``$($c.Provider)`` | ``$($c.Model)`` | $($c.Status) $($c.Passed)/$($c.Failed)/$($c.Skipped) | ``$($c.Commit)`` | [report]($($c.Relative)) |")
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

    $lines.Add('')
    $lines.Add('## Latest result per model')
    $lines.Add('')
    $lines.Add("One row per model: the latest result of each mode on the model's latest campaign day (the")
    $lines.Add("reports of the same model are merged; a vision companion is a row of its own). The same table is")
    $lines.Add('published in the documentation, `docs/reference/llm-campaign-results.md`.')
    $lines.Add('')
    Add-ModelTable $lines 'en' ''
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

# The documentation page, in English and its French mirror. Only for the kit's own reports: a
# campaign archived elsewhere (-Root) is not what the documentation publishes.
function Write-DocsPage([string]$Lang, [string]$Target) {
    $page = [System.Collections.Generic.List[string]]::new()
    if ($Lang -eq 'fr') {
        $page.Add('> 🇬🇧 [English version](../../reference/llm-campaign-results.md)')
        $page.Add('')
        $page.Add('# Résultats des campagnes LLM')
        $page.Add('')
        $page.Add('> Page générée par `llmproviders-test/lib/recap.sh` (ou `recap.ps1`) à la fin de chaque')
        $page.Add('> campagne : ne pas la modifier à la main.')
        $page.Add('')
        $page.Add("Chaque modèle que le kit de campagne a exercé contre l'API réelle de son fournisseur, avec le")
        $page.Add('dernier résultat de chaque mode. Une ligne réunit les rapports du dernier jour de campagne du')
        $page.Add('modèle : seuls les rapports d''un même modèle sont réunis ; un compagnon vision est une ligne à part.')
        $page.Add("Les jours plus anciens sont dans le [journal des campagnes]($ReportUrl/README.md#campaigns-run).")
        $page.Add('')
        $page.Add('Les modes M1 à M13 sont ceux du chat, D1 à D8 ceux des modèles de décision ; ils sont définis dans')
        $page.Add("le [README du kit]($ReportUrl/README.md#the-protocol-modes). ✅ réussi, ❌ en échec, ➖ sans")
        $page.Add('objet (le fournisseur ou le modèle ne déclare pas la capacité). Les modèles par défaut et les')
        $page.Add('raisons de leur choix : [comparatif des fournisseurs LLM](llm-providers-comparison.md).')
        $page.Add('')
        $page.Add("Dernière campagne : $latestDay · $($modelRows.Count) modèles · $($campaigns.Count) rapports archivés.")
    }
    else {
        $page.Add('> 🇫🇷 [Version française](../fr/reference/llm-campaign-results.md)')
        $page.Add('')
        $page.Add('# LLM campaign results')
        $page.Add('')
        $page.Add('> Generated by `llmproviders-test/lib/recap.sh` (or `recap.ps1`) at the end of every')
        $page.Add('> campaign: do not edit it by hand.')
        $page.Add('')
        $page.Add("Every model the campaign kit has run against its vendor's real API, with the latest result of")
        $page.Add("each mode. A row merges the reports of the model's latest campaign day: only reports of the")
        $page.Add('same model are merged, and a vision companion is a row of its own. Older days are in the')
        $page.Add("[campaign journal]($ReportUrl/README.md#campaigns-run).")
        $page.Add('')
        $page.Add('Modes M1 to M13 are the chat modes, D1 to D8 the decision-model modes; they are defined in the')
        $page.Add("[kit README]($ReportUrl/README.md#the-protocol-modes). ✅ passed, ❌ failed, ➖ not")
        $page.Add('applicable (the provider or the model does not declare the capability). The default models and')
        $page.Add('why they were chosen: [LLM provider comparison](llm-providers-comparison.md).')
        $page.Add('')
        $page.Add("Latest campaign: $latestDay · $($modelRows.Count) models · $($campaigns.Count) archived reports.")
    }
    $page.Add('')
    Add-ModelTable $page $Lang "$ReportUrl/"

    $stagingPage = "$Target.$PID"
    [System.IO.File]::WriteAllText(
        $stagingPage, ($page -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $stagingPage -Destination $Target -Force
    Write-Output $Target
}

if ($Root -eq $KitDir -and $campaigns.Count -gt 0 `
    -and (Test-Path -LiteralPath (Join-Path $DocsDir 'reference')) `
    -and (Test-Path -LiteralPath (Join-Path $DocsDir 'fr/reference'))) {
    Write-DocsPage 'en' (Join-Path $DocsDir 'reference/llm-campaign-results.md')
    Write-DocsPage 'fr' (Join-Path $DocsDir 'fr/reference/llm-campaign-results.md')
}
