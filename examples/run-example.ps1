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
$ExampleDir = "examples/$ExamplePath"

if (-not (Test-Path $ConfigPath)) {
    # Migrated examples declare their crew in TypeScript (EX-01): one main.ork.ts,
    # run by the orkeon CLI. esbuild is required, so the npm bootstrap stays ON.
    $TsCrew = Join-Path $ExampleDir "main.ork.ts"
    if (Test-Path $TsCrew) {
        Write-Host "Running $ExamplePath with the orkeon CLI (TypeScript crew)..."
        & dotnet run --project src/scripting/Orkeon.Scripting.Cli -c Release -- run $TsCrew @(if ($Settings) { @("--settings", $Settings) })
        exit $LASTEXITCODE
    }
    # Code-driven examples (e.g. rag/basic-ingestion) ship a console project
    # instead of a config.yaml crew: run the folder's csproj directly.
    $Csproj = Get-ChildItem -Path $ExampleDir -Filter *.csproj -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($Csproj) {
        Write-Host "Running $ExamplePath as a console project..."
        & dotnet run --project $Csproj.FullName
        exit $LASTEXITCODE
    }
    Write-Error "ERROR: neither config.yaml nor a .csproj found under $ExampleDir"
    exit 1
}

Write-Host "Running $ExamplePath with the orkeon CLI..."
# YAML crews don't need esbuild; skip the scripting npm bootstrap during build.
$dotnetArgs = @("run", "--project", "src/scripting/Orkeon.Scripting.Cli", "-c", "Release",
                "-p:SkipScriptingNpmInstall=true", "--", "run", $ConfigPath)

if ($Settings) {
    $dotnetArgs += "--settings"
    $dotnetArgs += $Settings
}

& dotnet @dotnetArgs
