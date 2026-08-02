<#
.SYNOPSIS
  Runs the LLM provider test protocol against real APIs and archives the proof.
  PowerShell mirror of run-campaign.sh, with the same option surface.
.DESCRIPTION
  These campaigns spend real credits on real accounts. -DryRun resolves everything and
  prints the plan without placing a single call; use it first.

  API keys are never accepted as an argument — the harness refuses them there and this
  script does not work around it. A key arrives through the environment, or through the
  `apiKey` field of a configuration file this script refuses to read unless it is named
  *.local.json or *.secrets.json (both gitignored).
.PARAMETER Provider
  Provider key: openai, anthropic, ollama, azure, groq, together, qwen, deepseek, kimi,
  mistral, huggingface, zai.
.PARAMETER All
  Run every provider declared in the configuration. Requires -Config.
.PARAMETER Model
  A model identifier, or a glob such as 'gpt-5.6-*'.
.PARAMETER Modes
  Comma-separated protocol modes. Defaults to every mode the harness supports.
.PARAMETER Config
  Campaign configuration file — see providers.schema.json.
.PARAMETER BaseUrl
  Endpoint override (Azure, regional mirrors).
.PARAMETER ApiVersion
  Azure api-version, deployment mode only.
.PARAMETER ApiKeyEnv
  Name of the environment variable holding the API key.
.PARAMETER MaxModels
  Cap on the number of models a wildcard may expand to. Default 5.
.PARAMETER DryRun
  Resolve and print the plan; place no call and write nothing.
.PARAMETER NoRecap
  Skip the index rebuild — for parallel runs. Rebuild once at the end with lib/recap.ps1.
.PARAMETER Timeout
  Per-request timeout in seconds. Default 180 — a cold local model must load first.
.PARAMETER Temperature
  Sampling temperature. Default 0, pinned so a verdict is reproducible. Raise it only for a
  model that rejects a pinned temperature.
.PARAMETER Out
  Root directory for the reports. Defaults to the kit directory.
.EXAMPLE
  .\run-campaign.ps1 -Provider ollama -Model llama3.2
.EXAMPLE
  .\run-campaign.ps1 -All -Config providers.local.json -DryRun
#>
[CmdletBinding()]
param(
    [string]$Provider = '',
    [switch]$All,
    [string]$Model = '',
    [string]$Modes = '',
    [string]$Config = '',
    [string]$BaseUrl = '',
    [string]$ApiVersion = '',
    [string]$ApiKeyEnv = '',
    [int]$MaxModels = 0,
    [switch]$DryRun,
    [switch]$NoRecap,
    # Null means "let the harness decide". A plain 0 default could not express that for
    # -Temperature, where 0 is itself the value we want to pin.
    [Nullable[int]]$Timeout = $null,
    [Nullable[double]]$Temperature = $null,
    [string]$Out = '',
    [string]$Configuration = 'Debug'
)
$ErrorActionPreference = 'Stop'
# The probe writes diagnostics to stderr and exits non-zero when a mode fails — by design.
# Left at its 7.4 default, this would turn both into terminating errors and abort the
# campaign at the first red mode, which is precisely the result we came to collect.
$PSNativeCommandUseErrorActionPreference = $false

$KitDir = $PSScriptRoot
$RepoRoot = Split-Path -Parent $KitDir
$CliProject = Join-Path $RepoRoot 'src/scripting/Orkeon.Scripting.Cli/Orkeon.Scripting.Cli.csproj'

$DefaultMaxModels = 5
# M11 (long context) and M14 (end-to-end crew) are manual on purpose — see the kit README.
$AllModes = 'M1,M2,M3,M4,M5,M6,M7,M8,M9,M10,M12,M13'

