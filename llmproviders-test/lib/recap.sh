#!/usr/bin/env bash
# Rebuilds llmproviders-test/README.md from the campaign reports present on disk.
#
# Usage:
#   lib/recap.sh [root-dir]        # default: the kit directory
#
# Rebuild, not append. An index that grows by appending drifts the moment a report is
# deleted, re-run or renamed; one derived from what is actually on disk cannot. Running it
# twice in a row is therefore a no-op, which is exactly what makes it safe to call after
# every single campaign.
set -euo pipefail

LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="${1:-$(cd "$LIB_DIR/.." && pwd)}"
HEADER="$LIB_DIR/readme-header.md"
JSON_MARKER="<!-- orkeon-campaign-json -->"

command -v jq >/dev/null 2>&1 || { echo "recap.sh: jq is required." >&2; exit 2; }

# Pulls the embedded campaign JSON back out of a rendered report.
extract_json() {
  awk -v marker="$JSON_MARKER" '
    $0 == marker { seen = 1; next }
    seen && $0 == "```json" { inside = 1; next }
    inside && $0 == "```" { exit }
    inside { print }
  ' "$1"
}

rows=""
count=0
declare -A latest_line=()

# Sorted so the index is byte-stable across runs and across filesystems.
while IFS= read -r report; do
  json=$(extract_json "$report")
  [[ -n "$json" ]] || continue

  rel="${report#"$ROOT"/}"
  read -r provider model stamp version passed failed skipped < <(
    printf '%s' "$json" | jq -r '[.provider, .model, .timestampUtc, .orkeonVersion,
      (.passed|tostring), (.failed|tostring), (.notApplicable|tostring)] | @tsv' | tr '\t' ' ')

  if [[ "$failed" -gt 0 ]]; then status="❌"; else status="✅"; fi
  modes=$(printf '%s' "$json" | jq -r '[.modes[].mode] | join(" ")')

  # Prefixed with an explicit sort key. Two campaigns launched in parallel routinely land on
  # the same second, and a plain reverse sort over the rendered line would then break the tie
  # on whatever the row happens to start with — differently from the PowerShell mirror.
  rows+="${stamp}"$'\t'"${provider}"$'\t'"${model}"$'\t'
  rows+="| ${stamp} | \`${provider}\` | \`${model}\` | ${status} ${passed}/${failed}/${skipped} | ${version} | [rapport](${rel}) |"$'\n'
  count=$((count + 1))

  # Reports are visited in ascending timestamp order, so the last write wins.
  latest_line["$provider"]="| \`${provider}\` | \`${model}\` | ${stamp%%T*} | ${status} | ${modes} |"
# `lib/` sits at the same depth as the provider directories, so it has to be excluded by name.
# Nothing in it has an embedded campaign JSON today, which is the only reason a stray Markdown
# file there has never been mistaken for a report — that is luck, not a rule.
done < <(find "$ROOT" -mindepth 2 -maxdepth 2 -name '*.md' -type f -not -path "$ROOT/lib/*" | sort)

{
  cat "$HEADER"
  echo
  echo "## Campagnes exécutées"
  echo

  if [[ "$count" -eq 0 ]]; then
    echo "_Aucune campagne archivée pour l'instant._ Lancez-en une — commencez par Ollama, dont le coût est nul :"
    echo
    echo '```bash'
    echo "llmproviders-test/run-campaign.sh --provider ollama --model llama3.2"
    echo '```'
  else
    echo "| Horodatage (UTC) | Provider | Modèle | ✅/❌/➖ | Version | Rapport |"
    echo "|---|---|---|---|---|---|"
    # Newest first — then provider, then model, so a tie is broken the same way everywhere.
    printf '%s' "$rows" | sort -t$'\t' -k1,1r -k2,2 -k3,3 | cut -f4-
    echo
    echo "## Dernière campagne par provider"
    echo
    echo "| Provider | Modèle | Date | Statut | Modes exercés |"
    echo "|---|---|---|---|---|"
    for provider in $(printf '%s\n' "${!latest_line[@]}" | sort); do
      echo "${latest_line[$provider]}"
    done
  fi

  echo
  echo "---"
  echo
  # Names both mirrors, never the one that happened to run: the index is versioned, and a
  # footer that changed with the operator's platform would churn the file for nothing.
  echo "_Index régénéré par \`lib/recap.sh\` ou \`lib/recap.ps1\` depuis les rapports présents sur disque._"
  echo "_${count} campagne(s) archivée(s)._"
} > "$ROOT/.README.md.$$"

# Written aside then moved into place. Two campaigns running in parallel both rebuild the
# index when they finish; a direct `> README.md` truncates before it writes, so the loser of
# that race can be read half-empty. A rename on the same filesystem cannot be observed
# partially — the reader sees the old file or the new one, never a torn one.
mv -f "$ROOT/.README.md.$$" "$ROOT/README.md"

echo "$ROOT/README.md"
