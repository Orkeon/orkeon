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
  Provider key, or several separated by commas: openai, anthropic, ollama, azure,
  together, qwen, deepseek, kimi, mistral, huggingface, zai, gemini, grok, minimax,
  openrouter, mammouth; decision providers (modes D1-D8): typesafe, liquid, kev.
.PARAMETER All
  Run every provider declared in the configuration. Requires -Config.
.PARAMETER Parallel
  Run the selected providers concurrently. Each provider's own models still run one after
  another: they share its rate limit, and racing them would measure the throttle rather than
  the protocol. Output is buffered per provider and printed in the order you named them, so a
  parallel run reads exactly like a sequential one.
.PARAMETER TallyFile
  Internal. Where a child process writes its "<archived> <failed>" counts so the parent can
  add them up. Set by -Parallel; never pass it by hand.
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

.PARAMETER WorkspaceId
  Workspace id for workspace-scoped keys (Anthropic identity-linked keys require it).
.PARAMETER ApiKeyEnv
  Name of the environment variable holding the API key.
.PARAMETER MaxModels
  Cap on the number of models a wildcard may expand to. Default 5.
.PARAMETER Candidates
  After each provider's default, also run the successors the catalogue records as
  candidateModels (review of 2026-09-19), same modes — the campaign that can promote one of
  them to default. Automatic path only: an explicit -Model is already a choice.
.PARAMETER DryRun
  Resolve and print the plan; place no call and write nothing.
.PARAMETER NoPreflight
  Skip the one-call check that opens each provider. By default each provider opens with one
  call of its first mode on its first model; a refused key, an empty balance or an exhausted
  quota skips the provider there, instead of failing every mode of every model in turn.
.PARAMETER NoRecap
  Skip the index rebuild — for parallel runs. Rebuild once at the end with lib/recap.ps1.
.PARAMETER Timeout
  Per-request timeout in seconds. Default 180 — a cold local model must load first.
.PARAMETER Temperature
  Sampling temperature. Default 0, pinned so a verdict is reproducible. Raise it only for a
  model that rejects a pinned temperature.
.PARAMETER ThinkingEffort
  Base reasoning effort override (e.g. none) — for models that refuse tools while
  reasoning; the catalogue's per-model requiredParams supplies it automatically when unset.
.PARAMETER Out
  Root directory for the reports. Defaults to the kit directory.
.EXAMPLE
  .\run-campaign.ps1 -Provider ollama -Model llama3.2
.EXAMPLE
  .\run-campaign.ps1 -Provider deepseek,zai,ollama -Parallel
.EXAMPLE
  .\run-campaign.ps1 -All -Config providers.local.json -DryRun
