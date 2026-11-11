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
KIT_DIR="$(cd "$LIB_DIR/.." && pwd)"
ROOT="${1:-$KIT_DIR}"
ROOT="$(cd "$ROOT" && pwd)"
HEADER="$LIB_DIR/readme-header.md"
CATALOG="$LIB_DIR/catalog.json"
DOCS_DIR="$KIT_DIR/../docs"
# The documentation links a report by its published URL: the docs site does not carry the kit.
REPORT_URL="https://github.com/Orkeon/orkeon/blob/main/llmproviders-test"
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
mode_rows=""
count=0
declare -A latest_line=()

# Sorted so the index is byte-stable across runs and across filesystems.
while IFS= read -r report; do
  json=$(extract_json "$report")
  [[ -n "$json" ]] || continue

  rel="${report#"$ROOT"/}"
  read -r provider model stamp commit passed failed skipped < <(
    printf '%s' "$json" | jq -r '[.provider, .model, .timestampUtc, (.commit // "" | if . == "" then "—" else . end),
      (.passed|tostring), (.failed|tostring), (.notApplicable|tostring)] | @tsv' | tr '\t' ' ')

  if [[ "$failed" -gt 0 ]]; then status="❌"; else status="✅"; fi
  modes=$(printf '%s' "$json" | jq -r '[.modes[].mode] | join(" ")')
  # One line per mode result, for the per-model table.
  mode_rows+=$(printf '%s' "$json" | jq -r --arg rel "$rel" \
    '. as $c | .modes[] | [$c.provider, $c.model, $c.timestampUtc, .mode, .outcome, $rel] | @tsv')$'\n'

  # Prefixed with an explicit sort key. Two campaigns launched in parallel routinely land on
  # the same second, and a plain reverse sort over the rendered line would then break the tie
  # on whatever the row happens to start with — differently from the PowerShell mirror.
  rows+="${stamp}"$'\t'"${provider}"$'\t'"${model}"$'\t'
  rows+="| ${stamp} | \`${provider}\` | \`${model}\` | ${status} ${passed}/${failed}/${skipped} | \`${commit}\` | [report](${rel}) |"$'\n'
  count=$((count + 1))

  # Reports are visited in ascending timestamp order, so the last write wins.
  latest_line["$provider"]="| \`${provider}\` | \`${model}\` | ${stamp%%T*} | ${status} | ${modes} |"
# `lib/` sits at the same depth as the provider directories, so it has to be excluded by name.
# Nothing in it has an embedded campaign JSON today, which is the only reason a stray Markdown
# file there has never been mistaken for a report — that is luck, not a rule.
done < <(find "$ROOT" -mindepth 2 -maxdepth 2 -name '*.md' -type f -not -path "$ROOT/lib/*" | sort)

