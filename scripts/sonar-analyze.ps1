#Requires -Version 7.0
<#
.SYNOPSIS
    SonarQube Analysis Script for Orkeon

.DESCRIPTION
    Performs a full SonarQube analysis with code coverage, enforces the
    (blocking) Quality Gate and generates a Markdown report of the results.

    Quality Gate (chantier R5.4 - BLOCKING): the script provisions an
    idempotent "Orkeon Transitional" Quality Gate with transitional thresholds
    (see $QualityGateConditions below and docs/guides/quality-gate.md for the
    hardening trajectory), binds it to the project, runs the analysis with
    sonar.qualitygate.wait=true, and EXITS WITH A NON-ZERO CODE when the gate
    is FAILED.

.PARAMETER SonarToken
    Authentication token for SonarQube. Can also be set via SONAR_TOKEN env var.

.NOTES
    Environment variables:
      SONAR_TOKEN        (required) Authentication token for SonarQube
      SONAR_HOST_URL     (optional) Server URL (default: http://localhost:9000)
      SONAR_PROJECT_KEY  (optional) Project key (default: Orkeon - renamed
                         from the historical CrewAI.NET key on 2026-08-17,
                         PUB-01 follow-up: last public trace of the old name)
      SONAR_NO_DOCKER    (optional) Set to 1 to forbid the Docker Compose
                         fallback when the server is unreachable (implied when
                         CI=true, e.g. on GitHub Actions)

.EXAMPLE
    $env:SONAR_TOKEN = "<your-token>"
    .\scripts\sonar-analyze.ps1
#>

$ErrorActionPreference = "Stop"

# ── Configuration ─────────────────────────────────────────────────────────────
$SonarHost      = $env:SONAR_HOST_URL ?? "http://localhost:9000"
# Project key renamed "CrewAI.NET" -> "Orkeon" (2026-08-17, PUB-01 follow-up).
# This supersedes QCM 2026-06-11 and resets the server-side analysis history;
# set SONAR_PROJECT_KEY=CrewAI.NET to keep browsing the old project.
$ProjectKey     = $env:SONAR_PROJECT_KEY ?? "Orkeon"
$SonarToken     = $env:SONAR_TOKEN
if (-not $SonarToken) { throw "Variable SONAR_TOKEN requise. Set it before running this script." }

$SolutionPath   = "Orkeon.sln"
$CoverageDir    = "./coverage"
$ReportDir      = "./sonarqube"
$ScriptDir      = $PSScriptRoot
$ProjectRoot    = Split-Path $ScriptDir -Parent
$SonarPropsFile = Join-Path $ProjectRoot "sonar-project.properties"
$SonarPropsBackup = $null
$SonarQubeWaitTimeout = 300  # 5 minutes
$CeTaskWaitTimeout    = 300  # 5 minutes

# ── Quality Gate (R5.4 — « assouplir puis durcir », QCM 2026-06-11) ───────────
# The gate is BLOCKING: the analysis runs with sonar.qualitygate.wait=true and
# the script exits non-zero when the gate is FAILED.
# Single source of truth for the thresholds: the table below, mirrored in
# scripts/sonar-analyze.sh (QUALITY_GATE_CONDITIONS) — keep both in sync.
# Hardening trajectory and exit criteria: docs/guides/quality-gate.md.
# All conditions apply to NEW code; Op is the comparator that triggers ERROR.
$QualityGateName    = "Orkeon Transitional"
$QualityGateTimeout = 600  # seconds the scanner 'end' step waits for the gate verdict
$QualityGateConditions = @(
    # transitional 70 (final 80) — new code at 71.3% on 2026-05-31; aligned with the 70% CI coverage gate (R5.2); raised to 75 then 80 after R5.5/R5.6
    @{ Metric = "new_coverage";                   Op = "LT"; Error = "70" }
    # transitional 2 = B (final 1 = A) — B tolerates MINOR bugs while the 3 known MAJOR bugs are fixed; stays red until then (intended pressure)
    @{ Metric = "new_reliability_rating";         Op = "GT"; Error = "2" }
    # already met — kept strict (A)
    @{ Metric = "new_security_rating";            Op = "GT"; Error = "1" }
    # already met — kept strict (A)
    @{ Metric = "new_maintainability_rating";     Op = "GT"; Error = "1" }
    # already met (0.56%) — kept (3%)
    @{ Metric = "new_duplicated_lines_density";   Op = "GT"; Error = "3" }
    # transitional 0 = neutralised, stays visible (final 100) — until the 9 inherited hotspots are reviewed (security remediation, fiche 07)
    @{ Metric = "new_security_hotspots_reviewed"; Op = "LT"; Error = "0" }
)
$script:ScannerEndExitCode = 0
$script:FinalExitCode      = 0
$script:QualityGateStatus  = "UNKNOWN"

# ── Logging utilities ─────────────────────────────────────────────────────────
function Log-Info    { param([string]$Msg) Write-Host "[INFO]  $Msg" -ForegroundColor Blue }
function Log-Success { param([string]$Msg) Write-Host "[OK]    $Msg" -ForegroundColor Green }
function Log-Warn    { param([string]$Msg) Write-Host "[WARN]  $Msg" -ForegroundColor Yellow }
function Log-Error   { param([string]$Msg) Write-Host "[ERROR] $Msg" -ForegroundColor Red }

# ── API helper ────────────────────────────────────────────────────────────────
function Invoke-SonarApi {
    param([string]$Endpoint)
    $headers = @{ Authorization = "Basic " + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${SonarToken}:")) }
    try {
        return Invoke-RestMethod -Uri "$SonarHost$Endpoint" -Headers $headers -TimeoutSec 30
    } catch {
        return $null
    }
}

# POST helper for Quality Gate provisioning. Returns a hashtable:
#   Ok (bool), Data (response or $null), StatusCode (int), Message (string)
function Invoke-SonarApiPost {
    param([string]$Endpoint, [hashtable]$Body)
    $headers = @{ Authorization = "Basic " + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${SonarToken}:")) }
    try {
        $data = Invoke-RestMethod -Method Post -Uri "$SonarHost$Endpoint" -Headers $headers -Body $Body -TimeoutSec 30
        return @{ Ok = $true; Data = $data; StatusCode = 200; Message = "" }
    } catch {
        $statusCode = 0
        if ($_.Exception.Response) { $statusCode = [int]$_.Exception.Response.StatusCode }
        return @{ Ok = $false; Data = $null; StatusCode = $statusCode; Message = $_.Exception.Message }
    }
}

# ── Prerequisites check ──────────────────────────────────────────────────────
function Test-Prerequisites {
    Log-Info "Checking prerequisites..."

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Log-Error "'dotnet' CLI not found. Install .NET SDK first."
        exit 1
    }

    # Ensure ~/.dotnet/tools is in PATH
    $dotnetToolsPath = Join-Path $env:HOME ".dotnet/tools"
    if ((Test-Path $dotnetToolsPath) -and ($env:PATH -notlike "*$dotnetToolsPath*")) {
        $env:PATH = "$env:PATH$([IO.Path]::PathSeparator)$dotnetToolsPath"
    }

    if (-not (Get-Command dotnet-sonarscanner -ErrorAction SilentlyContinue)) {
        $tools = dotnet tool list -g 2>$null
        if ($tools -match "dotnet-sonarscanner") {
            Log-Warn "'dotnet-sonarscanner' installed but not in PATH. Check ~/.dotnet/tools/"
        } else {
            Log-Warn "'dotnet-sonarscanner' not found globally. Installing..."
            dotnet tool install --global dotnet-sonarscanner
        }
    }

    Log-Success "Prerequisites OK"
}

# ── Handle sonar-project.properties (Bug #2) ─────────────────────────────────
function Set-SonarPropertiesAside {
    if (Test-Path $SonarPropsFile) {
        $script:SonarPropsBackup = "$SonarPropsFile.bak"
        Log-Warn "Found sonar-project.properties - renaming to .bak to avoid conflict with CLI parameters"
        Move-Item $SonarPropsFile $script:SonarPropsBackup -Force
    }
}

function Restore-SonarProperties {
    if ($script:SonarPropsBackup -and (Test-Path $script:SonarPropsBackup)) {
        Move-Item $script:SonarPropsBackup $SonarPropsFile -Force
        Log-Info "Restored sonar-project.properties"
    }
}

# ── Wait for SonarQube to be ready ───────────────────────────────────────────
function Wait-ForSonarQube {
    Log-Info "Waiting for SonarQube to be ready at $SonarHost ..."
    $elapsed = 0
    $interval = 5

    while ($elapsed -lt $SonarQubeWaitTimeout) {
        try {
            $response = Invoke-RestMethod -Uri "$SonarHost/api/system/status" -TimeoutSec 5
            if ($response.status -eq "UP") {
                Log-Success "SonarQube is ready"
                return
            }
        } catch { }

        Write-Host "." -NoNewline
        Start-Sleep -Seconds $interval
        $elapsed += $interval
    }

    Write-Host ""
    Log-Error "SonarQube did not become ready within ${SonarQubeWaitTimeout}s"
    exit 1
}

# ── Start SonarQube if not running ───────────────────────────────────────────
function Ensure-SonarQubeRunning {
    try {
        $response = Invoke-RestMethod -Uri "$SonarHost/api/system/status" -TimeoutSec 5
        if ($response.status -eq "UP") {
            Log-Success "SonarQube is already running"
            return
        }
    } catch { }

    # In CI (or when explicitly disabled) never try to boot a local SonarQube:
    # an ephemeral server would have an empty history and a meaningless gate.
    if ($env:CI -eq "true" -or $env:SONAR_NO_DOCKER -eq "1") {
        Log-Error "SonarQube is not reachable at $SonarHost and the Docker fallback is disabled (CI/SONAR_NO_DOCKER)."
        Log-Error "Point SONAR_HOST_URL to a reachable SonarQube instance."
        exit 1
    }

    Log-Info "SonarQube not reachable. Attempting to start via Docker Compose..."

    $composeFile = Join-Path $ProjectRoot "docker-compose.sonarqube.yml"
    if (-not (Test-Path $composeFile)) {
        Log-Error "docker-compose.sonarqube.yml not found at $composeFile"
        Log-Error "Start SonarQube manually or set SONAR_HOST_URL to an existing instance."
        exit 1
    }

    try {
        $output = docker compose -f $composeFile up -d 2>&1
        if ($LASTEXITCODE -ne 0) {
            if ($output -match "whiteout") {
                Log-Error "Docker pull failed due to overlayfs whiteout file issue (common in WSL2/DinD)."
                Log-Error "Workaround: Switch Docker storage driver to vfs:"
                Log-Error "  1. Edit /etc/docker/daemon.json and add: {`"storage-driver`": `"vfs`"}"
                Log-Error "  2. Restart Docker: sudo systemctl restart docker"
                Log-Error "  3. Re-run this script"
                exit 1
            }
            Log-Error "Failed to start SonarQube via Docker Compose:"
            Log-Error $output
            exit 1
        }
    } catch {
        Log-Error "Failed to start SonarQube: $_"
        exit 1
    }

    Wait-ForSonarQube
}

# ── Wait for Compute Engine task to finish ───────────────────────────────────
function Wait-ForCeTask {
    Log-Info "Waiting for SonarQube background task to complete..."
    $elapsed = 0
    $interval = 5

    while ($elapsed -lt $CeTaskWaitTimeout) {
        $response = Invoke-SonarApi "/api/ce/component?component=$ProjectKey"

        if ($response) {
            $status = $null
            if ($response.current) {
                $status = $response.current.status
            } elseif ($response.queue -and $response.queue.Count -gt 0) {
                $status = $response.queue[0].status
            }

            switch ($status) {
                "SUCCESS" {
                    Log-Success "Background analysis completed successfully"
                    return
                }
                "FAILED" {
                    Log-Error "Background analysis task FAILED"
                    return
                }
                "CANCELED" {
                    Log-Error "Background analysis task CANCELED"
                    return
                }
            }
        }

        Write-Host "." -NoNewline
        Start-Sleep -Seconds $interval
        $elapsed += $interval
    }

    Write-Host ""
    Log-Warn "CE task did not complete within ${CeTaskWaitTimeout}s - report may be incomplete"
}

# ── Quality Gate provisioning (R5.4 — « assouplir puis durcir ») ──────────────
# Provisions the transitional Quality Gate decided by the maintainer (QCM
# 2026-06-11) and binds it to the project. Idempotent: existing conditions are
# updated only when their threshold differs from $QualityGateConditions.
function Initialize-QualityGate {
    Log-Info "Provisioning Quality Gate '$QualityGateName' (transitional thresholds - R5.4)..."

    # 1. Create the gate if it does not exist yet (idempotent).
    $encodedName = [uri]::EscapeDataString($QualityGateName)
    $gate = Invoke-SonarApi "/api/qualitygates/show?name=$encodedName"
    if (-not $gate -or -not $gate.name) {
        $created = Invoke-SonarApiPost "/api/qualitygates/create" @{ name = $QualityGateName }
        if (-not $created.Ok) {
            Log-Warn "Could not create Quality Gate '$QualityGateName' (HTTP $($created.StatusCode))."
            Log-Warn "The token needs the 'Administer Quality Gates' permission."
            Log-Warn "Continuing - the gate currently assigned to the project will be enforced instead."
            return
        }
        Log-Success "Quality Gate '$QualityGateName' created"
        $gate = Invoke-SonarApi "/api/qualitygates/show?name=$encodedName"
    }

    # 2. Create or update each condition.
    foreach ($cond in $QualityGateConditions) {
        $existing = @($gate.conditions | Where-Object { $_.metric -eq $cond.Metric }) | Select-Object -First 1
        if ($existing) {
            if ("$($existing.error)" -eq $cond.Error) { continue }  # already provisioned
            $updated = Invoke-SonarApiPost "/api/qualitygates/update_condition" @{
                id = $existing.id; metric = $cond.Metric; op = $cond.Op; error = $cond.Error
            }
            if ($updated.Ok) { Log-Info "  Condition updated: $($cond.Metric) $($cond.Op) $($cond.Error)" }
            else { Log-Warn "  Could not update condition '$($cond.Metric)' (HTTP $($updated.StatusCode))" }
        } else {
            $createdCond = Invoke-SonarApiPost "/api/qualitygates/create_condition" @{
                gateName = $QualityGateName; metric = $cond.Metric; op = $cond.Op; error = $cond.Error
            }
            if ($createdCond.Ok) { Log-Info "  Condition created: $($cond.Metric) $($cond.Op) $($cond.Error)" }
            else { Log-Warn "  Could not create condition '$($cond.Metric)' (HTTP $($createdCond.StatusCode))" }
        }
    }

    # 3. Bind the gate to the project. On a brand-new server the project does
    #    not exist before its first analysis - provision it, then retry.
    $selected = Invoke-SonarApiPost "/api/qualitygates/select" @{ gateName = $QualityGateName; projectKey = $ProjectKey }
    if (-not $selected.Ok -and $selected.StatusCode -eq 404) {
        Log-Info "Project '$ProjectKey' not found on the server - provisioning it..."
        $null = Invoke-SonarApiPost "/api/projects/create" @{ project = $ProjectKey; name = $ProjectKey }
        $selected = Invoke-SonarApiPost "/api/qualitygates/select" @{ gateName = $QualityGateName; projectKey = $ProjectKey }
    }
    if ($selected.Ok) {
        Log-Success "Quality Gate '$QualityGateName' assigned to project '$ProjectKey'"
    } else {
        Log-Warn "Could not assign the gate to project '$ProjectKey' (HTTP $($selected.StatusCode)) - the currently assigned gate will be enforced."
    }
}

# ── Enforce the Quality Gate verdict (R5.4 — blocking gate) ───────────────────
# Reads the gate status from the API (robust complement to
# sonar.qualitygate.wait) and prints every failed condition so the verdict is
# visible in the logs. Returns $false when the gate is in ERROR (or any non-OK
# computed status).
function Test-QualityGate {
    Log-Info "Fetching Quality Gate verdict for '$ProjectKey'..."
    $response = Invoke-SonarApi "/api/qualitygates/project_status?projectKey=$ProjectKey"

    if (-not $response -or -not $response.projectStatus) {
        $script:QualityGateStatus = "UNKNOWN"
        Log-Warn "Quality Gate status unavailable (API unreachable?) - not failing on an unknown verdict"
        return $true
    }

    $script:QualityGateStatus = $response.projectStatus.status
    foreach ($cond in @($response.projectStatus.conditions | Where-Object { $_.status -ne "OK" })) {
        Log-Error "  Condition failed: $($cond.metricKey) = $($cond.actualValue) (threshold $($cond.errorThreshold)) [$($cond.status)]"
    }

    switch ($script:QualityGateStatus) {
        "OK"   { Log-Success "Quality Gate: OK"; return $true }
        "NONE" { Log-Warn "Quality Gate: NONE (no gate computed)"; return $true }
        default {
            Log-Error "Quality Gate: $($script:QualityGateStatus) - gate '$QualityGateName' is blocking (see failed conditions above)"
            return $false
        }
    }
}

# ── Fetch all issues with pagination ─────────────────────────────────────────
function Get-AllIssues {
    param([string]$Query)
    $allIssues = @()
    $page = 1
    $pageSize = 500

    do {
        $response = Invoke-SonarApi "/api/issues/search?${Query}&ps=${pageSize}&p=${page}"
        if (-not $response -or -not $response.issues) { break }
        $allIssues += $response.issues
        $total = $response.paging.total
        $page++
    } while (($page - 1) * $pageSize -lt $total)

    return $allIssues
}

# ── Generate Markdown report ─────────────────────────────────────────────────
function New-MarkdownReport {
    Log-Info "Generating Markdown report..."

    New-Item -Path $ReportDir -ItemType Directory -Force | Out-Null
    $reportDate = Get-Date -Format "yyyy-MM-dd"
    $reportFile = Join-Path $ReportDir "sonarqube-report-${reportDate}.md"

    $srcProjects = @("src/core/Orkeon.Domain", "src/core/Orkeon.Application", "src/core/Orkeon.Infrastructure", "src/plugins/Orkeon.Plugins", "src/apps/Orkeon.ConsoleApp")
    $testProjects = @("tests/core/Orkeon.Domain.Tests", "tests/core/Orkeon.Application.Tests", "tests/core/Orkeon.Infrastructure.Tests", "tests/plugins/Orkeon.Plugins.Tests", "tests/tools/Orkeon.Tools.Abstractions.Tests", "tests/tools/Orkeon.Tools.Code.Tests", "tests/tools/Orkeon.Tools.Data.Tests", "tests/tools/Orkeon.Tools.FileSystem.Tests", "tests/tools/Orkeon.Tools.Web.Tests")

    # ── Fetch data ──
    Log-Info "  Fetching Quality Gate..."
    $qgResponse = Invoke-SonarApi "/api/qualitygates/project_status?projectKey=$ProjectKey"

    Log-Info "  Fetching global metrics..."
    $measuresResponse = Invoke-SonarApi "/api/measures/component?component=$ProjectKey&metricKeys=bugs,vulnerabilities,code_smells,coverage,duplicated_lines_density,ncloc,sqale_index,sqale_debt_ratio,reliability_rating,security_rating,sqale_rating,security_hotspots,cognitive_complexity"

    Log-Info "  Fetching issues facets..."
    $issuesFacets = Invoke-SonarApi "/api/issues/search?componentKeys=$ProjectKey&ps=1&facets=severities,types&resolved=false"

    Log-Info "  Fetching all issues (paginated)..."
    $allIssues = Get-AllIssues -Query "componentKeys=$ProjectKey&resolved=false"
    Log-Info "  Fetched $($allIssues.Count) issues"

    Log-Info "  Fetching security hotspots..."
    $hotspots = Invoke-SonarApi "/api/hotspots/search?projectKey=$ProjectKey&ps=500"

    Log-Info "  Fetching per-project metrics..."
    $projectMetrics = @{}
    foreach ($proj in $srcProjects) {
        $projectMetrics[$proj] = Invoke-SonarApi "/api/measures/component?component=${ProjectKey}:${proj}&metricKeys=coverage,ncloc,bugs,vulnerabilities,code_smells,duplicated_lines_density,cognitive_complexity"
    }

    Log-Info "  Fetching per-directory coverage..."
    $dirMetrics = Invoke-SonarApi "/api/measures/component_tree?component=$ProjectKey&metricKeys=coverage,ncloc,bugs,code_smells&qualifier=DIR&ps=500&s=path"

    # ── Helpers ──
    function Get-Metric {
        param([string]$Key, $Response = $measuresResponse)
        $m = $Response.component.measures | Where-Object { $_.metric -eq $Key }
        if ($m) { return $m.value } else { return "-" }
    }

    function Get-Rating {
        param([string]$Value)
        switch ($Value) {
            { $_ -in "1", "1.0" } { return "A" }
            { $_ -in "2", "2.0" } { return "B" }
            { $_ -in "3", "3.0" } { return "C" }
            { $_ -in "4", "4.0" } { return "D" }
            { $_ -in "5", "5.0" } { return "E" }
            default { return $Value }
        }
    }

    function Get-SeverityOrder {
        param([string]$Severity)
        switch ($Severity) { "BLOCKER" { 0 } "CRITICAL" { 1 } "MAJOR" { 2 } "MINOR" { 3 } default { 4 } }
    }

    function Sanitize-Msg {
        param([string]$Msg, [int]$MaxLen = 120)
        $clean = $Msg -replace "\|", "∣" -replace "\n", " "
        if ($clean.Length -gt $MaxLen) { $clean = $clean.Substring(0, $MaxLen) }
        return $clean
    }

    # ── Quality Gate ──
    $qgStatus = if ($qgResponse.projectStatus) { $qgResponse.projectStatus.status } else { "UNKNOWN" }
    $qgIcon = switch ($qgStatus) { "OK" { "✅" } "ERROR" { "❌" } default { "❓" } }

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("# Rapport d'Analyse SonarQube — Orkeon")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("**Date** : ${reportDate}  |  **Dashboard** : [Ouvrir SonarQube](${SonarHost}/dashboard?id=${ProjectKey})")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("---")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("## Quality Gate : ${qgIcon} ${qgStatus}")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Condition | Statut | Valeur | Seuil |")
    [void]$sb.AppendLine("|-----------|--------|--------|-------|")

    if ($qgResponse.projectStatus.conditions) {
        foreach ($cond in $qgResponse.projectStatus.conditions) {
            $condIcon = switch ($cond.status) { "OK" { "✅" } "ERROR" { "❌" } default { "⚠️" } }
            $threshold = if ($cond.errorThreshold) { $cond.errorThreshold } else { "-" }
            [void]$sb.AppendLine("| $($cond.metricKey) | $condIcon $($cond.status) | $($cond.actualValue) | $threshold |")
        }
    } else {
        [void]$sb.AppendLine("| _Aucune condition disponible_ | - | - | - |")
    }

    # ── Global Metrics ──
    [void]$sb.AppendLine(""); [void]$sb.AppendLine("---"); [void]$sb.AppendLine("")
    [void]$sb.AppendLine("## Métriques globales")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Métrique | Valeur |")
    [void]$sb.AppendLine("|----------|--------|")

    if ($measuresResponse) {
        [void]$sb.AppendLine("| Lignes de code | $(Get-Metric 'ncloc') |")
        [void]$sb.AppendLine("| Bugs | $(Get-Metric 'bugs') |")
        [void]$sb.AppendLine("| Vulnérabilités | $(Get-Metric 'vulnerabilities') |")
        [void]$sb.AppendLine("| Code Smells | $(Get-Metric 'code_smells') |")
        [void]$sb.AppendLine("| Couverture | $(Get-Metric 'coverage')% |")
        [void]$sb.AppendLine("| Duplication | $(Get-Metric 'duplicated_lines_density')% |")
        [void]$sb.AppendLine("| Dette technique (min) | $(Get-Metric 'sqale_index') |")
        [void]$sb.AppendLine("| Ratio dette | $(Get-Metric 'sqale_debt_ratio')% |")
        [void]$sb.AppendLine("| Hotspots sécurité | $(Get-Metric 'security_hotspots') |")
        [void]$sb.AppendLine("| Complexité cognitive | $(Get-Metric 'cognitive_complexity') |")
    }

    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("### Ratings")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Catégorie | Rating |")
    [void]$sb.AppendLine("|-----------|--------|")
    if ($measuresResponse) {
        [void]$sb.AppendLine("| Fiabilité | $(Get-Rating (Get-Metric 'reliability_rating')) |")
        [void]$sb.AppendLine("| Sécurité | $(Get-Rating (Get-Metric 'security_rating')) |")
        [void]$sb.AppendLine("| Maintenabilité | $(Get-Rating (Get-Metric 'sqale_rating')) |")
    }

    # ── Coverage par projet ──
    [void]$sb.AppendLine(""); [void]$sb.AppendLine("---"); [void]$sb.AppendLine("")
    [void]$sb.AppendLine("## Couverture par projet")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Projet | Lignes | Couverture | Bugs | Code Smells | Duplication | Complexité |")
    [void]$sb.AppendLine("|--------|--------|------------|------|-------------|-------------|------------|")

    foreach ($proj in $srcProjects) {
        $projName = Split-Path $proj -Leaf
        $pm = $projectMetrics[$proj]
        $ncloc = Get-Metric 'ncloc' $pm
        $cov = Get-Metric 'coverage' $pm
        $bugs = Get-Metric 'bugs' $pm
        $smells = Get-Metric 'code_smells' $pm
        $dup = Get-Metric 'duplicated_lines_density' $pm
        $cog = Get-Metric 'cognitive_complexity' $pm
        [void]$sb.AppendLine("| **${projName}** | ${ncloc} | ${cov}% | ${bugs} | ${smells} | ${dup}% | ${cog} |")
    }

    # ── Coverage par dossier ──
    foreach ($proj in $srcProjects) {
        $projName = Split-Path $proj -Leaf
        $prefix = "$proj/"
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("### ${projName} — Couverture par dossier")
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("| Dossier | Lignes | Couverture | Bugs | Code Smells |")
        [void]$sb.AppendLine("|---------|--------|------------|------|-------------|")

        if ($dirMetrics -and $dirMetrics.components) {
            $dirs = $dirMetrics.components | Where-Object { $_.path -like "${prefix}*" -and ($_.measures | Where-Object { $_.metric -eq "ncloc" }) }
            foreach ($d in ($dirs | Sort-Object -Property path)) {
                $relPath = $d.path.Substring($prefix.Length)
                $dncloc = ($d.measures | Where-Object { $_.metric -eq "ncloc" }).value ?? "-"
                $dcov = ($d.measures | Where-Object { $_.metric -eq "coverage" }).value ?? "-"
                $dbugs = ($d.measures | Where-Object { $_.metric -eq "bugs" }).value ?? "0"
                $dsmells = ($d.measures | Where-Object { $_.metric -eq "code_smells" }).value ?? "0"
                [void]$sb.AppendLine("| ``${relPath}`` | ${dncloc} | ${dcov}% | ${dbugs} | ${dsmells} |")
            }
        }
    }

    # ── Issues summary ──
    [void]$sb.AppendLine(""); [void]$sb.AppendLine("---"); [void]$sb.AppendLine("")
    [void]$sb.AppendLine("## Résumé des issues")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("### Par sévérité")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Sévérité | Nombre |")
    [void]$sb.AppendLine("|----------|--------|")
    $sevFacet = $issuesFacets.facets | Where-Object { $_.property -eq "severities" }
    if ($sevFacet) { foreach ($v in $sevFacet.values) { [void]$sb.AppendLine("| $($v.val) | $($v.count) |") } }

    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("### Par type")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Type | Nombre |")
    [void]$sb.AppendLine("|------|--------|")
    $typeFacet = $issuesFacets.facets | Where-Object { $_.property -eq "types" }
    if ($typeFacet) { foreach ($v in $typeFacet.values) { [void]$sb.AppendLine("| $($v.val) | $($v.count) |") } }

    # Issues par projet
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("### Par projet")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Projet | Bugs | Vulnérabilités | Code Smells | Total |")
    [void]$sb.AppendLine("|--------|------|----------------|-------------|-------|")

    foreach ($proj in ($srcProjects + $testProjects)) {
        $projName = Split-Path $proj -Leaf
        $prefix = "$proj/"
        $projIssues = $allIssues | Where-Object { ($_.component -split ":")[1] -like "${prefix}*" }
        $bugs = ($projIssues | Where-Object { $_.type -eq "BUG" }).Count
        $vulns = ($projIssues | Where-Object { $_.type -eq "VULNERABILITY" }).Count
        $smells = ($projIssues | Where-Object { $_.type -eq "CODE_SMELL" }).Count
        $label = if ($proj -like "tests/*") { "${projName} _(tests)_" } else { "**${projName}**" }
        [void]$sb.AppendLine("| ${label} | ${bugs} | ${vulns} | ${smells} | $($bugs + $vulns + $smells) |")
    }

    # ── All Bugs ──
    $bugList = $allIssues | Where-Object { $_.type -eq "BUG" } | Sort-Object { Get-SeverityOrder $_.severity }
    [void]$sb.AppendLine(""); [void]$sb.AppendLine("---"); [void]$sb.AppendLine("")
    [void]$sb.AppendLine("## Tous les Bugs ($($bugList.Count))")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| # | Sévérité | Fichier | Ligne | Message |")
    [void]$sb.AppendLine("|---|----------|---------|-------|---------|")
    if ($bugList.Count -gt 0) {
        $i = 1
        foreach ($issue in $bugList) {
            $file = ($issue.component -split ":")[-1]
            $line = if ($issue.line) { $issue.line } else { "-" }
            [void]$sb.AppendLine("| $i | $($issue.severity) | ``$file`` | $line | $(Sanitize-Msg $issue.message 150) |")
            $i++
        }
    } else {
        [void]$sb.AppendLine("| - | _Aucun bug_ | - | - | - |")
    }

    # ── All Vulnerabilities ──
    $vulnList = $allIssues | Where-Object { $_.type -eq "VULNERABILITY" } | Sort-Object { Get-SeverityOrder $_.severity }
    [void]$sb.AppendLine(""); [void]$sb.AppendLine("---"); [void]$sb.AppendLine("")
    [void]$sb.AppendLine("## Toutes les Vulnérabilités ($($vulnList.Count))")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| # | Sévérité | Fichier | Ligne | Message |")
    [void]$sb.AppendLine("|---|----------|---------|-------|---------|")
    if ($vulnList.Count -gt 0) {
        $i = 1
        foreach ($issue in $vulnList) {
            $file = ($issue.component -split ":")[-1]
            $line = if ($issue.line) { $issue.line } else { "-" }
            [void]$sb.AppendLine("| $i | $($issue.severity) | ``$file`` | $line | $(Sanitize-Msg $issue.message 150) |")
            $i++
        }
    } else {
        [void]$sb.AppendLine("| - | _Aucune vulnérabilité_ | - | - | - |")
    }

    # ── All Code Smells by project ──
    $smellList = $allIssues | Where-Object { $_.type -eq "CODE_SMELL" }
    [void]$sb.AppendLine(""); [void]$sb.AppendLine("---"); [void]$sb.AppendLine("")
    [void]$sb.AppendLine("## Tous les Code Smells ($($smellList.Count))")

    foreach ($proj in ($srcProjects + $testProjects)) {
        $projName = Split-Path $proj -Leaf
        $prefix = "$proj/"
        $projSmells = $smellList | Where-Object { ($_.component -split ":")[1] -like "${prefix}*" } | Sort-Object { Get-SeverityOrder $_.severity }
        if ($projSmells.Count -eq 0) { continue }

        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("### ${projName} ($($projSmells.Count) code smells)")
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("| # | Sévérité | Fichier | Ligne | Message | Règle |")
        [void]$sb.AppendLine("|---|----------|---------|-------|---------|-------|")

        $i = 1
        foreach ($issue in $projSmells) {
            $fileName = (($issue.component -split ":")[-1] -split "/")[-1]
            $line = if ($issue.line) { $issue.line } else { "-" }
            $rule = if ($issue.rule) { $issue.rule } else { "-" }
            [void]$sb.AppendLine("| $i | $($issue.severity) | ``$fileName`` | $line | $(Sanitize-Msg $issue.message) | $rule |")
            $i++
        }
    }

    # ── Security hotspots ──
    $hotspotCount = if ($hotspots.hotspots) { $hotspots.hotspots.Count } else { 0 }
    [void]$sb.AppendLine(""); [void]$sb.AppendLine("---"); [void]$sb.AppendLine("")
    [void]$sb.AppendLine("## Hotspots de sécurité ($hotspotCount)")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("### Par statut")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Statut | Nombre |")
    [void]$sb.AppendLine("|--------|--------|")

    if ($hotspotCount -gt 0) {
        $grouped = $hotspots.hotspots | Group-Object -Property status
        foreach ($g in $grouped) { [void]$sb.AppendLine("| $($g.Name) | $($g.Count) |") }

        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("### Détail des hotspots")
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("| # | Catégorie | Fichier | Ligne | Message |")
        [void]$sb.AppendLine("|---|-----------|---------|-------|---------|")
        $i = 1
        $sortedHotspots = $hotspots.hotspots | Sort-Object { switch ($_.vulnerabilityProbability) { "HIGH" { 0 } "MEDIUM" { 1 } default { 2 } } }
        foreach ($h in $sortedHotspots) {
            $fileName = (($h.component -split ":")[-1] -split "/")[-1]
            $line = if ($h.line) { $h.line } else { "-" }
            [void]$sb.AppendLine("| $i | $($h.vulnerabilityProbability) | ``$fileName`` | $line | $(Sanitize-Msg $h.message) |")
            $i++
        }
    } else {
        [void]$sb.AppendLine("| _Aucun hotspot_ | 0 |")
    }

    # ── Footer ──
    $timestamp = Get-Date -Format "yyyy-MM-dd à HH:mm:ss"
    [void]$sb.AppendLine(""); [void]$sb.AppendLine("---"); [void]$sb.AppendLine("")
    [void]$sb.AppendLine("_Rapport généré automatiquement par ``scripts/sonar-analyze.ps1`` le ${timestamp}_")
    [void]$sb.AppendLine("_Total : $($allIssues.Count) issues | Dashboard : ${SonarHost}/dashboard?id=${ProjectKey}_")

    $sb.ToString() | Out-File -FilePath $reportFile -Encoding utf8
    Log-Success "Report generated: $reportFile ($($allIssues.Count) issues)"
}

# ══════════════════════════════════════════════════════════════════════════════
# Main flow
# ══════════════════════════════════════════════════════════════════════════════

try {
    Set-Location $ProjectRoot

    Write-Host ""
    Write-Host "══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  SonarQube Analysis — Orkeon" -ForegroundColor Cyan
    Write-Host "══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  Host    : $SonarHost"
    Write-Host "  Project : $ProjectKey"
    Write-Host "══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""

    # Step 1: Check prerequisites
    Test-Prerequisites

    # Step 2: Handle sonar-project.properties (Bug #2)
    Set-SonarPropertiesAside

    # Step 3: Ensure SonarQube is running (Bug #1)
    Ensure-SonarQubeRunning

    # Step 3b: Provision the transitional Quality Gate and bind it to the
    # project (R5.4 - idempotent; trajectory in docs/guides/quality-gate.md)
    Initialize-QualityGate

    # Step 4: Clean previous coverage results
    Log-Info "Cleaning previous coverage results..."
    if (Test-Path $CoverageDir) { Remove-Item $CoverageDir -Recurse -Force }
    New-Item -Path $CoverageDir -ItemType Directory -Force | Out-Null

    # Step 5: SonarScanner begin (Bug #3 — sonar.login, Bug #4 — opencover glob)
    # sonar.qualitygate.wait=true makes the 'end' step wait for the Compute
    # Engine task and fail when the Quality Gate is in ERROR (R5.4 - blocking).
    Log-Info "Starting SonarQube scanner..."
    dotnet sonarscanner begin `
        /k:"$ProjectKey" `
        /d:sonar.host.url="$SonarHost" `
        /d:sonar.login="$SonarToken" `
        /d:sonar.cs.opencover.reportsPaths="**/coverage.opencover.xml" `
        /d:sonar.qualitygate.wait=true `
        /d:sonar.qualitygate.timeout="$QualityGateTimeout" `
        /d:sonar.exclusions="**/bin/**,**/obj/**,examples/**"

    if ($LASTEXITCODE -ne 0) { throw "SonarScanner begin failed" }

    # Step 6: Build
    Log-Info "Building solution..."
    dotnet build $SolutionPath --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }

    # Step 7: Run tests with coverage (Bug #4 — Format=opencover)
    Log-Info "Running tests with code coverage..."
    dotnet test $SolutionPath --configuration Release --no-build `
        --collect:"XPlat Code Coverage" `
        --results-directory $CoverageDir `
        -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover

    if ($LASTEXITCODE -ne 0) {
        Log-Warn "Some tests failed - continuing with analysis"
    }

    # Step 8: SonarScanner end (Bug #3 — sonar.login)
    # With sonar.qualitygate.wait=true this step also fails when the Quality
    # Gate is FAILED. The failure is captured (not fatal yet) so the Markdown
    # report is still generated; the script exits non-zero afterwards (R5.4).
    Log-Info "Finalizing SonarQube analysis..."
    dotnet sonarscanner end /d:sonar.login="$SonarToken"
    if ($LASTEXITCODE -ne 0) {
        $script:ScannerEndExitCode = $LASTEXITCODE
        Log-Warn "sonarscanner end exited with code $LASTEXITCODE (Quality Gate FAILED or analysis error)."
        Log-Warn "The report is still generated below; the script will exit non-zero."
    }

    # Step 9: Wait for background processing
    Wait-ForCeTask

    # Step 10: Generate Markdown report
    New-MarkdownReport

    # Step 11: Enforce the Quality Gate verdict (R5.4 - blocking gate)
    $gateOk = Test-QualityGate

    # Summary
    Write-Host ""
    Write-Host "══════════════════════════════════════════════════════════════" -ForegroundColor Green
    Log-Success "Analysis complete!"
    Write-Host "  Dashboard    : $SonarHost/dashboard?id=$ProjectKey"
    Write-Host "  Quality Gate : $($script:QualityGateStatus)"
    $latestReport = Get-ChildItem "$ReportDir/sonarqube-report-*.md" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latestReport) {
        Write-Host "  Report       : $($latestReport.FullName)"
    }
    Write-Host "══════════════════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host ""

    if (-not $gateOk) {
        Log-Error "Quality Gate is $($script:QualityGateStatus) - failing the build (R5.4: blocking gate, see docs/guides/quality-gate.md)"
        $script:FinalExitCode = 1
    } elseif ($script:ScannerEndExitCode -ne 0) {
        Log-Error "sonarscanner end failed with exit code $($script:ScannerEndExitCode) - propagating failure"
        $script:FinalExitCode = $script:ScannerEndExitCode
    }

} finally {
    Restore-SonarProperties
}

exit $script:FinalExitCode
