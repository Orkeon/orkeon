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

$commit = if ([string]::IsNullOrWhiteSpace($c.commit)) { 'non fourni' } else { $c.commit }
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
    if ($c.failed -gt 0) { "❌ **Échec** — $($c.failed) mode(s) en échec" }
    elseif ($c.passed -eq 0) { '➖ **Rien exercé** — aucun mode applicable à ce provider' }
    else { "✅ **Succès** — $($c.passed) mode(s) validé(s)" }

$outDir = Split-Path -Parent $Output
if ($outDir -and -not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

$date = $stamp.Split('T')[0]
$fileName = Split-Path -Leaf $Output

$lines = [System.Collections.Generic.List[string]]::new()
$add = { param($text) $lines.Add([string]$text) }

& $add "# Campagne $label — ``$($c.model)``"
& $add ''
& $add '|  |  |'
& $add '|---|---|'
& $add "| **Provider** | ``$($c.provider)`` |"
& $add "| **Modèle** | ``$($c.model)`` |"
& $add "| **Endpoint** | ``$($c.endpointHost)`` |"
& $add "| **Horodatage (UTC)** | $stamp |"
& $add "| **Version Orkéon** | $($c.orkeonVersion) |"
& $add "| **Commit** | ``$commit`` |"
& $add "| **Modes exercés** | $modesRun |"
# Older campaigns predate the field; "non épinglée" is the honest rendering of a run whose
# sampling was left at the framework default, and reads as the caveat it is.
$temperature = if ($null -ne $c.PSObject.Properties['temperature']) {
    $c.temperature.ToString([cultureinfo]::InvariantCulture)
} else { 'non épinglée' }
& $add "| **Température** | $temperature |"
& $add '| **Qualité de preuve** | sortie archivée |'
& $add ''
& $add '## Résultats'
& $add ''
& $add '| Mode | Protocole | Résultat | Détail | Durée |'
& $add '|---|---|---|---|---|'

foreach ($mode in $c.modes) {
    $protocol = $catalog.modes.PSObject.Properties[$mode.mode].Value
    $detail = $mode.detail -replace '\|', '/'
    & $add "| $($mode.mode) | $protocol | $($mode.symbol) | $detail | $($mode.elapsedMs) ms |"
}

& $add ''
& $add "$verdict · $($c.passed) ✅ · $($c.failed) ❌ · $($c.notApplicable) ➖"
& $add ''
& $add '> ➖ = mode non applicable à ce provider ou à ce modèle. Rien n''a été exercé, il n''y a'
& $add '> donc rien à corriger — c''est une absence de capacité, pas un défaut.'

if ($canonical -and $canonical.notes) {
    & $add ''
    & $add "## Points de vigilance (matrice $section)"
    & $add ''
    foreach ($note in $canonical.notes) { & $add "- $note" }
}

$sectionOrDefault = if ($section) { $section } else { '§6' }

& $add ''
& $add '## À reporter dans la matrice'
& $add ''
& $add 'Journal (§7) :'
& $add ''
& $add '```'
& $add "| $date | $label ``$($c.model)`` | campagne ``llmproviders-test`` | $modesRun | $status | ``llmproviders-test/$($c.provider)/$fileName`` | Sortie archivée |"
& $add '```'
& $add ''
& $add "Tableau modèles ($sectionOrDefault) :"
& $add ''
& $add '```'
& $add "| ``$($c.model)`` | | | $modesRun | $status | $date | $($c.orkeonVersion) | campagne $fileName |"
& $add '```'
& $add ''
& $add $JsonMarker
& $add '```json'
& $add $rawJson.TrimEnd()
& $add '```'

# UTF-8 without BOM, LF endings: the report is versioned and must be byte-identical to what
# the bash mirror produces, whichever platform ran the campaign.
$content = ($lines -join "`n") + "`n"
[System.IO.File]::WriteAllText(
    [System.IO.Path]::GetFullPath($Output), $content, [System.Text.UTF8Encoding]::new($false))

Write-Output $Output
