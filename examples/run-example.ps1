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

# Pick the runner: the 03-finance-trading examples reference the trading tool pack
# and use the dedicated trading runner (`--config <yaml>`); everything else runs on
# the `orkeon` CLI (`orkeon run <yaml>`), which loads a YAML crew through the same
# one-shot host as the trading runner.
$Category = ($ExamplePath -split '/')[0]
if ($Category -eq "03-finance-trading") {
    Write-Host "Running $ExamplePath with the trading runner..."
    $dotnetArgs = @("run", "--project", "examples/runners/trading", "--", "--config", $ConfigPath)
} else {
    Write-Host "Running $ExamplePath with the orkeon CLI..."
    # YAML crews don't need esbuild; skip the scripting npm bootstrap during build.
    $dotnetArgs = @("run", "--project", "src/scripting/Orkeon.Scripting.Cli", "-c", "Release",
                    "-p:SkipScriptingNpmInstall=true", "--", "run", $ConfigPath)
}

if ($Settings) {
    $dotnetArgs += "--settings"
    $dotnetArgs += $Settings
}

& dotnet @dotnetArgs
