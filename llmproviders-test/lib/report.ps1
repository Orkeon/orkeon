<#
.SYNOPSIS
  Renders one campaign — the JSON emitted by `orkeon llm probe --format json` — as the
  archived Markdown report. PowerShell mirror of lib/report.sh.
.DESCRIPTION
  The JSON is embedded verbatim at the end of the report behind a marker comment, so the
  index can be rebuilt from the reports alone. One file per campaign, still machine-readable.

  Nothing here invents data: every value comes from the probe's own output, which is built
  from observed behaviour and never carries a credential.
.PARAMETER Campaign
  Path to the campaign JSON produced by the probe.
.PARAMETER Output
  Path of the Markdown report to write.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Campaign,
    [Parameter(Mandatory)][string]$Output
)
$ErrorActionPreference = 'Stop'

$LibDir = $PSScriptRoot
$JsonMarker = '<!-- orkeon-campaign-json -->'

$rawJson = Get-Content -Raw -LiteralPath $Campaign
$c = $rawJson | ConvertFrom-Json
$catalog = Get-Content -Raw -LiteralPath (Join-Path $LibDir 'catalog.json') | ConvertFrom-Json

$commit = if ([string]::IsNullOrWhiteSpace($c.commit)) { 'not supplied' } else { $c.commit }
$modesRun = ($c.modes | ForEach-Object { $_.mode }) -join ', '

# ConvertFrom-Json turns an ISO-8601 string into a [DateTime], which would render in the
# host's own format — and the report is versioned, so it must read the same everywhere.
$stamp = if ($c.timestampUtc -is [DateTime]) {
    $c.timestampUtc.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
} else { [string]$c.timestampUtc }

# An alias resolves to the same notes as its canonical key: "hf" must read like "huggingface".
$entry = $catalog.providers.PSObject.Properties[$c.provider].Value
$canonicalKey = if ($entry -and $entry.aliasOf) { $entry.aliasOf } else { $c.provider }
$canonical = $catalog.providers.PSObject.Properties[$canonicalKey].Value
$label = if ($canonical -and $canonical.label) { $canonical.label } else { $c.provider }
$section = if ($canonical -and $canonical.matrix) { $canonical.matrix } else { '' }

$status = if ($c.failed -gt 0) { '❌' } else { '✅' }
$verdict =
    if ($c.failed -gt 0) { "❌ **Failure** — $($c.failed) failed mode(s)" }
    elseif ($c.passed -eq 0) { '➖ **Nothing exercised** — no mode applicable to this provider' }
    else { "✅ **Success** — $($c.passed) mode(s) validated" }

