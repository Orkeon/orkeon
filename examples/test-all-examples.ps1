# test-all-examples.ps1
# Automated test runner for all 105 Orkeon examples
# Uses Docker Desktop Models (ai/granite-4.0-h-tiny) via localhost:12434
#
# Usage:
#   .\examples\test-all-examples.ps1                    # Test all examples
#   .\examples\test-all-examples.ps1 -Category 01       # Test only category 01
#   .\examples\test-all-examples.ps1 -Example 01-enterprise/01-research-assistant  # Single example
#   .\examples\test-all-examples.ps1 -Level load         # Only test config loading (fast)
#   .\examples\test-all-examples.ps1 -Level run          # Full crew execution (slow)
#   .\examples\test-all-examples.ps1 -TimeoutSeconds 300  # Custom timeout per example
#   .\examples\test-all-examples.ps1 -Settings examples\_shared\appsettings.docker.json

param(
    [string]$Category = "",
    [string]$Example = "",
    [ValidateSet("build", "load", "run")]
    [string]$Level = "load",
    [int]$TimeoutSeconds = 180,
    [string]$Settings = "",
    [switch]$StopOnError,
    [switch]$Verbose
)

$ErrorActionPreference = "Continue"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$ReportDir = Join-Path $ScriptDir "test-reports"
$Timestamp = Get-Date -Format "yyyy-MM-dd_HHmmss"
$ReportPath = Join-Path $ReportDir "test-report-$Timestamp.md"

# ============================================================
# Step 0: Build
# ============================================================
Write-Host "`n=== Building Orkeon.Examples.sln ===" -ForegroundColor Cyan
$buildResult = dotnet build "$ScriptDir\Orkeon.Examples.sln" --configuration Release --verbosity quiet 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED" -ForegroundColor Red
    $buildResult | Write-Host
    exit 1
}
Write-Host "Build OK" -ForegroundColor Green

# Non-trading examples run on the `orkeon` CLI, which lives in the root solution.
$OrkeonCliDll = Join-Path $RepoRoot "src\scripting\Orkeon.Scripting.Cli\bin\Release\net10.0\orkeon.dll"
Write-Host "`n=== Building the orkeon CLI ===" -ForegroundColor Cyan
$cliBuild = dotnet build (Join-Path $RepoRoot "src\scripting\Orkeon.Scripting.Cli\Orkeon.Scripting.Cli.csproj") --configuration Release --verbosity quiet 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "BUILD FAILED (orkeon CLI)" -ForegroundColor Red
    $cliBuild | Write-Host
    exit 1
}

if ($Level -eq "build") {
    Write-Host "`n=== Build-only mode: done ===" -ForegroundColor Green
    exit 0
}

# ============================================================
# Step 1: Discover examples
# ============================================================
$exampleDirs = @()

