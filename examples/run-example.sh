#!/usr/bin/env bash
# Usage: ./examples/run-example.sh <category/example> [--settings path/to/appsettings.json]
# Ex:    ./examples/run-example.sh 01-enterprise/01-research-assistant
# Ex:    ./examples/run-example.sh 01-enterprise/01-research-assistant --settings examples/_shared/appsettings.docker.json
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

if [ ! -f "$CONFIG_PATH" ]; then
  echo "ERROR: config.yaml not found at $CONFIG_PATH" >&2
  exit 1
fi

# Determine runner: 03-finance-trading -> trading, others -> standard
CATEGORY=$(echo "$EXAMPLE_PATH" | cut -d'/' -f1)
if [ "$CATEGORY" = "03-finance-trading" ]; then
  RUNNER="examples/runners/trading"
else
  RUNNER="examples/runners/standard"
fi

echo "Running $EXAMPLE_PATH with runner $(basename $RUNNER)..."
dotnet run --project "$RUNNER" -- --config "$CONFIG_PATH" $SETTINGS_ARG