$outDir = Split-Path -Parent $Output
if ($outDir -and -not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

$date = $stamp.Split('T')[0]
$fileName = Split-Path -Leaf $Output

$lines = [System.Collections.Generic.List[string]]::new()
$add = { param($text) $lines.Add([string]$text) }

& $add "# Campaign $label — ``$($c.model)``"
& $add ''
& $add '|  |  |'
& $add '|---|---|'
& $add "| **Provider** | ``$($c.provider)`` |"
& $add "| **Model** | ``$($c.model)`` |"
& $add "| **Endpoint** | ``$($c.endpointHost)`` |"
& $add "| **Timestamp (UTC)** | $stamp |"
& $add "| **Commit** | ``$commit`` |"
& $add "| **Modes exercised** | $modesRun |"
# Older campaigns predate the field; "not pinned" is the honest rendering of a run whose
# sampling was left at the framework default, and reads as the caveat it is.
$temperature = if ($null -ne $c.PSObject.Properties['temperature']) {
    $c.temperature.ToString([cultureinfo]::InvariantCulture)
} else { 'not pinned' }
& $add "| **Temperature** | $temperature |"
# Reasoning efforts pinned by the per-model registry (requiredParams): present only when
# the campaign had to depart from the default.
if ($c.PSObject.Properties['thinking_effort'] -and $c.thinking_effort) {
    & $add "| **Reasoning effort (base)** | $($c.thinking_effort) |"
}
if ($c.PSObject.Properties['m7_thinking_effort'] -and $c.m7_thinking_effort) {
    & $add "| **Reasoning effort (M7)** | $($c.m7_thinking_effort) |"
}
if ($c.PSObject.Properties['sweepEfforts'] -and $c.sweepEfforts) {
    & $add "| **Sweep efforts (M16)** | $($c.sweepEfforts) |"
}
& $add '| **Proof quality** | archived output |'
& $add ''
& $add '## Results'
& $add ''
& $add '| Mode | Protocol | Result | Detail | Duration |'
& $add '|---|---|---|---|---|'

foreach ($mode in $c.modes) {
    $protocol = $catalog.modes.PSObject.Properties[$mode.mode].Value
    $detail = $mode.detail -replace '\|', '/'
    & $add "| $($mode.mode) | $protocol | $($mode.symbol) | $detail | $($mode.elapsedMs) ms |"
}

& $add ''
& $add "$verdict · $($c.passed) ✅ · $($c.failed) ❌ · $($c.notApplicable) ➖"
& $add ''
& $add '> ➖ = mode not applicable to this provider or this model. Nothing was exercised, so there is'
& $add '> nothing to fix — it is an absence of capability, not a defect.'

# The reasoning steps of M15 to M17, one row each: only when a mode carries them.
$hasReasoning = @($c.modes | Where-Object { $_.PSObject.Properties['reasoning'] -and @($_.reasoning).Count -gt 0 }).Count -gt 0
if ($hasReasoning) {
    $esc = { param($v) ([string]$v) -replace "`r?`n", ' ' -replace '\|', '\|' }
    & $add ''
    & $add '## Reasoning'
    & $add ''
    & $add '| Mode | Step | Outcome | Sent | Trace | Reasoning tokens | Finish | Note |'
    & $add '|---|---|---|---|---|---|---|---|'
    foreach ($mode in $c.modes) {
        if (-not $mode.PSObject.Properties['reasoning']) { continue }
        foreach ($s in @($mode.reasoning)) {
            $outcome = switch ($s.outcome) {
                'sent'    { '✅ sent' }
                'lowered' { "✅ lowered→$(if ($s.sentValue) { $s.sentValue } else { '?' })" }
                'omitted' { '➖ omitted' }
                'accepted' { '✅ accepted' }
                'refused'  { '❌ refused' }
                default   { '❌ rejected' }
            }
            $sent = if ($s.sentValue) { $s.sentValue } elseif ($s.PSObject.Properties['wire'] -and $s.wire) { $s.wire } else { '—' }
            $trace = if ($s.trace) { '✅' } else { '—' }
            $tokens = if ($null -ne $s.reasoningTokens) { $s.reasoningTokens } else { '—' }
            $finish = if ($s.finishReason) { $s.finishReason } else { '—' }
            $noteParts = @()
            if ($s.error) {
                $errorText = [string]$s.error
                if ($mode.mode -eq 'M18' -and $errorText.Length -gt 160) { $errorText = $errorText.Substring(0, 160) }
                $noteParts += $errorText
            }
            if ($s.warnings) { $noteParts += @($s.warnings | ForEach-Object { [string]$_ }) }
            $note = $noteParts -join '; '
            $cells = @($mode.mode, $s.step, $outcome, $sent, $trace, $tokens, $finish, $note) | ForEach-Object { & $esc $_ }
            & $add ('| ' + ($cells -join ' | ') + ' |')
        }
    }
    & $add ''
    & $add '> ✅ sent = the value went out as asked; ✅ lowered = Orkeon sent a nearby supported value; ➖ omitted ='
    & $add '> nothing was sent (the model does not take it); ❌ rejected = the vendor refused what Orkeon sent.'
    & $add '> M18 steps: ✅ accepted = the vendor answered 2xx to the raw fragment shown in Sent (not proof that it had'
    & $add '> an effect: compare Trace and tokens); ❌ refused = it did not, the start of its answer is in Note.'
}

# The provider's notes are not copied: a copy is frozen the day it is written, and a note
# about a campaign is stale at the next one. The report points at the one place the notes
# are kept current (same rule as report.sh).
if ($canonical -and $canonical.notes) {
    & $add ''
    & $add "## Points of attention (matrix $section)"
    & $add ''
    & $add "Kept current in ``llmproviders-test/lib/catalog.json``, ``providers.$canonicalKey.notes``."
}

$sectionOrDefault = if ($section) { $section } else { '§6' }

& $add ''
& $add '## To carry into the matrix'
& $add ''
& $add 'Journal (§7):'
& $add ''
& $add '```'
& $add "| $date | $label ``$($c.model)`` | campaign ``llmproviders-test`` | $modesRun | $status | ``llmproviders-test/$($c.provider)/$fileName`` | Archived output |"
& $add '```'
& $add ''
& $add "Model table ($sectionOrDefault):"
& $add ''
& $add '```'
& $add "| ``$($c.model)`` | | | $modesRun | $status | $date | $commit | campaign $fileName |"
& $add '```'
& $add ''
& $add $JsonMarker
& $add '```json'
# The report names the commit, never an Orkeon version: the version goes stale at the
# next tag, the commit does not. The probe's own field is left out of the archive.
& $add ((($rawJson.TrimEnd() -split "`n") | Where-Object { $_ -notmatch '^\s*"orkeonVersion":' }) -join "`n")
& $add '```'

# UTF-8 without BOM, LF endings: the report is versioned and must be byte-identical to what
# the bash mirror produces, whichever platform ran the campaign.
$content = ($lines -join "`n") + "`n"
[System.IO.File]::WriteAllText(
    [System.IO.Path]::GetFullPath($Output), $content, [System.Text.UTF8Encoding]::new($false))

Write-Output $Output
