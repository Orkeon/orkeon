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
commit=$(jq -r '.commit // "" | if . == "" then "non fourni" else . end' "$CAMPAIGN")
stamp=$(jq -r '.timestampUtc' "$CAMPAIGN")
# Older campaigns predate the field; "non épinglée" is the honest rendering of a run whose
# sampling was left at the framework default, and reads as the caveat it is.
temperature=$(jq -r 'if has("temperature") then (.temperature | tostring) else "non épinglée" end' "$CAMPAIGN")
# Efforts de raisonnement epingles par le registre par-modele (requiredParams) : presents
# uniquement quand la campagne a du s'ecarter du defaut — l'en-tete porte les valeurs
# reellement utilisees, sinon un verdict M7 lit comme obtenu au defaut.
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
  verdict="❌ **Échec** — $failed mode(s) en échec"
elif [[ "$passed" -eq 0 ]]; then
  verdict="➖ **Rien exercé** — aucun mode applicable à ce provider"
else
  verdict="✅ **Succès** — $passed mode(s) validé(s)"
fi

mkdir -p "$(dirname "$OUTPUT")"

{
  echo "# Campagne ${label} — \`${model}\`"
  echo
  echo "|  |  |"
  echo "|---|---|"
  echo "| **Provider** | \`${provider}\` |"
  echo "| **Modèle** | \`${model}\` |"
  echo "| **Endpoint** | \`${host}\` |"
  echo "| **Horodatage (UTC)** | ${stamp} |"
  echo "| **Version Orkéon** | ${version} |"
  echo "| **Commit** | \`${commit}\` |"
  echo "| **Modes exercés** | ${modes_run} |"
  echo "| **Température** | ${temperature} |"
  [[ -n "$thinking_effort" ]] && echo "| **Effort de raisonnement (base)** | ${thinking_effort} |"
  [[ -n "$m7_effort" ]] && echo "| **Effort de raisonnement (M7)** | ${m7_effort} |"
  echo "| **Qualité de preuve** | sortie archivée |"
  echo
  echo "## Résultats"
  echo
  echo "| Mode | Protocole | Résultat | Détail | Durée |"
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
  echo "> ➖ = mode non applicable à ce provider ou à ce modèle. Rien n'a été exercé, il n'y a"
  echo "> donc rien à corriger — c'est une absence de capacité, pas un défaut."

  notes=$(jq -r --arg p "$canonical" '.providers[$p].notes // [] | .[] | "- " + .' "$CATALOG")
  if [[ -n "$notes" ]]; then
    echo
    echo "## Points de vigilance (matrice ${section})"
    echo
    echo "$notes"
  fi

  echo
  echo "## À reporter dans la matrice"
  echo
  echo "Journal (§7) :"
  echo
  echo '```'
  printf '| %s | %s `%s` | campagne `llmproviders-test` | %s | %s | `llmproviders-test/%s/%s` | Sortie archivée |\n' \
    "${stamp%%T*}" "$label" "$model" "$modes_run" \
    "$(if [[ "$failed" -gt 0 ]]; then echo "❌"; else echo "✅"; fi)" \
    "$provider" "$(basename "$OUTPUT")"
  echo '```'
  echo
  echo "Tableau modèles (${section:-§6}) :"
  echo
  echo '```'
  printf '| `%s` | | | %s | %s | %s | %s | campagne %s |\n' \
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
