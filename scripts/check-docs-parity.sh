#!/usr/bin/env bash
# Bilingual documentation parity gate (OSS-012 / R8.4).
#
# Fails if any English documentation file lacks its French mirror (or vice versa):
#   - every docs/**/*.md outside docs/fr/  ⇄  docs/fr/**/*.md
#   - the root pairs                        README/CONTRIBUTING/CODE_OF_CONDUCT/SECURITY/SUPPORT/index .md ⇄ .fr.md
#
# This checks file *existence* (the contract), not content equivalence — a missing mirror
# is a hard error; keeping the two in sync content-wise stays the contributor's job.
set -euo pipefail

cd "$(dirname "$0")/.."

fail=0

note() { printf '  - %s\n' "$1"; }

# --- docs/ ⇄ docs/fr/ -------------------------------------------------------------------
en_docs=$(cd docs && find . -name '*.md' -not -path './fr/*' | sed 's|^\./||' | sort)
fr_docs=$(cd docs/fr && find . -name '*.md' | sed 's|^\./||' | sort)

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
for base in README CONTRIBUTING CODE_OF_CONDUCT SECURITY SUPPORT CLA index; do
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

# --- informational content-drift report (never fails the build) ------------------------
# The parity contract is existence-only by design; this surfaces mirrors whose line
# counts diverge enough to suggest one side fell behind (DOC-02/F3).
# The threshold is relative: French prose naturally runs a few percent longer than
# the English original, so a fixed line count alone false-positives on long files.
# A pair is flagged only when the delta exceeds BOTH the absolute floor (so short
# files are not flagged for a reworded paragraph) AND the percentage of the larger
# side (so long files tolerate proportional wording drift while a genuinely missing
# section still trips it).
DRIFT_THRESHOLD=${DRIFT_THRESHOLD:-15}
DRIFT_THRESHOLD_PCT=${DRIFT_THRESHOLD_PCT:-7}
drift=0
while IFS= read -r f; do
  en="docs/$f"; fr="docs/fr/$f"
  [ -f "$fr" ] || continue
  en_lc=$(wc -l < "$en"); fr_lc=$(wc -l < "$fr")
  delta=$((en_lc - fr_lc)); [ "$delta" -lt 0 ] && delta=$((-delta))
  max_lc=$en_lc; [ "$fr_lc" -gt "$max_lc" ] && max_lc=$fr_lc
  pct_allowance=$((max_lc * DRIFT_THRESHOLD_PCT / 100))
  if [ "$delta" -gt "$DRIFT_THRESHOLD" ] && [ "$delta" -gt "$pct_allowance" ]; then
    [ "$drift" -eq 0 ] && echo "::notice::EN/FR content drift (informational, threshold ${DRIFT_THRESHOLD} lines and ${DRIFT_THRESHOLD_PCT}% of the larger file):"
    note "$en ($en_lc) vs $fr ($fr_lc) — Δ$delta"
    drift=$((drift + 1))
  fi
done <<< "$en_docs"
[ "$drift" -gt 0 ] && echo "  ($drift mirror(s) drifting — informational only, the contributor keeps content in sync)"
exit 0
