#!/usr/bin/env bash
# generate-examples-index.sh — Regenerate examples/INDEX.md and examples/usecases.json from
# the on-disk example catalog.
#
# Thin wrapper around the Python generator, scripts/generate_examples_index.py.
# Scans the numbered example categories (01-enterprise … 09-experimental), extracts
# each example's title / process / agents / tasks / tools from its crew + README, and
# writes a navigable Markdown catalog to examples/INDEX.md; joins that to each example's
# usecase.yaml sheet and writes the use-case manifest to examples/usecases.json.
#
# Usage:
#   bash scripts/generate-examples-index.sh            # regenerate both files
#   bash scripts/generate-examples-index.sh --check     # exit 1 if either would change (CI)
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec python3 "$ROOT/scripts/generate_examples_index.py" "$@"
