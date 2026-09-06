#!/usr/bin/env bash
# Renders one campaign — the JSON emitted by `orkeon llm probe --format json` — as the
# archived Markdown report.
#
# Usage:
#   lib/report.sh <campaign.json> <output.md>
#
# The JSON is embedded verbatim at the end of the report behind a marker comment, so the
# index can be rebuilt from the reports alone. One file per campaign, still machine-readable.
#
# Nothing here invents data: every value comes from the probe's own output, which is built
# from observed behaviour and never carries a credential.
set -euo pipefail

LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CATALOG="$LIB_DIR/catalog.json"
JSON_MARKER="<!-- orkeon-campaign-json -->"

if [[ $# -ne 2 ]]; then
  echo "usage: $(basename "$0") <campaign.json> <output.md>" >&2
  exit 2
fi

CAMPAIGN="$1"
OUTPUT="$2"

command -v jq >/dev/null 2>&1 || { echo "report.sh: jq is required." >&2; exit 2; }

provider=$(jq -r '.provider' "$CAMPAIGN")
model=$(jq -r '.model' "$CAMPAIGN")
host=$(jq -r '.endpointHost' "$CAMPAIGN")
version=$(jq -r '.orkeonVersion' "$CAMPAIGN")
commit=$(jq -r '.commit // "" | if . == "" then "not supplied" else . end' "$CAMPAIGN")
stamp=$(jq -r '.timestampUtc' "$CAMPAIGN")
# Older campaigns predate the field; "not pinned" is the honest rendering of a run whose
# sampling was left at the framework default, and reads as the caveat it is.
temperature=$(jq -r 'if has("temperature") then (.temperature | tostring) else "not pinned" end' "$CAMPAIGN")
# Reasoning efforts pinned by the per-model registry (requiredParams): present only when
# the campaign had to depart from the default — the header carries the values actually
# used, otherwise an M7 verdict reads as obtained at the default.
thinking_effort=$(jq -r '.thinking_effort // ""' "$CAMPAIGN")
m7_effort=$(jq -r '.m7_thinking_effort // ""' "$CAMPAIGN")
passed=$(jq -r '.passed' "$CAMPAIGN")
failed=$(jq -r '.failed' "$CAMPAIGN")
skipped=$(jq -r '.notApplicable' "$CAMPAIGN")
modes_run=$(jq -r '[.modes[].mode] | join(", ")' "$CAMPAIGN")

# An alias resolves to the same notes as its canonical key: "hf" must read like "huggingface".
canonical=$(jq -r --arg p "$provider" '.providers[$p].aliasOf // $p' "$CATALOG")
label=$(jq -r --arg p "$canonical" '.providers[$p].label // $p' "$CATALOG")
section=$(jq -r --arg p "$canonical" '.providers[$p].matrix // ""' "$CATALOG")

if [[ "$failed" -gt 0 ]]; then
  verdict="❌ **Failure** — $failed failed mode(s)"
elif [[ "$passed" -eq 0 ]]; then
  verdict="➖ **Nothing exercised** — no mode applicable to this provider"
else
  verdict="✅ **Success** — $passed mode(s) validated"
fi

mkdir -p "$(dirname "$OUTPUT")"

{
  echo "# Campaign ${label} — \`${model}\`"
  echo
  echo "|  |  |"
  echo "|---|---|"
  echo "| **Provider** | \`${provider}\` |"
  echo "| **Model** | \`${model}\` |"
  echo "| **Endpoint** | \`${host}\` |"
  echo "| **Timestamp (UTC)** | ${stamp} |"
  echo "| **Orkeon version** | ${version} |"
  echo "| **Commit** | \`${commit}\` |"
  echo "| **Modes exercised** | ${modes_run} |"
  echo "| **Temperature** | ${temperature} |"
  [[ -n "$thinking_effort" ]] && echo "| **Reasoning effort (base)** | ${thinking_effort} |"
  [[ -n "$m7_effort" ]] && echo "| **Reasoning effort (M7)** | ${m7_effort} |"
  echo "| **Proof quality** | archived output |"
  echo
  echo "## Results"
  echo
  echo "| Mode | Protocol | Result | Detail | Duration |"
  echo "|---|---|---|---|---|"

  jq -r --slurpfile catalog "$CATALOG" '
    .modes[]
    | [ .mode,
        ($catalog[0].modes[.mode] // ""),
        .symbol,
        (.detail | gsub("\\|"; "/")),
        "\(.elapsedMs) ms" ]
    | "| " + join(" | ") + " |"
  ' "$CAMPAIGN"

  echo
  echo "${verdict} · ${passed} ✅ · ${failed} ❌ · ${skipped} ➖"
  echo
  echo "> ➖ = mode not applicable to this provider or this model. Nothing was exercised, so there is"
  echo "> nothing to fix — it is an absence of capability, not a defect."

  notes=$(jq -r --arg p "$canonical" '.providers[$p].notes // [] | .[] | "- " + .' "$CATALOG")
  if [[ -n "$notes" ]]; then
    echo
    echo "## Points of attention (matrix ${section})"
    echo
    echo "$notes"
  fi

  echo
  echo "## To carry into the matrix"
  echo
  echo "Journal (§7):"
  echo
  echo '```'
  printf '| %s | %s `%s` | campaign `llmproviders-test` | %s | %s | `llmproviders-test/%s/%s` | Archived output |\n' \
    "${stamp%%T*}" "$label" "$model" "$modes_run" \
    "$(if [[ "$failed" -gt 0 ]]; then echo "❌"; else echo "✅"; fi)" \
    "$provider" "$(basename "$OUTPUT")"
  echo '```'
  echo
  echo "Model table (${section:-§6}):"
  echo
  echo '```'
  printf '| `%s` | | | %s | %s | %s | %s | campaign %s |\n' \
    "$model" "$modes_run" \
    "$(if [[ "$failed" -gt 0 ]]; then echo "❌"; else echo "✅"; fi)" \
    "${stamp%%T*}" "$version" "$(basename "$OUTPUT")"
  echo '```'
  echo
  echo "$JSON_MARKER"
  echo '```json'
  cat "$CAMPAIGN"
  echo '```'
} > "$OUTPUT"

echo "$OUTPUT"