# Diagnostics go to stderr, exactly where the bash mirror puts them, so `--dry-run > plan.txt`
# yields the plan and nothing else on both platforms. Write-Host would not do: under
# `pwsh -File` it reaches stdout and survives a `6>` redirect, which quietly pollutes the plan.
function Write-Log  { param($Message) [Console]::Error.WriteLine($Message) }
function Write-Warn { param($Message) [Console]::Error.WriteLine("⚠  $Message") }
# Not Write-Error: with $ErrorActionPreference = 'Stop' it throws, and the exit code we chose
# never reaches the caller.
function Stop-Run   { param($Message) [Console]::Error.WriteLine("run-campaign: $Message"); exit 2 }

# ── Arguments ────────────────────────────────────────────────────────────────

if (-not $Provider -and -not $All) { Stop-Run 'pass -Provider <key> or -All (see Get-Help).' }
if ($Provider -and $All)           { Stop-Run '-Provider and -All are mutually exclusive.' }
if ($All -and -not $Config)        { Stop-Run '-All needs -Config: the provider list comes from it.' }

if (-not $Out) { $Out = $KitDir }
# A dry run promises to write nothing, and creating the output tree would already break that
# promise on a fresh -Out. Normalise the path without touching the filesystem instead.
if (-not $DryRun -and -not (Test-Path -LiteralPath $Out)) {
    New-Item -ItemType Directory -Force -Path $Out | Out-Null
}
$Out = if (Test-Path -LiteralPath $Out) { (Resolve-Path -LiteralPath $Out).Path }
       else { [System.IO.Path]::GetFullPath($Out) }

# ── Configuration file ───────────────────────────────────────────────────────

$campaignConfig = $null
if ($Config) {
    if (-not (Test-Path -LiteralPath $Config)) { Stop-Run "configuration file not found: $Config" }
    try { $campaignConfig = Get-Content -Raw -LiteralPath $Config | ConvertFrom-Json }
    catch { Stop-Run "configuration file is not valid JSON: $Config" }

    # A configuration carrying a literal key must be named so that .gitignore catches it.
    # This guard is the difference between a convention and a rule.
    $hasLiteralKey = $campaignConfig.providers.PSObject.Properties |
        Where-Object { $_.Value.PSObject.Properties.Name -contains 'apiKey' }

    if ($hasLiteralKey) {
        $name = Split-Path -Leaf $Config
        if ($name -notlike '*.local.json' -and $name -notlike '*.secrets.json') {
            Stop-Run ("$Config declares a literal apiKey but is not named *.local.json or " +
                      '*.secrets.json — that name is what keeps it out of git. Rename it, or ' +
                      'switch to apiKeyEnv.')
        }
    }
}

# Reads a value from the config: provider-level, then defaults, then the supplied fallback.
function Get-Setting {
    param([string]$ProviderKey, [string]$Field, $Fallback = '')

    if ($campaignConfig) {
        $entry = $campaignConfig.providers.PSObject.Properties[$ProviderKey]
        if ($entry -and ($entry.Value.PSObject.Properties.Name -contains $Field)) {
            $value = $entry.Value.$Field
            if ($value -is [array]) { return ($value -join ',') }
            if ($null -ne $value -and "$value" -ne '') { return "$value" }
        }
        if ($campaignConfig.defaults -and
            ($campaignConfig.defaults.PSObject.Properties.Name -contains $Field)) {
            return "$($campaignConfig.defaults.$Field)"
        }
    }

    return "$Fallback"
}

# The variable each vendor's own SDK reads, resolved through the catalogue's alias table.
#
# The built-in default used to be ORKEON_LLM_API_KEY for every provider, so any run without
# a -Config looked for a variable nobody exports. The probe refused and said so — on a
# stderr the kit discarded — and the campaign was archived as an empty report
# (deepseek-v4-pro, 2026-08-02). A default that is wrong for all twelve providers is not a
# default. -ApiKeyEnv and the configuration still win; the point is that the common case
# should need neither.
$script:Catalog = $null
function Get-CatalogKeyEnv {
    param([string]$ProviderKey)

    $path = Join-Path $KitDir 'lib/catalog.json'
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    if (-not $script:Catalog) {
        $script:Catalog = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    }

    $entry = $script:Catalog.providers.PSObject.Properties[$ProviderKey]
    if (-not $entry) { return '' }
    if ($entry.Value.aliasOf) {
        $entry = $script:Catalog.providers.PSObject.Properties[$entry.Value.aliasOf]
        if (-not $entry) { return '' }
    }
    return "$($entry.Value.apiKeyEnv)"
}