#>
[CmdletBinding()]
param(
    [string]$Provider = '',
    [switch]$All,
    [switch]$Candidates,
    [string]$Model = '',
    [string]$Modes = '',
    [string]$Config = '',
    [string]$BaseUrl = '',
    [string]$ApiVersion = '',
    [string]$WorkspaceId = '',
    [string]$ApiKeyEnv = '',
    [int]$MaxModels = 0,
    [switch]$DryRun,
    [switch]$NoRecap,
    [switch]$NoPreflight,
    [switch]$Parallel,
    [string]$TallyFile = '',
    # Null means "let the harness decide". A plain 0 default could not express that for
    # -Temperature, where 0 is itself the value we want to pin.
    [Nullable[int]]$Timeout = $null,
    [Nullable[double]]$Temperature = $null,
    [string]$ThinkingEffort = '',
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
$ServedPath = Join-Path $KitDir 'lib/served-models.json'

$DefaultMaxModels = 5
# M11 (long context) and M14 (end-to-end crew) are manual on purpose — see the kit README.
$AllModes = 'M1,M2,M3,M4,M5,M6,M7,M8,M9,M10,M12,M13,M15,M16,M17'

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
# (deepseek-v4-pro, 2026-08-02). A default that is wrong for every provider is not a
# default. -ApiKeyEnv and the configuration still win; the point is that the common case
# should need neither.
$script:Catalog = $null
function Get-CatalogField {
    param([string]$ProviderKey, [string]$Field)

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
    return "$($entry.Value.$Field)"
}

function Get-CatalogKeyEnv { param([string]$ProviderKey) Get-CatalogField $ProviderKey 'apiKeyEnv' }

# The successors a catalogue review recorded for a provider's default.
function Get-CatalogCandidates {
    param([string]$ProviderKey)

    $path = Join-Path $KitDir 'lib/catalog.json'
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    if (-not $script:Catalog) {
        $script:Catalog = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    }

    $entry = $script:Catalog.providers.PSObject.Properties[$ProviderKey]
    if (-not $entry) { return @() }
    if ($entry.Value.aliasOf) {
        $entry = $script:Catalog.providers.PSObject.Properties[$entry.Value.aliasOf]
        if (-not $entry) { return @() }
    }
    return @($entry.Value.candidateModels | ForEach-Object { "$($_.id)" } | Where-Object { $_ })
}

# A parameter value one MODEL demands, from the per-model registry. Per model and not per
# provider on purpose: gpt-5.6-sol demands temperature 1 and reasoning_effort none while
# gpt-4o-mini, same provider, rejects reasoning_effort outright (both measured 2026-08-30).
function Get-CatalogModelParam {
    param([string]$ProviderKey, [string]$ModelId, [string]$Field)

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
    $params = $entry.Value.PSObject.Properties['requiredParams']
    if (-not $params) { return '' }
    $model = $params.Value.PSObject.Properties[$ModelId]
    if (-not $model) { return '' }
    return "$($model.Value.$Field)"
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
    # Always build: an existing dll may come from another branch, and the report would name HEAD's
    # commit for code that did not run. The build is incremental, so an up-to-date dll costs seconds.
    if (Test-Path -LiteralPath $dll) {
        Write-Log "Bringing the orkeon CLI up to date ($Configuration) …"
        dotnet build $CliProject -c $Configuration --no-restore -v q --nologo | Out-Null
    }
    else {
        Write-Log "Building the orkeon CLI once ($Configuration) …"
        dotnet build $CliProject -c $Configuration -v q --nologo | Out-Null
    }
    if (-not (Test-Path -LiteralPath $dll)) {
        Stop-Run 'could not locate or build the orkeon CLI. Set ORKEON_BIN to point at it.'
    }
    $script:Orkeon = @('dotnet', $dll)
}

$Commit = try { (git -C $RepoRoot rev-parse --short HEAD 2>$null).Trim() } catch { '' }

# A report names the commit of the code that ran. With src/ changed and not committed, that
# commit names code that did not run: the TypeSafe reports of 2026-11-11 said 925c13871 while
# the probe they ran was still uncommitted. The suffix says so (same rule as the bash twin).
if ($Commit) {
    $dirty = $false
    try {
        git -C $RepoRoot diff --quiet HEAD -- src 2>$null
        if ($LASTEXITCODE -ne 0) { $dirty = $true }
        $untracked = git -C $RepoRoot ls-files --others --exclude-standard -- src 2>$null
        if ($untracked) { $dirty = $true }
    } catch { }
    if ($dirty) { $Commit = "$Commit-dirty" }
}

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

    if (-not $Pattern) {
        if ($declared) { return $declared }

        # Neither -Model nor a configuration entry: fall back to the catalogue's own default
        # for this provider. Without it, `-Provider a,b,c` resolved nothing and the whole
        # point of naming several providers at once was lost to a per-provider -Model.
        #
        # These identifiers come from the matrix sections 6.x, not from the providers'
        # compiled-in defaults — six of those are flagged retired or wrong (G-01..G-04, G-07,
        # G-08), so inheriting them would hand every second campaign a model its own API no
        # longer serves. Azure declares none on purpose: deployments are account-specific.
        $fallback = Get-CatalogField $ProviderKey 'defaultModel'
        if ($fallback) { return @($fallback) }
        return @()
    }
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
# Providers the preflight stopped: counted in Failures for the exit status, named apart in the
# closing line — "a failed mode" would send the reader to reports that were never written.
$script:Skipped = 0
$script:ServedDiff = @()
$TmpDir = Join-Path ([System.IO.Path]::GetTempPath()) ("orkeon-campaign-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $TmpDir | Out-Null

function Invoke-Campaign {
    param(
        [string]$ProviderKey, [string]$ModelId, [string]$ModeList,
        [string]$KeyEnv, [string]$Base, [string]$Version, [string]$Workspace
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

    $arguments = Get-ProbeArguments $ProviderKey $ModelId $ModeList $KeyEnv $Base $Version $Workspace

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

# The arguments of one `orkeon llm probe` call: shared by the campaign run and the preflight,
# so the check calls exactly what the campaign will.
function Get-ProbeArguments {
    param(
        [string]$ProviderKey, [string]$ModelId, [string]$ModeList,
        [string]$KeyEnv, [string]$Base, [string]$Version, [string]$Workspace
    )

    $arguments = @('llm', 'probe', '-p', $ProviderKey, '-m', $ModelId,
                   '--modes', $ModeList, '--format', 'json', '-k', $KeyEnv)
    if ($Base)    { $arguments += @('-u', $Base) }
    if ($Version) { $arguments += @('--api-version', $Version) }
    if ($Workspace) { $arguments += @('--workspace-id', $Workspace) }
    if ($Commit)  { $arguments += @('--commit', $Commit) }
    if ($null -ne $Timeout)     { $arguments += @('--timeout', $Timeout.ToString([cultureinfo]::InvariantCulture)) }

    # Temperature 0 is the default because a pinned temperature makes a verdict reproducible.
    # Some models refuse it outright — `kimi-k2.6` answers every call with
    # `invalid temperature: only 1 is allowed for this model`, which cost a whole campaign
    # (2026-08-03: ten of twelve modes red, one cause). A provider that cannot take 0 declares
    # what it can take in the catalogue, and the report prints the value actually used.
    $effectiveTemperature = $Temperature
    if ($null -eq $effectiveTemperature) {
        $catalogTemperature = Get-CatalogModelParam -ProviderKey $ProviderKey -ModelId $ModelId -Field 'temperature'
        if ($catalogTemperature) { $effectiveTemperature = [double]$catalogTemperature }
    }
    # InvariantCulture matters: a French locale renders 0.5 as "0,5", which the parser rejects.
    if ($null -ne $effectiveTemperature) {
        $arguments += @('--temperature', $effectiveTemperature.ToString([cultureinfo]::InvariantCulture))
    }

    # Same shape for the base thinking effort: gpt-5.6-sol refuses function tools on
    # chat/completions unless reasoning is explicitly off (2026-08-30). Catalogue-declared,
    # printed by the report header; M7 keeps probing thinking with its own explicit effort.
    $thinkingEffort = if ($ThinkingEffort) { $ThinkingEffort } else { Get-CatalogModelParam -ProviderKey $ProviderKey -ModelId $ModelId -Field 'thinkingEffort' }
    if ($thinkingEffort) { $arguments += @('--thinking-effort', $thinkingEffort) }

    # M7's own effort, where a model's supported set excludes the default "low"
    # (mistral-medium-2604: only high or none, 2026-08-30).
    $m7Effort = Get-CatalogModelParam -ProviderKey $ProviderKey -ModelId $ModelId -Field 'm7Effort'
    if ($m7Effort) { $arguments += @('--m7-effort', $m7Effort) }

    # M16's own sweep, where a model takes vendor values beyond low..max (gpt-5.6-sol: none).
    $sweepEfforts = Get-CatalogModelParam -ProviderKey $ProviderKey -ModelId $ModelId -Field 'sweepEfforts'
    if ($sweepEfforts) { $arguments += @('--sweep-efforts', $sweepEfforts) }
    return ,$arguments
}

# An account-level refusal: the key, the balance or the quota, never the model or the mode.
# Matched on the detail the probe archives, which carries the vendor's own body. Each
# alternative was measured: MiniMax `PaymentRequired … insufficient_balance_error (2067)`
# (2026-08-30), Z.AI `1113 Insufficient balance or no resource package` (2026-11-11), Kimi
# `exceeded_current_quota_error` (2026-10-07). Same pattern as the bash twin.
$AccountError = 'Unauthorized|Forbidden|PaymentRequired|insufficient[ _]balance|insufficient_quota|exceeded_current_quota|"1113"|invalid[ _]api[ _]key|invalid_authentication|refused the key|HTTP 40[123][^0-9]'

# One call of the provider's first mode on its first model. Returns $false — skip the
# provider — when that call shows the account cannot pay or the key is refused, or the probe
# could not start at all. Any other outcome, red included, lets the campaign run: a
# model-level red is a finding the full report must carry. Z.AI on 2026-11-11: an empty
# balance answered 429 and every mode of five models waited out its retries, 21 minutes.
function Test-Preflight {
    param(
        [string]$ProviderKey, [string]$ModelId, [string]$Mode,
        [string]$KeyEnv, [string]$Base, [string]$Version, [string]$Workspace
    )

    $arguments = Get-ProbeArguments $ProviderKey $ModelId $Mode $KeyEnv $Base $Version $Workspace
    $err = Join-Path $TmpDir "$ProviderKey-preflight.stderr"
    Write-Log "▷ $ProviderKey : preflight, $Mode on $ModelId"
    $output = & { Invoke-Orkeon $arguments } 2>$err

    $campaign = $null
    try { $campaign = ($output -join "`n") | ConvertFrom-Json } catch { }
    if ((-not $campaign) -or ($null -eq $campaign.modes)) {
        Write-Warn "$ProviderKey : preflight could not run the probe — provider skipped."
        if (Test-Path -LiteralPath $err) {
            Get-Content -LiteralPath $err | ForEach-Object { Write-Warn "    $_" }
        }
        return $false
    }

    $detail = (@($campaign.modes | Where-Object { $_.outcome -eq 'failed' } | ForEach-Object { $_.detail }) -join ' ')
    if ($detail -and ($detail -match $AccountError)) {
        Write-Warn "$ProviderKey : preflight refused at the account level — provider skipped, no mode run."
        Write-Log ("    " + $detail.Substring(0, [Math]::Min(300, $detail.Length)))
        return $false
    }
    return $true
}

# The provider's live catalogue against lib/served-models.json: what it serves that the
# snapshot does not list, and what the snapshot lists that it no longer serves. Printed at the
# end of the campaign. Free — a catalogue listing, no inference — and silent when the provider
# has no catalogue endpoint or no snapshot entry.
function Compare-ServedModels {
    param([string]$ProviderKey, [string]$KeyEnv, [string]$Base)

    if (-not (Test-Path -LiteralPath $ServedPath)) { return }
    Get-CatalogField $ProviderKey 'label' | Out-Null   # loads $script:Catalog
    $entry = if ($script:Catalog) { $script:Catalog.providers.PSObject.Properties[$ProviderKey] } else { $null }
    $canonicalKey = if ($entry -and $entry.Value.aliasOf) { $entry.Value.aliasOf } else { $ProviderKey }
    $served = Get-Content -Raw -LiteralPath $ServedPath | ConvertFrom-Json
    $snapshot = $served.providers.PSObject.Properties[$canonicalKey]
    if (-not $snapshot -or -not $snapshot.Value.models) { return }

    $arguments = @('llm', 'models', '-p', $ProviderKey, '-k', $KeyEnv)
    if ($Base) { $arguments += @('-u', $Base) }
    $live = @()
    try {
        $live = @(Invoke-Orkeon $arguments 2>$null | Where-Object { $_ -and ($_ -notmatch '\s') } | Sort-Object -Unique -CaseSensitive)
    } catch { $live = @() }
    if ($live.Count -eq 0) { return }
    $known = @($snapshot.Value.models.PSObject.Properties.Name)

    $added = @($live | Where-Object { $known -cnotcontains $_ })
    $removed = @($known | Where-Object { $live -cnotcontains $_ } | Sort-Object -CaseSensitive)
    if ($added.Count -gt 0) { $script:ServedDiff += "${canonicalKey}: new — $($added -join ' ')" }
    if ($removed.Count -gt 0) { $script:ServedDiff += "${canonicalKey}: no longer served — $($removed -join ' ')" }
}

function Invoke-Provider {
    param([string]$ProviderKey)

    # Command line wins over the configuration, which wins over the built-in default — the
    # usual precedence, so an operator can override one provider without editing the file.
    # A decision provider (TypeSafe, Liquid, Kev) has its own modes, D1-D8 (D8 only when named): the catalogue names
    # a provider's default set, so the chat modes are never sent where none of them applies.
    $catalogModes = Get-CatalogField $ProviderKey 'modes'
    $modeList = if ($Modes)     { $Modes }     else { Get-Setting $ProviderKey 'modes' $(if ($catalogModes) { $catalogModes } else { $AllModes }) }
    $keyEnv   = if ($ApiKeyEnv) { $ApiKeyEnv } else { Get-Setting $ProviderKey 'apiKeyEnv' (Get-CatalogKeyEnv $ProviderKey) }
    if (-not $keyEnv) { $keyEnv = 'ORKEON_LLM_API_KEY' }
    $base     = if ($BaseUrl)   { $BaseUrl }   else { Get-Setting $ProviderKey 'baseUrl' }
    $version  = if ($ApiVersion){ $ApiVersion} else { Get-Setting $ProviderKey 'apiVersion' }
    $workspace = if ($WorkspaceId) { $WorkspaceId } else { Get-Setting $ProviderKey 'workspaceId' }
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

        # One call before the many: an account that cannot pay fails here once, not per mode.
        # Skipped on a dry run (it would place a call) and with -NoPreflight.
        # The cheapest mode of the family, not the first one asked: -Modes D8 must not pay its
        # 100k tokens twice.
        $firstMode = if ($modeList -like 'D*') { 'D1' } else { 'M1' }
        if ((-not $NoPreflight) -and (-not $DryRun)) {
            if (-not (Test-Preflight $ProviderKey $models[0] $firstMode $keyEnv $base $version $workspace)) {
                $script:Failures++
                $script:Skipped++
                return
            }
        }
        elseif (-not $NoPreflight) {
            Write-Output ('  would check {0,-11} {1} on {2} first' -f $ProviderKey, $firstMode, $models[0])
        }

        foreach ($m in $models) { Invoke-Campaign $ProviderKey $m $modeList $keyEnv $base $version $workspace }

        # A vision companion run, when the provider's default model cannot see and it declares
        # one that can. This is the practical half of D-03: capabilities are declared per
        # provider, reality is per model, so a single default model can never answer M9 for a
        # provider whose sight lives in a different identifier. Measured twice on 2026-08-02 —
        # `glm-5.2` returns code 1210 while `glm-4.6v-flash` reads the image, and `llama3.2`
        # refuses multimodal while `llava` reads it.
        #
        # The default model still runs M9 and still scores its red: that red *is* the D-03
        # evidence and deleting it would hide the mismatch the matrix exists to track. The
        # companion adds the complementary fact — that Orkeon's multimodal path works.
        #
        # Only on the automatic path. An explicit -Model is a choice, and second-guessing it
        # would spend credits the caller did not ask to spend.
        if ((-not $Model) -and (",$modeList," -like '*,M9,*')) {
            $vision = Get-CatalogField $ProviderKey 'visionModel'
            if ($vision -and ($models -notcontains $vision)) {
                Write-Log "  ↳ $ProviderKey declares a vision model — running M9 on $vision as well"
                Invoke-Campaign $ProviderKey $vision 'M9' $keyEnv $base $version $workspace
            }
        }

        # The catalogue's candidates — the successors the review of 2026-09-19 recorded for
        # the default — run after it, all modes, only when asked: a campaign is what promotes
        # a candidate to default, and it must be one the caller chose to pay for. Automatic
        # path only, like the vision companion.
        if ($Candidates -and (-not $Model)) {
            foreach ($candidate in (Get-CatalogCandidates $ProviderKey)) {
                if ($models -contains $candidate) { continue }
                Write-Log "  ↳ $ProviderKey : candidate $candidate (-Candidates)"
                Invoke-Campaign $ProviderKey $candidate $modeList $keyEnv $base $version $workspace
            }
        }

        if (-not $DryRun) { Compare-ServedModels $ProviderKey $keyEnv $base }
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
        @($campaignConfig.providers.PSObject.Properties.Name | Sort-Object)
    } else {
        # `-Provider a,b,c`. Splitting here rather than asking the caller for one run per
        # provider keeps the campaign a single unit of work: one index rebuild, one exit
        # status, one place that knows what was attempted.
        @($Provider -split ',' | ForEach-Object { $_.Trim() })
    }

    foreach ($p in $providers) {
        if (-not $p) { Stop-Run '-Provider has an empty entry: check for a stray comma' }
    }

    # A dry run resolves catalogues and prints a plan; running that concurrently would only
    # scramble the plan's order for no gain, since nothing is being waited on.
    if ($Parallel -and (-not $DryRun) -and $providers.Count -gt 1) {
        # Build the CLI here, before forking. Left to the children, three of them would race
        # to produce the same assembly and collide in obj/ — and that failure would surface
        # as a provider error, sending the reader after the wrong thing entirely.
        Resolve-Orkeon

        # Children rather than runspaces: Invoke-Provider leans on a dozen script-scope
        # functions and variables that ForEach-Object -Parallel would not carry across, and
        # rehydrating them by hand would be a second implementation to keep in step. A child
        # is the same script on the same arguments, which is exactly what we want to run.
        $forwarded = @()
        if ($Model)         { $forwarded += @('-Model', $Model) }
        if ($Modes)         { $forwarded += @('-Modes', $Modes) }
        if ($Config)        { $forwarded += @('-Config', $Config) }
        if ($BaseUrl)       { $forwarded += @('-BaseUrl', $BaseUrl) }
        if ($ApiVersion)    { $forwarded += @('-ApiVersion', $ApiVersion) }
        if ($WorkspaceId)   { $forwarded += @('-WorkspaceId', $WorkspaceId) }
        if ($ApiKeyEnv)     { $forwarded += @('-ApiKeyEnv', $ApiKeyEnv) }
        if ($MaxModels)     { $forwarded += @('-MaxModels', "$MaxModels") }
        if ($Candidates)    { $forwarded += '-Candidates' }
        if ($NoPreflight)   { $forwarded += '-NoPreflight' }
        if ($ThinkingEffort) { $forwarded += @('-ThinkingEffort', $ThinkingEffort) }
        if ($null -ne $Timeout)     { $forwarded += @('-Timeout', "$Timeout") }
        if ($null -ne $Temperature) { $forwarded += @('-Temperature', "$Temperature") }
        $forwarded += @('-Out', $Out, '-Configuration', $Configuration, '-NoRecap')

        # The host that runs this script, to run its children. PowerShell installed as a dotnet
        # tool runs under `dotnet` itself: the process path is then dotnet, and the children
        # need pwsh.dll in front of their arguments.
        $hostPath = (Get-Process -Id $PID).Path
        $hostPrefix = @()
        if ([System.IO.Path]::GetFileNameWithoutExtension($hostPath) -eq 'dotnet') {
            $hostPrefix = @(Join-Path $PSHOME 'pwsh.dll')
        }

        $children = @()
        for ($i = 0; $i -lt $providers.Count; $i++) {
            # Indexed, not named: the same provider may legitimately appear twice with
            # different models, and two children writing one buffer would interleave.
            $child = [pscustomobject]@{
                Provider = $providers[$i]
                Out      = Join-Path $TmpDir "par-$i.out"
                Err      = Join-Path $TmpDir "par-$i.err"
                Tally    = Join-Path $TmpDir "par-$i.tally"
                Process  = $null
            }
            $child.Process = Start-Process -FilePath $hostPath `
                -ArgumentList ($hostPrefix + @('-NoProfile', '-File', $PSCommandPath,
                                 '-Provider', $child.Provider,
                                 '-TallyFile', $child.Tally) + $forwarded) `
                -NoNewWindow -PassThru `
                -RedirectStandardOutput $child.Out -RedirectStandardError $child.Err
            $children += $child
            Write-Log "▶ $($child.Provider) — started"
        }

        Write-Log ''
        $children | ForEach-Object { $_.Process.WaitForExit() }

        # Replayed in the order the caller named them, so a parallel run reads like a
        # sequential one. Interleaving live would be honest about the timing and useless
        # as a report.
        foreach ($child in $children) {
            foreach ($stream in @($child.Out, $child.Err)) {
                if ((Test-Path -LiteralPath $stream) -and (Get-Item -LiteralPath $stream).Length -gt 0) {
                    Get-Content -LiteralPath $stream | ForEach-Object { Write-Log $_ }
                }
            }

            if (Test-Path -LiteralPath $child.Tally) {
                $counts = (Get-Content -Raw -LiteralPath $child.Tally).Trim() -split '\s+'
                $script:Ran      += [int]$counts[0]
                $script:Failures += [int]$counts[1]
                if ($counts.Count -gt 2) { $script:Skipped += [int]$counts[2] }
                $servedFile = "$($child.Tally).served"
                if (Test-Path -LiteralPath $servedFile) { $script:ServedDiff += @(Get-Content -LiteralPath $servedFile) }
            }
            else {
                # No tally means the child died before it could write one — a crash, not a
                # failed mode. Counting it is what keeps the exit status honest.
                Write-Warn "$($child.Provider): the campaign process ended without reporting a result."
                $script:Failures++
            }
        }
    }
    else {
        foreach ($p in $providers) { Invoke-Provider $p }
    }

    if ($DryRun) {
        Write-Log ''
        Write-Log 'Nothing was called and nothing was written.'
        exit 0
    }

    # A child hands its catalogue changes to the parent, which prints them once at the end.
    if ($TallyFile -and $script:ServedDiff.Count -gt 0) {
        Set-Content -LiteralPath "$TallyFile.served" -Value $script:ServedDiff
    }
    elseif ($script:ServedDiff.Count -gt 0) {
        $readOn = (Get-Content -Raw -LiteralPath $ServedPath | ConvertFrom-Json).readOn
        Write-Log ''
        Write-Log "Catalogue changes since lib/served-models.json was read ($readOn):"
        $script:ServedDiff | ForEach-Object { Write-Log "  $_" }
        Write-Log 'Re-read the snapshot when one of them matters: a new id is a candidate, a withdrawn one may be a default.'
    }

    # A child of -Parallel reports its counts and stops here. Letting it print the closing
    # summary too would have the parent replay "index NOT rebuilt" once per provider, which
    # the bash twin never does — there the unit of parallelism is a subshell around one
    # function, not a whole run.
    if ($TallyFile) {
        Set-Content -LiteralPath $TallyFile -Value "$script:Ran $script:Failures $script:Skipped" -NoNewline
        if ($script:Failures -gt 0) { exit 1 }
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

    if ($script:Skipped -gt 0) {
        Write-Log "$script:Skipped provider(s) skipped by the preflight: fix the key or the balance named above, then rerun them."
    }
    if ($script:Failures -gt $script:Skipped) {
        Write-Log "$($script:Failures - $script:Skipped) campaign(s) reported a failed mode. That is the point of the exercise; read the reports."
    }
    if ($script:Failures -gt 0) { exit 1 }
}
finally {
    Remove-Item -Recurse -Force -LiteralPath $TmpDir -ErrorAction SilentlyContinue
}
