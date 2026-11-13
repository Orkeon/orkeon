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
        # The reasoning steps (M15 to M17): absent from older reports.
        Steps    = @($campaign.modes | Where-Object { $_.mode -ne 'M18' -and $_.PSObject.Properties['reasoning'] } | ForEach-Object { $_.reasoning } | ForEach-Object {
            [pscustomobject]@{
                Step    = [string]$_.step
                Outcome = [string]$_.outcome
                Sent    = [string]$_.sentValue
                Trace   = ($_.PSObject.Properties['trace'] -and [bool]$_.trace)
            } })
        # The raw vendor matrix (M18): its steps are vendor fragments with their own labels.
        Raw      = @($campaign.modes | Where-Object { $_.mode -eq 'M18' -and $_.PSObject.Properties['reasoning'] } | ForEach-Object { $_.reasoning } | ForEach-Object {
            [pscustomobject]@{
                Step    = [string]$_.step
                Outcome = [string]$_.outcome
                Trace   = ($_.PSObject.Properties['trace'] -and [bool]$_.trace)
            } })
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

# The reasoning steps of each model's latest campaign day: the reports of that day are merged,
# the latest result of each step winning (same rule as the model table, and as lib/recap.sh).
# Columns: on off low medium high xhigh max budget; any other step is a vendor value, listed in
# Other as "step cell"; Trace is a check mark when any step of the day carried a reasoning trace.
$ReasoningColumns = @('on', 'off', 'low', 'medium', 'high', 'xhigh', 'max', 'budget')

function Get-ReasoningRows {
    $catalog = Get-Content -Raw -LiteralPath $CatalogPath | ConvertFrom-Json
    $labels = @{}
    foreach ($entry in $catalog.providers.PSObject.Properties) {
        if ($null -ne $entry.Value.aliasOf) { continue }
        $labels[$entry.Name] = [string]$entry.Value.label
    }

    $groups = @{}
    foreach ($c in ($campaigns | Where-Object { $_.Steps.Count -gt 0 })) {
        $key = "$($c.Provider)`t$($c.Model)"
        if (-not $groups.ContainsKey($key)) { $groups[$key] = [System.Collections.Generic.List[object]]::new() }
        $groups[$key].Add($c)
    }

    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($group in $groups.Values) {
        $day = ($group | ForEach-Object { $_.Stamp.Substring(0, 10) } | Sort-Object | Select-Object -Last 1)
        $sameDay = @($group | Where-Object { $_.Stamp.Substring(0, 10) -eq $day } | Sort-Object Stamp)
        $res = [ordered]@{}
        $trace = $false
        foreach ($c in $sameDay) {
            foreach ($st in $c.Steps) {
                $res[$st.Step] = switch ($st.Outcome) {
                    'sent'    { '✅' }
                    'lowered' { "→$($st.Sent)" }
                    'omitted' { '➖' }
                    default   { '❌' }
                }
                if ($st.Trace) { $trace = $true }
            }
        }
        $cells = foreach ($k in $ReasoningColumns) { if ($res.Contains($k)) { $res[$k] } else { '' } }
        $other = foreach ($k in $res.Keys) { if ($ReasoningColumns -cnotcontains $k) { "$k $($res[$k])" } }
        $provider = $group[0].Provider
        $rows.Add([pscustomobject]@{
            Provider = $provider
            Label    = if ($labels.ContainsKey($provider) -and $labels[$provider]) { $labels[$provider] } else { $provider }
            Model    = $group[0].Model
            Day      = $day
            Rest     = '| ' + (@($cells) -join ' | ') + ' | ' + (@($other) -join ', ') + ' | ' + $(if ($trace) { '✅' } else { '' }) + ' |'
        })
    }
    $rows.Sort([Comparison[object]] {
        param($a, $b)
        $c = [string]::CompareOrdinal($a.Provider, $b.Provider); if ($c -ne 0) { return $c }
        return [string]::CompareOrdinal($a.Model, $b.Model)
    })
    return , $rows
}

# Renders the reasoning table into $Target, or one sentence when no report carries reasoning steps.
function Add-ReasoningTable($Target, [string]$Lang) {
    if ($reasoningRows.Count -eq 0) {
        if ($Lang -eq 'fr') { $Target.Add("Aucun rapport archivé ne porte encore d'étapes de raisonnement (modes M15 à M17) : le tableau apparaîtra à la prochaine campagne qui les exécute.") }
        else { $Target.Add('No archived report carries reasoning steps yet (modes M15 to M17): the table appears with the next campaign that runs them.') }
        return
    }
    if ($Lang -eq 'fr') { $Target.Add('| Fournisseur | Modèle | Date | on | off | low | medium | high | xhigh | max | budget | Autres | Trace |') }
    else { $Target.Add('| Provider | Model | Date | on | off | low | medium | high | xhigh | max | budget | Other | Trace |') }
    $Target.Add('|---|---|---|---|---|---|---|---|---|---|---|---|---|')
    $previous = ''
    foreach ($row in $reasoningRows) {
        $cell = if ($row.Provider -ceq $previous) { '' } else { $row.Label }
        $previous = $row.Provider
        $Target.Add("| $cell | ``$($row.Model)`` | $($row.Day) $($row.Rest)")
    }
}

