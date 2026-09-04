#!/usr/bin/env bash
# Usage: ./examples/run-example.sh <category/example> [--settings path/to/appsettings.json]
# Ex:    ./examples/run-example.sh 01-enterprise/01-research-assistant
# Ex:    ./examples/run-example.sh 01-enterprise/01-research-assistant --settings examples/appsettings/appsettings.json
set -euo pipefail

EXAMPLE_PATH="$1"
shift
SETTINGS_ARG=""

# Parse optional --settings
while [[ $# -gt 0 ]]; do
    case "$1" in
        --settings|-s) SETTINGS_ARG="--settings $2"; shift 2 ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

CONFIG_PATH="examples/${EXAMPLE_PATH}/config.yaml"
EXAMPLE_DIR="examples/${EXAMPLE_PATH}"

if [ ! -f "$CONFIG_PATH" ]; then
  # Migrated examples declare their crew in TypeScript (EX-01): one main.ork.ts,
  # run by the orkeon CLI. esbuild is required, so the npm bootstrap stays ON.
  if [ -f "$EXAMPLE_DIR/main.ork.ts" ]; then
    echo "Running $EXAMPLE_PATH with the orkeon CLI (TypeScript crew)..."
    exec dotnet run --project src/scripting/Orkeon.Scripting.Cli -c Release \
      -- run "$EXAMPLE_DIR/main.ork.ts" $SETTINGS_ARG
  fi
  # Code-driven examples (e.g. rag/basic-ingestion) ship a console project
  # instead of a config.yaml crew: run the folder's csproj directly.
  CSPROJ=$(find "$EXAMPLE_DIR" -maxdepth 1 -name '*.csproj' 2>/dev/null | head -n 1)
  if [ -n "$CSPROJ" ]; then
    echo "Running $EXAMPLE_PATH as a console project..."
    exec dotnet run --project "$CSPROJ"
  fi
  echo "ERROR: neither config.yaml nor a .csproj found under $EXAMPLE_DIR" >&2
  exit 1
fi

# Pick the runner: the 03-finance-trading examples reference the trading tool pack
# and use the dedicated trading runner (`--config <yaml>`); everything else runs on
# the `orkeon` CLI (`orkeon run <yaml>`), which loads a YAML crew through the same
# one-shot host as the trading runner.
CATEGORY=$(echo "$EXAMPLE_PATH" | cut -d'/' -f1)
if [ "$CATEGORY" = "03-finance-trading" ]; then
  echo "Running $EXAMPLE_PATH with the trading runner..."
  dotnet run --project examples/runners/trading -- --config "$CONFIG_PATH" $SETTINGS_ARG
else
  echo "Running $EXAMPLE_PATH with the orkeon CLI..."
  # YAML crews don't need esbuild; skip the scripting npm bootstrap during build.
  dotnet run --project src/scripting/Orkeon.Scripting.Cli -c Release \
    -p:SkipScriptingNpmInstall=true -- run "$CONFIG_PATH" $SETTINGS_ARG
fi
