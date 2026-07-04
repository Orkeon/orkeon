# Usage: .\examples\run-example.ps1 <category/example> [-Settings path\to\appsettings.json]
# Ex:    .\examples\run-example.ps1 01-enterprise/01-research-assistant
# Ex:    .\examples\run-example.ps1 01-enterprise/01-research-assistant -Settings examples\_shared\appsettings.docker.json
param(
    [Parameter(Mandatory=$true, Position=0)]
    [string]$ExamplePath,

    [Parameter(Mandatory=$false)]
    [string]$Settings = ""
)

$ErrorActionPreference = "Stop"

$ConfigPath = "examples/$ExamplePath/config.yaml"

if (-not (Test-Path $ConfigPath)) {
    Write-Error "ERROR: config.yaml not found at $ConfigPath"
    exit 1
}

# Determine runner: 03-finance-trading -> trading, others -> standard
$Category = ($ExamplePath -split '/')[0]
if ($Category -eq "03-finance-trading") {
    $Runner = "examples/runners/trading"
} else {
    $Runner = "examples/runners/standard"
}

$RunnerName = Split-Path $Runner -Leaf
Write-Host "Running $ExamplePath with runner $RunnerName..."

$args = @("run", "--project", $Runner, "--", "--config", $ConfigPath)
if ($Settings) {
    $args += "--settings"
    $args += $Settings
}

& dotnet @args
