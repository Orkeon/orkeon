#!/usr/bin/env bash
# The numbers on the front page, recomputed from the tree -- each next to the rule that
# produced it. Same code path as the CI gate (scripts/check-doc-claims.py), so what this
# prints is exactly what the README is checked against. `--json` for machines.
#
#   bash scripts/count-surface.sh
#   bash scripts/count-surface.sh --json
set -euo pipefail
cd "$(dirname "$0")/.."
exec python3 scripts/check-doc-claims.py --surface "$@"