if ($Example) {
    # Single example mode
    $configPath = Join-Path $ScriptDir "$Example\config.yaml"
    if (-not (Test-Path $configPath)) {
        Write-Host "ERROR: config.yaml not found at $configPath" -ForegroundColor Red
        exit 1
    }
    $exampleDirs += $Example
}
else {
    # Discover all examples (or by category)
    $searchPath = $ScriptDir
    Get-ChildItem -Path $searchPath -Filter "config.yaml" -Recurse |
        Where-Object {
            $_.FullName -notmatch "_legacy" -and
            $_.FullName -notmatch "_shared" -and
            $_.FullName -notmatch "runners" -and
            $_.FullName -notmatch "\.vs" -and
            $_.FullName -notmatch "\.orkeon"
        } |
        ForEach-Object {
            $rel = $_.Directory.FullName.Replace($searchPath, "").TrimStart("\", "/")
            if (-not $Category -or $rel.StartsWith($Category)) {
                $exampleDirs += $rel
            }
        }
    $exampleDirs = $exampleDirs | Sort-Object
}

$total = $exampleDirs.Count
Write-Host "`n=== Testing $total examples (level: $Level, timeout: ${TimeoutSeconds}s) ===`n" -ForegroundColor Cyan

# ============================================================
# Step 2: Run tests
# ============================================================
$results = @()
$passed = 0
$failed = 0
$skipped = 0
$index = 0

foreach ($example in $exampleDirs) {
    $index++
    $configPath = Join-Path $ScriptDir "$example\config.yaml"

    # A multi-file crew keeps its agents/tasks in sibling folders, so its config.yaml
    # is only the crew settings: the runner must be given the DIRECTORY, not the file.
    $exampleDir = Join-Path $ScriptDir $example
    if ((Test-Path (Join-Path $exampleDir "agents") -PathType Container) -or
        (Test-Path (Join-Path $exampleDir "tasks") -PathType Container)) {
        $configPath = $exampleDir
    }

    # Determine how to run it: trading crews use the dedicated trading runner
    # (`--config <yaml>`); everything else runs on the `orkeon` CLI.
    $cat = ($example -split "[/\\]")[0]
    $isTrading = ($cat -eq "03-finance-trading")
    if ($isTrading -and $Level -eq "load") {
        # The CLI cannot resolve the trading-specific tools and the trading runner has
        # no --validate: skip at this level rather than fail for the wrong reason.
        Write-Host "[$index/$total] $example " -NoNewline
        Write-Host "SKIP (trading runner has no --validate; covered at -Level run)" -ForegroundColor Yellow
        $results += [PSCustomObject]@{ Index = $index; Example = $example; Status = "SKIP"; Duration = 0; Warnings = 0; Error = "" }
        continue
    }

    $shortName = $example -replace "^[0-9]+-[^/\\]+[/\\]", ""
    Write-Host "[$index/$total] $example " -NoNewline

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $status = "UNKNOWN"
    $errorMsg = ""
    $warnings = @()

    try {
        if ($isTrading) {
            $dotnetArgs = @("run", "--project", (Join-Path $ScriptDir "runners\trading"), "--no-build", "--configuration", "Release", "--", "--config", $configPath)
        } else {
            $dotnetArgs = @($OrkeonCliDll, "run", $configPath)
            # Level `load` = config parses and agents/tasks/tools resolve, no LLM call:
            # `orkeon run --validate` does exactly that and exits non-zero on a broken config.
            if ($Level -eq "load") { $dotnetArgs += "--validate" }
        }
        if ($Settings) {
            $dotnetArgs += "--settings"
            $dotnetArgs += $Settings
        }

        $proc = Start-Process -FilePath "dotnet" `
            -ArgumentList $dotnetArgs `
            -WorkingDirectory $RepoRoot `
            -NoNewWindow -PassThru `
            -RedirectStandardOutput "$env:TEMP\orkeon-stdout-$index.txt" `
            -RedirectStandardError "$env:TEMP\orkeon-stderr-$index.txt"

        $exited = $proc.WaitForExit($TimeoutSeconds * 1000)
        $sw.Stop()

        $stdout = if (Test-Path "$env:TEMP\orkeon-stdout-$index.txt") { Get-Content "$env:TEMP\orkeon-stdout-$index.txt" -Raw } else { "" }
        $stderr = if (Test-Path "$env:TEMP\orkeon-stderr-$index.txt") { Get-Content "$env:TEMP\orkeon-stderr-$index.txt" -Raw } else { "" }
        $combined = "$stdout`n$stderr"

        # Extract warnings
        $warnings = @($combined -split "`n" | Where-Object { $_ -match "warn:" } | ForEach-Object { $_.Trim() })

        if (-not $exited) {
            # Timeout
            try { $proc.Kill() } catch {}
            $status = "TIMEOUT"
            $errorMsg = "Killed after ${TimeoutSeconds}s"
            $failed++
        }
        elseif ($proc.ExitCode -eq 0) {
            $status = "PASS"
            $passed++
        }
        elseif ($combined -match "Successfully created crew") {
            # Crew loaded but execution failed (e.g., LLM issue)
            if ($Level -eq "load") {
                $status = "PASS"
                $passed++
            } else {
                $status = "FAIL"
                # Extract the exception message
                $errorMsg = ($combined -split "`n" | Where-Object { $_ -match "fail:|Exception|Error" } | Select-Object -First 3) -join "; "
                $failed++
            }
        }
        else {
            $status = "FAIL"
            $errorMsg = ($combined -split "`n" | Where-Object { $_ -match "fail:|Exception|Error|ERROR" } | Select-Object -First 3) -join "; "
            $failed++
        }

        # Cleanup temp files
        Remove-Item "$env:TEMP\orkeon-stdout-$index.txt" -ErrorAction SilentlyContinue
        Remove-Item "$env:TEMP\orkeon-stderr-$index.txt" -ErrorAction SilentlyContinue
    }
    catch {
        $sw.Stop()
        $status = "ERROR"
        $errorMsg = $_.Exception.Message
        $failed++
    }

    $duration = $sw.Elapsed.TotalSeconds.ToString("F1")

    # Color-coded output
    $color = switch ($status) {
        "PASS"    { "Green" }
        "FAIL"    { "Red" }
        "TIMEOUT" { "Yellow" }
        "ERROR"   { "Magenta" }
        default   { "Gray" }
    }
    Write-Host "$status (${duration}s)" -ForegroundColor $color

    if ($warnings.Count -gt 0 -and $Verbose) {
        foreach ($w in $warnings) {
            Write-Host "  WARN: $w" -ForegroundColor DarkYellow
        }
    }
    if ($errorMsg -and ($status -ne "PASS")) {
        Write-Host "  $errorMsg" -ForegroundColor DarkRed
    }

    $results += [PSCustomObject]@{
        Index    = $index
        Example  = $example
        Status   = $status
        Duration = $duration
        Warnings = $warnings.Count
        Error    = $errorMsg
    }

    if ($StopOnError -and ($status -in @("FAIL", "ERROR"))) {
        Write-Host "`nStopping on first error (-StopOnError)" -ForegroundColor Yellow
        break
    }
}

# ============================================================
# Step 3: Summary
# ============================================================
Write-Host "`n$("=" * 60)" -ForegroundColor Cyan
Write-Host "RESULTS: $passed passed, $failed failed, $skipped skipped / $total total" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "$("=" * 60)`n" -ForegroundColor Cyan

# ============================================================
# Step 4: Generate Markdown report
# ============================================================
if (-not (Test-Path $ReportDir)) { New-Item -ItemType Directory -Path $ReportDir | Out-Null }

$report = @"
# Orkeon Examples Test Report

- **Date**: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
- **Level**: ``$Level``
- **Model**: ``ai/granite-4.0-h-tiny`` (Docker Desktop Models)
- **Timeout**: ${TimeoutSeconds}s per example
- **Results**: $passed passed, $failed failed / $total total

## Summary by Category

| Category | Passed | Failed | Total |
|----------|--------|--------|-------|
"@

# Group by category
$grouped = $results | Group-Object { ($_.Example -split "[/\\]")[0] }
foreach ($g in $grouped | Sort-Object Name) {
    $catPassed = ($g.Group | Where-Object { $_.Status -eq "PASS" }).Count
    $catFailed = ($g.Group | Where-Object { $_.Status -ne "PASS" }).Count
    $catTotal = $g.Group.Count
    $icon = if ($catFailed -eq 0) { "OK" } else { "FAIL" }
    $report += "| $($g.Name) | $catPassed | $catFailed | $catTotal |`n"
}

$report += @"

## Detailed Results

| # | Example | Status | Duration | Warnings | Error |
|---|---------|--------|----------|----------|-------|
"@

foreach ($r in $results) {
    $statusIcon = switch ($r.Status) {
        "PASS"    { "PASS" }
        "FAIL"    { "FAIL" }
        "TIMEOUT" { "TIMEOUT" }
        "ERROR"   { "ERROR" }
        default   { "?" }
    }
    $errorTrunc = if ($r.Error.Length -gt 80) { $r.Error.Substring(0, 80) + "..." } else { $r.Error }
    $errorTrunc = $errorTrunc -replace "\|", "/"  # Escape pipes for markdown
    $report += "| $($r.Index) | ``$($r.Example)`` | $statusIcon | $($r.Duration)s | $($r.Warnings) | $errorTrunc |`n"
}

$report | Set-Content -Path $ReportPath -Encoding UTF8
Write-Host "Report saved to: $ReportPath" -ForegroundColor Cyan

# Exit code
if ($failed -gt 0) { exit 1 } else { exit 0 }