# The latest result of each model, one row per model, as TSV: provider, label, model, default
# flag, date, passed, failed, not applicable, failed modes, report. A row merges every report
# of the model's latest campaign day, the latest result of each mode winning: the vision
# companion's M9 run and the paid D8 run join the main run of their day, and a day older than
# the model's latest never mixes in. The report linked is that day's with the most modes.
# Sorted on code points (jq), the same order as the PowerShell mirror's ordinal sort.
model_table_tsv() {
  local providers
  providers=$(jq -c '.providers | with_entries(select(.value.aliasOf == null))
    | map_values({d: (.defaultModel // ""), l: (.label // "")})' "$CATALOG")
  printf '%s' "$mode_rows" | jq -R -s -r --argjson cat "$providers" '
    def mode_key: [.[0:1], (.[1:] | tonumber)];
    split("\n") | map(select(length > 0) | split("\t")
      | {p: .[0], m: .[1], s: .[2], mode: .[3], o: .[4], rel: .[5]})
    | group_by([.p, .m])
    | map((map(.s[0:10]) | max) as $d
        | map(select(.s[0:10] == $d)) | sort_by(.s)
        | (reduce .[] as $x ({}; .[$x.mode] = $x.o)) as $res
        | {p: .[0].p, m: .[0].m, d: $d,
           def: (if ($cat[.[0].p].d // "") == .[0].m then 0 else 1 end),
           l: (($cat[.[0].p].l // "") | if . == "" then null else . end),
           pass: ([$res[] | select(. == "passed")] | length),
           fail: ([$res[] | select(. == "failed")] | length),
           na: ([$res[] | select(. == "not-applicable")] | length),
           failed: ([$res | to_entries[] | select(.value == "failed") | .key] | sort_by(mode_key) | join(", ") | if . == "" then "—" else . end),
           rel: (group_by(.rel) | map({rel: .[0].rel, n: length, s: .[0].s}) | sort_by(.n, .s) | last.rel)})
    | sort_by(.p, .def, (.d | explode | map(-.)), .m)
    | .[] | [.p, (.l // .p), .m, (.def | tostring), .d, (.pass | tostring), (.fail | tostring),
             (.na | tostring), .failed, .rel] | @tsv'
}

# Renders the per-model table. $1: en or fr; $2: the prefix of a report link. No field of the
# TSV is ever empty ("—" stands for no failed mode): read would merge two tabs into one.
render_model_table() {
  local lang="$1" prefix="$2" previous="" p label model def d pass fail na failed rel cell mark
  if [[ "$lang" == fr ]]; then
    echo "| Fournisseur | Modèle | Date | ✅/❌/➖ | Modes en échec | Rapport |"
  else
    echo "| Provider | Model | Date | ✅/❌/➖ | Failed modes | Report |"
  fi
  echo "|---|---|---|---|---|---|"
  while IFS=$'\t' read -r p label model def d pass fail na failed rel; do
    cell=""
    [[ "$p" == "$previous" ]] || cell="$label"
    previous="$p"
    mark=""
    if [[ "$def" == 0 ]]; then
      if [[ "$lang" == fr ]]; then mark=" *(défaut)*"; else mark=" *(default)*"; fi
    fi
    echo "| ${cell} | \`${model}\`${mark} | ${d} | ${pass}/${fail}/${na} | ${failed} | [$([[ "$lang" == fr ]] && echo rapport || echo report)](${prefix}${rel}) |"
  done <<< "$model_table"
}

model_table=""
[[ "$count" -eq 0 ]] || model_table=$(model_table_tsv)
latest_day=$(printf '%s' "$model_table" | cut -f5 | sort | tail -n 1)
model_count=$(printf '%s' "$model_table" | grep -c . || true)

{
  cat "$HEADER"
  echo
  echo "## Campaigns run"
  echo
  # Generated, not hand-written: the index is rebuilt from scratch on every run, so a note
  # that only lived in the committed README would vanish at the next campaign.
  echo "> The per-campaign reports dated before 2026-09-06 are written in **French**: they predate the"
  echo "> switch of the report generator to English (2026-09-06), and they are timestamped evidence, so"
  echo "> they are kept exactly as they were produced rather than re-rendered. Reports from 2026-09-06"
  echo "> onwards are in English."
  echo

  if [[ "$count" -eq 0 ]]; then
    echo "_No campaign archived yet._ Launch one — start with Ollama, whose cost is zero:"
    echo
    echo '```bash'
    echo "llmproviders-test/run-campaign.sh --provider ollama --model llama3.2"
    echo '```'
  else
    echo "| Timestamp (UTC) | Provider | Model | ✅/❌/➖ | Commit | Report |"
    echo "|---|---|---|---|---|---|"
    # Newest first — then provider, then model, so a tie is broken the same way everywhere.
    printf '%s' "$rows" | sort -t$'\t' -k1,1r -k2,2 -k3,3 | cut -f4-
    echo
    echo "## Latest campaign per provider"
    echo
    echo "| Provider | Model | Date | Status | Modes exercised |"
    echo "|---|---|---|---|---|"
    for provider in $(printf '%s\n' "${!latest_line[@]}" | sort); do
      echo "${latest_line[$provider]}"
    done
    echo
    echo "## Latest result per model"
    echo
    echo "One row per model: the latest result of each mode on the model's latest campaign day (the"
    echo "vision companion's M9 and the paid D8 join the main run of their day). The same table is"
    echo "published in the documentation, \`docs/reference/llm-campaign-results.md\`."
    echo
    render_model_table en ""
  fi

  echo
  echo "---"
  echo
  # Names both mirrors, never the one that happened to run: the index is versioned, and a
  # footer that changed with the operator's platform would churn the file for nothing.
  echo "_Index regenerated by \`lib/recap.sh\` or \`lib/recap.ps1\` from the reports present on disk._"
  echo "_${count} archived campaign(s)._"
} > "$ROOT/.README.md.$$"

# Written aside then moved into place. Two campaigns running in parallel both rebuild the
# index when they finish; a direct `> README.md` truncates before it writes, so the loser of
# that race can be read half-empty. A rename on the same filesystem cannot be observed
# partially — the reader sees the old file or the new one, never a torn one.
mv -f "$ROOT/.README.md.$$" "$ROOT/README.md"

echo "$ROOT/README.md"

# The documentation page, in English and its French mirror. Only for the kit's own reports: a
# campaign archived elsewhere (--out) is not what the documentation publishes.
write_docs_page() {
  local lang="$1" target="$2"
  {
    if [[ "$lang" == fr ]]; then
      echo "> 🇬🇧 [English version](../../reference/llm-campaign-results.md)"
      echo
      echo "# Résultats des campagnes LLM"
      echo
      echo "> Page générée par \`llmproviders-test/lib/recap.sh\` (ou \`recap.ps1\`) à la fin de chaque"
      echo "> campagne : ne pas la modifier à la main."
      echo
      echo "Chaque modèle que le kit de campagne a exercé contre l'API réelle de son fournisseur, avec le"
      echo "dernier résultat de chaque mode. Une ligne réunit les rapports du dernier jour de campagne du"
      echo "modèle : le M9 du compagnon vision et le D8 payant rejoignent la campagne principale de leur jour."
      echo "Les jours plus anciens sont dans le [journal des campagnes](${REPORT_URL}/README.md#campaigns-run)."
      echo
      echo "Les modes M1 à M13 sont ceux du chat, D1 à D8 ceux des modèles de décision ; ils sont définis dans"
      echo "le [README du kit](${REPORT_URL}/README.md#the-protocol-modes). ✅ réussi, ❌ en échec, ➖ sans"
      echo "objet (le fournisseur ou le modèle ne déclare pas la capacité). Les modèles par défaut et les"
      echo "raisons de leur choix : [comparatif des fournisseurs LLM](llm-providers-comparison.md)."
      echo
      echo "Dernière campagne : ${latest_day} · ${model_count} modèles · ${count} rapports archivés."
    else
      echo "> 🇫🇷 [Version française](../fr/reference/llm-campaign-results.md)"
      echo
      echo "# LLM campaign results"
      echo
      echo "> Generated by \`llmproviders-test/lib/recap.sh\` (or \`recap.ps1\`) at the end of every"
      echo "> campaign: do not edit it by hand."
      echo
      echo "Every model the campaign kit has run against its vendor's real API, with the latest result of"
      echo "each mode. A row merges the reports of the model's latest campaign day: the vision companion's"
      echo "M9 and the paid D8 join the main run of their day. Older days are in the"
      echo "[campaign journal](${REPORT_URL}/README.md#campaigns-run)."
      echo
      echo "Modes M1 to M13 are the chat modes, D1 to D8 the decision-model modes; they are defined in the"
      echo "[kit README](${REPORT_URL}/README.md#the-protocol-modes). ✅ passed, ❌ failed, ➖ not"
      echo "applicable (the provider or the model does not declare the capability). The default models and"
      echo "why they were chosen: [LLM provider comparison](llm-providers-comparison.md)."
      echo
      echo "Latest campaign: ${latest_day} · ${model_count} models · ${count} archived reports."
    fi
    echo
    render_model_table "$lang" "${REPORT_URL}/"
  } > "$target.$$"
  mv -f "$target.$$" "$target"
  echo "$target"
}

if [[ "$ROOT" == "$KIT_DIR" && -d "$DOCS_DIR/reference" && -d "$DOCS_DIR/fr/reference" && "$count" -gt 0 ]]; then
  write_docs_page en "$DOCS_DIR/reference/llm-campaign-results.md"
  write_docs_page fr "$DOCS_DIR/fr/reference/llm-campaign-results.md"
fi