# ── The orkeon CLI ───────────────────────────────────────────────────────────

# Resolved on first use, not up front: a dry run that only expands literal model names never
# needs the CLI, and building it would be a side effect on a run that promised none.
$script:Orkeon = @()

function Resolve-Orkeon {
    if ($script:Orkeon.Count -gt 0) { return }

    if ($env:ORKEON_BIN) { $script:Orkeon = @($env:ORKEON_BIN); return }
    if (Get-Command orkeon -ErrorAction SilentlyContinue) { $script:Orkeon = @('orkeon'); return }

    $dll = Join-Path $RepoRoot "src/scripting/Orkeon.Scripting.Cli/bin/$Configuration/net10.0/orkeon.dll"
    if (-not (Test-Path -LiteralPath $dll)) {
        Write-Log "Building the orkeon CLI once ($Configuration) …"
        dotnet build $CliProject -c $Configuration -v q --nologo | Out-Null
    }
    if (-not (Test-Path -LiteralPath $dll)) {
        Stop-Run 'could not locate or build the orkeon CLI. Set ORKEON_BIN to point at it.'
    }
    $script:Orkeon = @('dotnet', $dll)
}

$Commit = try { (git -C $RepoRoot rev-parse --short HEAD 2>$null).Trim() } catch { '' }

function Invoke-Orkeon {
    param([string[]]$Arguments)
    Resolve-Orkeon
    # $script:Orkeon is either @('orkeon') or @('dotnet', '<path>.dll'); a bare 1..($n-1) slice
    # would silently reverse itself on the one-element form.
    $prefix = if ($script:Orkeon.Count -gt 1) { $script:Orkeon[1..($script:Orkeon.Count - 1)] } else { @() }
    # The splat operator takes a variable, not an expression: `@($prefix + $Arguments)` is an
    # array subexpression, and the whole command line ends up collapsed into one token.
    $all = @($prefix) + $Arguments
    & $script:Orkeon[0] @all
}

# ── Model resolution ─────────────────────────────────────────────────────────

function Test-Glob { param([string]$Value) $Value -match '[*?]' }

function ConvertTo-GlobRegex {
    param([string]$Pattern)
    $escaped = [regex]::Escape($Pattern).Replace('\*', '.*').Replace('\?', '.')
    return "^$escaped$"
}

# Live catalogue first, declared list as the fallback. A hand-maintained list is exactly
# what let four providers ship a retired default model for months.
function Resolve-Models {
    param([string]$ProviderKey, [string]$Pattern, [string]$KeyEnv, [string]$Base)

    $declared = @(Get-Setting $ProviderKey 'models' | Where-Object { $_ } |
        ForEach-Object { $_ -split ',' } | Where-Object { $_ })

    if (-not $Pattern) { return $declared }
    if (-not (Test-Glob $Pattern)) { return @($Pattern) }

    $arguments = @('llm', 'models', '-p', $ProviderKey, '--filter', $Pattern, '-k', $KeyEnv)
    if ($Base) { $arguments += @('-u', $Base) }

    # A catalogue listing is one identifier per line and identifiers never contain whitespace.
    # Anything else is a diagnostic that leaked into the stream, and treating it as a model
    # name would launch a campaign against nonsense.
    $discovered = @()
    try {
        $discovered = @(Invoke-Orkeon $arguments 2>$null |
            Where-Object { $_ -and ($_ -notmatch '\s') })
    } catch { $discovered = @() }
    if ($discovered.Count -gt 0) { return $discovered }

    Write-Warn "$ProviderKey : catalogue lookup failed — falling back to the models declared in the configuration."
    $regex = ConvertTo-GlobRegex $Pattern
    return @($declared | Where-Object { $_ -match $regex })
}