# The vendor reasoning matrix (M18): one row per model, from the latest report that ran M18 (not
# merged across days). Accepted and Refused list the step labels, Trace the labels whose step
# returned a reasoning trace; a label is code-formatted. Same rule as lib/recap.sh.
function Get-MatrixRows {
    $catalog = Get-Content -Raw -LiteralPath $CatalogPath | ConvertFrom-Json
    $labels = @{}
    foreach ($entry in $catalog.providers.PSObject.Properties) {
        if ($null -ne $entry.Value.aliasOf) { continue }
        $labels[$entry.Name] = [string]$entry.Value.label
    }

    $groups = @{}
    foreach ($c in ($campaigns | Where-Object { $_.Raw.Count -gt 0 })) {
        $key = "$($c.Provider)`t$($c.Model)"
        if (-not $groups.ContainsKey($key)) { $groups[$key] = [System.Collections.Generic.List[object]]::new() }
        $groups[$key].Add($c)
    }

    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($group in $groups.Values) {
        $latest = ($group | ForEach-Object { $_.Stamp } | Sort-Object | Select-Object -Last 1)
        $steps = @($group | Where-Object { $_.Stamp -eq $latest } | ForEach-Object { $_.Raw } | ForEach-Object { $_ })
        $accepted = ($steps | Where-Object { $_.Outcome -eq 'accepted' } | ForEach-Object { "``$($_.Step)``" }) -join ', '
        $refused = ($steps | Where-Object { $_.Outcome -eq 'refused' } | ForEach-Object { "``$($_.Step)``" }) -join ', '
        $traced = ($steps | Where-Object { $_.Trace } | ForEach-Object { "``$($_.Step)``" }) -join ', '
        $provider = $group[0].Provider
        $rows.Add([pscustomobject]@{
            Provider = $provider
            Label    = if ($labels.ContainsKey($provider) -and $labels[$provider]) { $labels[$provider] } else { $provider }
            Model    = $group[0].Model
            Day      = $latest.Substring(0, 10)
            Rest     = "| $accepted | $refused | $traced |"
        })
    }
    $rows.Sort([Comparison[object]] {
        param($a, $b)
        $c = [string]::CompareOrdinal($a.Provider, $b.Provider); if ($c -ne 0) { return $c }
        return [string]::CompareOrdinal($a.Model, $b.Model)
    })
    return , $rows
}

# Renders the vendor reasoning matrix into $Target, or one sentence when no report ran M18 yet.
function Add-MatrixTable($Target, [string]$Lang) {
    if ($matrixRows.Count -eq 0) {
        if ($Lang -eq 'fr') { $Target.Add("Aucun rapport archivé ne porte encore la matrice brute (mode M18) : le tableau apparaîtra à la prochaine campagne qui l'exécute.") }
        else { $Target.Add('No archived report carries the raw matrix yet (mode M18): the table appears with the next campaign that runs it.') }
        return
    }
    if ($Lang -eq 'fr') { $Target.Add('| Fournisseur | Modèle | Date | Acceptés | Refusés | Trace |') }
    else { $Target.Add('| Provider | Model | Date | Accepted | Refused | Trace |') }
    $Target.Add('|---|---|---|---|---|---|')
    $previous = ''
    foreach ($row in $matrixRows) {
        $cell = if ($row.Provider -ceq $previous) { '' } else { $row.Label }
        $previous = $row.Provider
        $Target.Add("| $cell | ``$($row.Model)`` | $($row.Day) $($row.Rest)")
    }
}

