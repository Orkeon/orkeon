#!/usr/bin/env bash
# generate-examples-index.sh — Regenerate examples/INDEX.md from the on-disk example catalog.
#
# Thin wrapper around the Python generator (kept alongside validate_use_cases.py).
# Scans the numbered example categories (01-enterprise … 09-experimental), extracts
# each example's title / process / agents / tasks / tools from its config.yaml + README,
# and writes a navigable Markdown catalog to examples/INDEX.md.
#
# Usage:
#   bash scripts/generate-examples-index.sh            # regenerate examples/INDEX.md
#   bash scripts/generate-examples-index.sh --check     # exit 1 if INDEX.md would change (CI)
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec python3 "$ROOT/scripts/generate_examples_index.py" "$@"