# ── One campaign ─────────────────────────────────────────────────────────────

$script:Failures = 0
$script:Ran = 0
$TmpDir = Join-Path ([System.IO.Path]::GetTempPath()) ("orkeon-campaign-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $TmpDir | Out-Null

function Invoke-Campaign {
    param(
        [string]$ProviderKey, [string]$ModelId, [string]$ModeList,
        [string]$KeyEnv, [string]$Base, [string]$Version
    )

    $slug = ($ModelId.ToCharArray() | ForEach-Object {
        if ($_ -match '[A-Za-z0-9._-]') { $_ } else { '_' } }) -join ''
    $stamp = [DateTime]::UtcNow.ToString('yyyy-MM-dd-HHmmss')
    $target = Join-Path $Out (Join-Path $ProviderKey "$stamp-$slug.md")

    if ($DryRun) {
        # Write-Output, not Write-Host: the plan is the product of a dry run and belongs on
        # stdout, where the bash mirror also puts it. Diagnostics stay on the host stream.
        Write-Output ('  would run  {0,-12} {1,-42} modes={2}  →  {3}/{4}' -f
            $ProviderKey, $ModelId, $ModeList, $ProviderKey, "$stamp-$slug.md")
        return
    }

    $arguments = @('llm', 'probe', '-p', $ProviderKey, '-m', $ModelId,
                   '--modes', $ModeList, '--format', 'json', '-k', $KeyEnv)
    if ($Base)    { $arguments += @('-u', $Base) }
    if ($Version) { $arguments += @('--api-version', $Version) }
    if ($Commit)  { $arguments += @('--commit', $Commit) }
    if ($null -ne $Timeout)     { $arguments += @('--timeout', $Timeout.ToString([cultureinfo]::InvariantCulture)) }
    # InvariantCulture matters: a French locale renders 0.5 as "0,5", which the parser rejects.
    if ($null -ne $Temperature) { $arguments += @('--temperature', $Temperature.ToString([cultureinfo]::InvariantCulture)) }

    $raw = Join-Path $TmpDir "$ProviderKey-$slug.json"
    $err = Join-Path $TmpDir "$ProviderKey-$slug.stderr"
    Write-Log "▶ $ProviderKey / $ModelId  [$ModeList]"

    # The probe exits non-zero when a mode fails. That is a result, not a crash: capture the
    # output either way, and let the report say what broke. Its diagnostics go to a file
    # rather than to $null — a probe that dies has already said why, and discarding that
    # left the operator with a bare "no usable JSON" and nothing to act on.
    $output = & { Invoke-Orkeon $arguments } 2>$err
    [System.IO.File]::WriteAllText($raw, ($output -join "`n"), [System.Text.UTF8Encoding]::new($false))

    # Assert on the fields the report actually reads, not merely that the payload parses:
    # the bash twin archived a hollow report because its guard stopped at "is this JSON".
    $campaign = $null
    try { $campaign = Get-Content -Raw -LiteralPath $raw | ConvertFrom-Json } catch { }
    if ((-not $campaign) -or ($null -eq $campaign.passed) -or ($null -eq $campaign.modes)) {
        Write-Warn "$ProviderKey / $ModelId : the probe produced no usable JSON."
        if (Test-Path -LiteralPath $err) {
            Get-Content -LiteralPath $err | ForEach-Object { Write-Warn "    $_" }
        }
        $script:Failures++
        return
    }

    & (Join-Path $KitDir 'lib/report.ps1') -Campaign $raw -Output $target | Out-Null
    $script:Ran++

    Write-Log ("  → {0} ✅  {1} ❌  {2} ➖   {3}/{4}" -f
        $campaign.passed, $campaign.failed, $campaign.notApplicable, $ProviderKey, "$stamp-$slug.md")
    if ($campaign.failed -gt 0) { $script:Failures++ }
}

function Invoke-Provider {
    param([string]$ProviderKey)

    # Command line wins over the configuration, which wins over the built-in default — the
    # usual precedence, so an operator can override one provider without editing the file.
    $modeList = if ($Modes)     { $Modes }     else { Get-Setting $ProviderKey 'modes' $AllModes }
    $keyEnv   = if ($ApiKeyEnv) { $ApiKeyEnv } else { Get-Setting $ProviderKey 'apiKeyEnv' (Get-CatalogKeyEnv $ProviderKey) }
    if (-not $keyEnv) { $keyEnv = 'ORKEON_LLM_API_KEY' }
    $base     = if ($BaseUrl)   { $BaseUrl }   else { Get-Setting $ProviderKey 'baseUrl' }
    $version  = if ($ApiVersion){ $ApiVersion} else { Get-Setting $ProviderKey 'apiVersion' }
    $cap      = if ($MaxModels -gt 0) { $MaxModels } else { [int](Get-Setting $ProviderKey 'maxModels' $DefaultMaxModels) }

    # A literal key from the configuration is set for the duration of this provider's
    # campaigns only: never onto a command line, where another process could read it.
    $literalKey = ''
    if ($campaignConfig) {
        $entry = $campaignConfig.providers.PSObject.Properties[$ProviderKey]
        if ($entry -and ($entry.Value.PSObject.Properties.Name -contains 'apiKey')) {
            $literalKey = $entry.Value.apiKey
        }
    }
    if ($literalKey) { Set-Item -Path "env:$keyEnv" -Value $literalKey }

    try {
        $models = @(Resolve-Models $ProviderKey $Model $keyEnv $base)

        if ($models.Count -eq 0) {
            Write-Warn "$ProviderKey : no model resolved — declare one under providers.$ProviderKey.models, or pass -Model."
            $script:Failures++
            return
        }

        if ($models.Count -gt $cap) {
            # Say what was dropped. A silent cap reads as full coverage.
            Write-Warn ("$ProviderKey : $($models.Count) models matched, capping at $cap (-MaxModels). " +
                        "Not exercised: " + (($models | Select-Object -Skip $cap) -join ' '))
            $models = $models | Select-Object -First $cap
        }

        foreach ($m in $models) { Invoke-Campaign $ProviderKey $m $modeList $keyEnv $base $version }
    }
    finally {
        if ($literalKey) { Remove-Item -Path "env:$keyEnv" -ErrorAction SilentlyContinue }
    }
}

# ── Drive ────────────────────────────────────────────────────────────────────

try {
    if ($DryRun) {
        Write-Log 'Dry run — resolving the plan, placing no call.'
        Write-Log ''
    }

    $providers = if ($All) {
        $campaignConfig.providers.PSObject.Properties.Name | Sort-Object
    } else {
        @($Provider)
    }

    foreach ($p in $providers) { Invoke-Provider $p }

    if ($DryRun) {
        Write-Log ''
        Write-Log 'Nothing was called and nothing was written.'
        exit 0
    }

    Write-Log ''
    if ($NoRecap) {
        # Two campaigns launched in parallel both finish holding a snapshot of the reports
        # taken before the other one landed. Whichever rebuilds last writes a complete but
        # stale index. -NoRecap defers it so the caller rebuilds once, after both finished.
        Write-Log "$script:Ran campaign(s) archived under $Out — index NOT rebuilt (-NoRecap)."
        Write-Log "Rebuild it once every parallel run has finished:  lib/recap.ps1 -Root $Out"
    }
    else {
        & (Join-Path $KitDir 'lib/recap.ps1') -Root $Out | Out-Null
        Write-Log "$script:Ran campaign(s) archived under $Out — index rebuilt."
    }

    if ($script:Failures -gt 0) {
        Write-Log "$script:Failures campaign(s) reported a failed mode. That is the point of the exercise; read the reports."
        exit 1
    }
}
finally {
    Remove-Item -Recurse -Force -LiteralPath $TmpDir -ErrorAction SilentlyContinue
}