$modelRows = if ($campaigns.Count -gt 0) { Get-ModelRows } else { [System.Collections.Generic.List[object]]::new() }
$reasoningRows = if ($campaigns.Count -gt 0) { Get-ReasoningRows } else { [System.Collections.Generic.List[object]]::new() }
$matrixRows = if ($campaigns.Count -gt 0) { Get-MatrixRows } else { [System.Collections.Generic.List[object]]::new() }
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
    $lines.Add('')
    $lines.Add('## Reasoning per model')
    $lines.Add('')
    $lines.Add('One row per model that ran the reasoning modes (M15 thinking toggle, M16 effort sweep, M17 budget),')
    $lines.Add("from the model's latest campaign day. A cell is ✅ when the value was sent as asked, ``→low`` when")
    $lines.Add('Orkeon lowered it to the nearest value the model takes, ➖ when it was left out, ❌ when the vendor')
    $lines.Add('refused what Orkeon sent, and empty when the step was not run. `Other` lists vendor values outside')
    $lines.Add('the usual set (`none ✅`); `Trace` is ✅ when a reasoning trace came back.')
    $lines.Add('')
    Add-ReasoningTable $lines 'en'
    $lines.Add('')
    $lines.Add('## Vendor reasoning matrix')
    $lines.Add('')
    $lines.Add("M18 sends each fragment of the catalog's ``rawReasoning`` list to the vendor exactly as written,")
    $lines.Add("bypassing Orkeon's own judgement of what a model supports: the table shows what the vendor itself")
    $lines.Add("accepts, from the model's latest report that ran M18. `"Accepted`" means HTTP 2xx, not that the value")
    $lines.Add('had an effect: compare the traces and the completion tokens in the reports. `Trace` lists the')
    $lines.Add('fragments after which a reasoning trace came back.')
    $lines.Add('')
    Add-MatrixTable $lines 'en'
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
        $page.Add('Les modes M1 à M18 sont ceux du chat, D1 à D8 ceux des modèles de décision ; ils sont définis dans')
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
        $page.Add('Modes M1 to M18 are the chat modes, D1 to D8 the decision-model modes; they are defined in the')
        $page.Add("[kit README]($ReportUrl/README.md#the-protocol-modes). ✅ passed, ❌ failed, ➖ not")
        $page.Add('applicable (the provider or the model does not declare the capability). The default models and')
        $page.Add('why they were chosen: [LLM provider comparison](llm-providers-comparison.md).')
        $page.Add('')
        $page.Add("Latest campaign: $latestDay · $($modelRows.Count) models · $($campaigns.Count) archived reports.")
    }
    $page.Add('')
    Add-ModelTable $page $Lang "$ReportUrl/"
    $page.Add('')
    if ($Lang -eq 'fr') {
        $page.Add('## Raisonnement par modèle')
        $page.Add('')
        $page.Add('Les modes M15 (bascule du raisonnement), M16 (balayage des efforts) et M17 (budget) sont décrits dans le')
        $page.Add("[README du kit]($ReportUrl/README.md#the-protocol-modes). ✅ la valeur est partie telle quelle,")
        $page.Add("``→low`` Orkeon l'a abaissée à la valeur la plus proche que le modèle accepte, ➖ rien n'a été envoyé,")
        $page.Add("❌ le fournisseur a refusé ce qu'Orkeon a envoyé, vide l'étape n'a pas été exécutée. ``Autres`` liste les")
        $page.Add("valeurs propres au fournisseur ; ``Trace`` est ✅ lorsqu'une trace de raisonnement est revenue.")
    }
    else {
        $page.Add('## Reasoning per model')
        $page.Add('')
        $page.Add('Modes M15 (thinking toggle), M16 (effort sweep) and M17 (budget) are described in the')
        $page.Add("[kit README]($ReportUrl/README.md#the-protocol-modes). ✅ the value was sent as asked,")
        $page.Add('`→low` Orkeon lowered it to the nearest value the model takes, ➖ nothing was sent, ❌ the vendor')
        $page.Add('refused what Orkeon sent, empty the step was not run. `Other` lists vendor-specific values;')
        $page.Add('`Trace` is ✅ when a reasoning trace came back.')
    }
    $page.Add('')
    Add-ReasoningTable $page $Lang
    $page.Add('')
    if ($Lang -eq 'fr') {
        $page.Add('## Matrice brute du fournisseur')
        $page.Add('')
        $page.Add("M18 envoie chaque fragment de la liste ``rawReasoning`` du catalogue au fournisseur tel quel, sans")
        $page.Add("passer par le jugement d'Orkeon sur ce qu'un modèle accepte : le tableau montre ce que le")
        $page.Add("fournisseur lui-même accepte, d'après le dernier rapport du modèle qui a exécuté M18. « Acceptés »")
        $page.Add("signifie HTTP 2xx, pas que la valeur ait eu un effet : comparer les traces et les jetons de")
        $page.Add("complétion dans les rapports. ``Trace`` liste les fragments après lesquels une trace de")
        $page.Add('raisonnement est revenue.')
    }
    else {
        $page.Add('## Vendor reasoning matrix')
        $page.Add('')
        $page.Add("M18 sends each fragment of the catalog's ``rawReasoning`` list to the vendor exactly as written,")
        $page.Add("bypassing Orkeon's own judgement of what a model supports: the table shows what the vendor itself")
        $page.Add("accepts, from the model's latest report that ran M18. `"Accepted`" means HTTP 2xx, not that the value")
        $page.Add('had an effect: compare the traces and the completion tokens in the reports. `Trace` lists the')
        $page.Add('fragments after which a reasoning trace came back.')
    }
    $page.Add('')
    Add-MatrixTable $page $Lang

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
