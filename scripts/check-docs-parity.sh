#!/usr/bin/env bash
# Bilingual documentation parity gate (OSS-012 / R8.4).
#
# Fails if any English documentation file lacks its French mirror (or vice versa):
#   - every docs/**/*.md outside docs/fr/  ⇄  docs/fr/**/*.md
#   - the root community pairs              README/CONTRIBUTING/CODE_OF_CONDUCT/SECURITY .md ⇄ .fr.md
#
# This checks file *existence* (the contract), not content equivalence — a missing mirror
# is a hard error; keeping the two in sync content-wise stays the contributor's job.
set -euo pipefail

cd "$(dirname "$0")/.."

fail=0

note() { printf '  - %s\n' "$1"; }

# --- docs/ ⇄ docs/fr/ -------------------------------------------------------------------
# docs/audit/ holds dated audit snapshots (GO/NO-GO reports) — English-only by decision
# (PUB-01, 2026-08-17), excluded from the bilingual parity contract.
en_docs=$(cd docs && find . -name '*.md' -not -path './fr/*' -not -path './audit/*' | sed 's|^\./||' | sort)
fr_docs=$(cd docs/fr && find . -name '*.md' -not -path './audit/*' | sed 's|^\./||' | sort)

missing_fr=$(comm -23 <(printf '%s\n' "$en_docs") <(printf '%s\n' "$fr_docs"))
missing_en=$(comm -13 <(printf '%s\n' "$en_docs") <(printf '%s\n' "$fr_docs"))

if [ -n "$missing_fr" ]; then
  echo "::error::English docs without a French mirror under docs/fr/:"
  while IFS= read -r f; do [ -n "$f" ] && note "docs/$f  →  missing docs/fr/$f"; done <<< "$missing_fr"
  fail=1
fi

if [ -n "$missing_en" ]; then
  echo "::error::French docs without an English original under docs/:"
  while IFS= read -r f; do [ -n "$f" ] && note "docs/fr/$f  →  missing docs/$f"; done <<< "$missing_en"
  fail=1
fi

# --- root community pairs ---------------------------------------------------------------
for base in README CONTRIBUTING CODE_OF_CONDUCT SECURITY; do
  if [ -f "$base.md" ] && [ ! -f "$base.fr.md" ]; then
    echo "::error::$base.md has no French mirror $base.fr.md"; fail=1
  fi
  if [ -f "$base.fr.md" ] && [ ! -f "$base.md" ]; then
    echo "::error::$base.fr.md has no English original $base.md"; fail=1
  fi
done

if [ "$fail" -ne 0 ]; then
  echo "Documentation parity check FAILED — every doc must have its bilingual mirror (see CONTRIBUTING)."
  exit 1
fi

echo "Documentation parity check passed: all docs have their bilingual mirror."
