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
reasoning_rows=""
matrix_rows=""
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

  # One line per reasoning step (M15 to M17), for the reasoning table: absent from older reports.
  reasoning_rows+=$(printf '%s' "$json" | jq -r '. as $c | (.modes[]? | select(.mode != "M18" and ((.reasoning // []) | length > 0)) | .reasoning[])
    | [$c.provider, $c.model, $c.timestampUtc, .step, .outcome, (.sentValue // "-"), (if (.trace // false) then "orkeon" elif (.vendorTrace // false) then "wire" else "none" end), (.tableCheck // "-")] | @tsv')
  [[ -z "$reasoning_rows" || "$reasoning_rows" == *$'\n' ]] || reasoning_rows+=$'\n'

  # One line per M18 step (the raw vendor matrix), for its own table: M18 steps are vendor
  # fragments with their own labels, never columns of the reasoning grid above.
  matrix_rows+=$(printf '%s' "$json" | jq -r '. as $c | (.modes[]? | select(.mode == "M18" and ((.reasoning // []) | length > 0)) | .reasoning[])
    | [$c.provider, $c.model, $c.timestampUtc, .step, .outcome, (if (.trace // false) then "orkeon" elif (.vendorTrace // false) then "wire" else "none" end)] | @tsv')
  [[ -z "$matrix_rows" || "$matrix_rows" == *$'\n' ]] || matrix_rows+=$'\n'

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
# of the model's latest campaign day, the latest result of each mode winning: reports of the
# same model (for example a D8 run made separately) are merged, a vision companion is a row of
# its own, and a day older than the model's latest never mixes in. The report linked is that day's with the most modes.
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

# The reasoning steps of each model's latest campaign day, as TSV: provider, label, model, day,
# then the rest of the Markdown row (so no field is ever empty). Same merge rule as the model
# table: the reports of the latest day are merged, the latest result of each step winning.
# Columns: on off low medium high xhigh max budget; any other step is a vendor value, listed in
# Other as "step cell"; Trace is ✅ when the provider surfaced a reasoning trace on any step of
# the day, "wire only" when the vendor sent one that the provider did not surface; Table is ✅ when every
# step the run checked against M18 agrees with the capability table, else the steps that contradict it.
reasoning_table_tsv() {
  local providers
  providers=$(jq -c '.providers | with_entries(select(.value.aliasOf == null)) | map_values(.label // "")' "$CATALOG")
  printf '%s' "$reasoning_rows" | jq -R -s -r --argjson cat "$providers" '
    def cell(o; v): if o == "sent" then "✅" elif o == "lowered" then "→\(v)" elif o == "omitted" then "➖" else "❌" end;
    split("\n") | map(select(length > 0) | split("\t")
      | {p: .[0], m: .[1], s: .[2], step: .[3], o: .[4], v: .[5], t: .[6], c: (.[7] // "-")})
    | group_by([.p, .m])
    | map((map(.s[0:10]) | max) as $d
        | map(select(.s[0:10] == $d)) | sort_by(.s)
        | (reduce .[] as $x ({}; .[$x.step] = cell($x.o; $x.v))) as $res
        | (reduce .[] as $x ({}; .[$x.step] = $x.c)) as $chk
        | ([$chk | to_entries[] | select(.value == "missing restriction" or .value == "too narrow") | "\(.key): \(.value)"]) as $bad
        | {p: .[0].p, m: .[0].m, d: $d, l: (($cat[.[0].p] // "") | if . == "" then .[0].p else . end),
           res: $res, table: (if ($bad | length) > 0 then "❌ " + ($bad | join(", ")) elif any($chk[]; . == "agrees") then "✅" else "" end),
           trace: (if any(.[]; .t == "orkeon" or .t == "true") then "✅" elif any(.[]; .t == "wire") then "wire only" else "" end)})
    | sort_by(.p, .m)
    | .[] | . as $r
    | [$r.p, ($r.l // $r.p), $r.m, $r.d,
       ("| " + ([ ("on","off","low","medium","high","xhigh","max","budget") as $k | ($r.res[$k] // "") ] | join(" | "))
        + " | " + ([$r.res | to_entries[] | select(.key as $k | ["on","off","low","medium","high","xhigh","max","budget"] | index($k) | not)
                   | "\(.key) \(.value)"] | join(", "))
        + " | " + $r.trace + " | " + $r.table + " |")] | @tsv'
}

# The vendor reasoning matrix (M18), as TSV: provider, label, model, day, then the rest of the
# Markdown row. One row per model, from the latest report that ran M18 (not merged across days:
# the matrix is one run of one fragment list). Accepted and Refused list the step labels, Trace
# the labels whose step returned a reasoning trace; a label is code-formatted.
matrix_table_tsv() {
  local providers
  providers=$(jq -c '.providers | with_entries(select(.value.aliasOf == null)) | map_values(.label // "")' "$CATALOG")
  printf '%s' "$matrix_rows" | jq -R -s -r --argjson cat "$providers" '
    def codes(f): [.[] | select(f) | "`" + .step + "`"] | join(", ");
    split("\n") | map(select(length > 0) | split("\t")
      | {p: .[0], m: .[1], s: .[2], step: .[3], o: .[4], t: (.[5] == "orkeon" or .[5] == "wire" or .[5] == "true")})
    | group_by([.p, .m])
    | map((map(.s) | max) as $latest | map(select(.s == $latest))
        | {p: .[0].p, m: .[0].m, d: $latest[0:10], l: (($cat[.[0].p] // "") | if . == "" then .[0].p else . end),
           acc: codes(.o == "accepted"), ref: codes(.o == "refused"), tr: codes(.t)})
    | sort_by(.p, .m)
    | .[] | [.p, .l, .m, .d, "| \(.acc) | \(.ref) | \(.tr) |"] | @tsv'
}

# Renders the vendor reasoning matrix, or one sentence when no report ran M18 yet.
render_matrix_table() {
  local lang="$1" previous="" p label model d rest cell
  if [[ -z "$matrix_table" ]]; then
    if [[ "$lang" == fr ]]; then
      echo "Aucun rapport archivé ne porte encore la matrice brute (mode M18) : le tableau apparaîtra à la prochaine campagne qui l'exécute."
    else
      echo "No archived report carries the raw matrix yet (mode M18): the table appears with the next campaign that runs it."
    fi
    return 0
  fi
  if [[ "$lang" == fr ]]; then
    echo "| Fournisseur | Modèle | Date | Acceptés | Refusés | Trace |"
  else
    echo "| Provider | Model | Date | Accepted | Refused | Trace |"
  fi
  echo "|---|---|---|---|---|---|"
  while IFS=$'\t' read -r p label model d rest; do
    cell=""
    [[ "$p" == "$previous" ]] || cell="$label"
    previous="$p"
    echo "| ${cell} | \`${model}\` | ${d} ${rest}"
  done <<< "$matrix_table"
}

# Renders the reasoning table, or one sentence when no report carries reasoning steps yet.
render_reasoning_table() {
  local lang="$1" previous="" p label model d rest cell
  if [[ -z "$reasoning_table" ]]; then
    if [[ "$lang" == fr ]]; then
      echo "Aucun rapport archivé ne porte encore d'étapes de raisonnement (modes M15 à M17) : le tableau apparaîtra à la prochaine campagne qui les exécute."
    else
      echo "No archived report carries reasoning steps yet (modes M15 to M17): the table appears with the next campaign that runs them."
    fi
    return 0
  fi
  if [[ "$lang" == fr ]]; then
    echo "| Fournisseur | Modèle | Date | on | off | low | medium | high | xhigh | max | budget | Autres | Trace | Table |"
  else
    echo "| Provider | Model | Date | on | off | low | medium | high | xhigh | max | budget | Other | Trace | Table |"
  fi
  echo "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|"
  while IFS=$'\t' read -r p label model d rest; do
    cell=""
    [[ "$p" == "$previous" ]] || cell="$label"
    previous="$p"
    echo "| ${cell} | \`${model}\` | ${d} ${rest}"
  done <<< "$reasoning_table"
}

model_table=""
[[ "$count" -eq 0 ]] || model_table=$(model_table_tsv)
latest_day=$(printf '%s' "$model_table" | cut -f5 | sort | tail -n 1)
reasoning_table=""
[[ -z "$reasoning_rows" ]] || reasoning_table=$(reasoning_table_tsv)
matrix_table=""
[[ -z "$matrix_rows" ]] || matrix_table=$(matrix_table_tsv)
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
    echo "reports of the same model are merged; a vision companion is a row of its own). The same table is"
    echo "published in the documentation, \`docs/reference/llm-campaign-results.md\`."
    echo
    render_model_table en ""
    echo
    echo "## Reasoning per model"
    echo
    echo "One row per model that ran the reasoning modes (M15 thinking toggle, M16 effort sweep, M17 budget),"
    echo "from the model's latest campaign day. A cell is ✅ when the value was sent as asked, \`→low\` when"
    echo "Orkeon lowered it to the nearest value the model takes, ➖ when it was left out, ❌ when the vendor"
    echo "refused what Orkeon sent, and empty when the step was not run. \`Other\` lists vendor values outside"
    echo "the usual set (\`none ✅\`); \`Trace\` is ✅ when the provider surfaced a reasoning trace, \`wire only\` when the vendor sent one the provider dropped."
    echo "\`Table\` checks the model capability table against the vendor's own answer to the same value (M18):"
    echo "✅ every checked step agrees, otherwise the steps where the table is missing a restriction (the vendor"
    echo "refused what Orkeon sent) or too narrow (Orkeon lowered or left out a value the vendor takes)."
    echo
    render_reasoning_table en
    echo
    echo "## Vendor reasoning matrix"
    echo
    echo "M18 sends each fragment of the catalog's \`rawReasoning\` list to the vendor exactly as written,"
    echo "bypassing Orkeon's own judgement of what a model supports: the table shows what the vendor itself"
    echo "accepts, from the model's latest report that ran M18. \"Accepted\" means HTTP 2xx, not that the value"
    echo "had an effect: compare the traces and the completion tokens in the reports. \`Trace\` lists the"
    echo "fragments after which the vendor answer carried a reasoning trace, read on the wire."
    echo
    render_matrix_table en
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
      echo "modèle : seuls les rapports d'un même modèle sont réunis ; un compagnon vision est une ligne à part."
      echo "Les jours plus anciens sont dans le [journal des campagnes](${REPORT_URL}/README.md#campaigns-run)."
      echo
      echo "Les modes M1 à M18 sont ceux du chat, D1 à D8 ceux des modèles de décision ; ils sont définis dans"
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
      echo "each mode. A row merges the reports of the model's latest campaign day: only reports of the"
      echo "same model are merged, and a vision companion is a row of its own. Older days are in the"
      echo "[campaign journal](${REPORT_URL}/README.md#campaigns-run)."
      echo
      echo "Modes M1 to M18 are the chat modes, D1 to D8 the decision-model modes; they are defined in the"
      echo "[kit README](${REPORT_URL}/README.md#the-protocol-modes). ✅ passed, ❌ failed, ➖ not"
      echo "applicable (the provider or the model does not declare the capability). The default models and"
      echo "why they were chosen: [LLM provider comparison](llm-providers-comparison.md)."
      echo
      echo "Latest campaign: ${latest_day} · ${model_count} models · ${count} archived reports."
    fi
    echo
    render_model_table "$lang" "${REPORT_URL}/"
    echo
    if [[ "$lang" == fr ]]; then
      echo "## Raisonnement par modèle"
      echo
      echo "Les modes M15 (bascule du raisonnement), M16 (balayage des efforts) et M17 (budget) sont décrits dans le"
      echo "[README du kit](${REPORT_URL}/README.md#the-protocol-modes). ✅ la valeur est partie telle quelle,"
      echo "\`→low\` Orkeon l'a abaissée à la valeur la plus proche que le modèle accepte, ➖ rien n'a été envoyé,"
      echo "❌ le fournisseur a refusé ce qu'Orkeon a envoyé, vide l'étape n'a pas été exécutée. \`Autres\` liste les"
      echo "valeurs propres au fournisseur ; \`Trace\` est ✅ lorsque le provider Orkeon a restitué une trace de raisonnement, \`wire only\` lorsque le fournisseur l'a envoyée et que le provider l'a perdue."
      echo "\`Table\` confronte la table des capacités des modèles à la réponse du fournisseur à la même valeur (M18) :"
      echo "✅ toutes les étapes vérifiées concordent, sinon les étapes où la table manque une restriction (le fournisseur"
      echo "a refusé ce qu'Orkeon a envoyé) ou est trop étroite (Orkeon a abaissé ou omis une valeur que le fournisseur accepte)."
    else
      echo "## Reasoning per model"
      echo
      echo "Modes M15 (thinking toggle), M16 (effort sweep) and M17 (budget) are described in the"
      echo "[kit README](${REPORT_URL}/README.md#the-protocol-modes). ✅ the value was sent as asked,"
      echo "\`→low\` Orkeon lowered it to the nearest value the model takes, ➖ nothing was sent, ❌ the vendor"
      echo "refused what Orkeon sent, empty the step was not run. \`Other\` lists vendor-specific values;"
      echo "\`Trace\` is ✅ when the provider surfaced a reasoning trace, \`wire only\` when the vendor sent one the provider dropped."
      echo "\`Table\` checks the model capability table against the vendor's own answer to the same value (M18):"
      echo "✅ every checked step agrees, otherwise the steps where the table is missing a restriction (the vendor"
      echo "refused what Orkeon sent) or too narrow (Orkeon lowered or left out a value the vendor takes)."
    fi
    echo
    render_reasoning_table "$lang"
    echo
    if [[ "$lang" == fr ]]; then
      echo "## Matrice brute du fournisseur"
      echo
      echo "M18 envoie chaque fragment de la liste \`rawReasoning\` du catalogue au fournisseur tel quel, sans"
      echo "passer par le jugement d'Orkeon sur ce qu'un modèle accepte : le tableau montre ce que le"
      echo "fournisseur lui-même accepte, d'après le dernier rapport du modèle qui a exécuté M18. « Acceptés »"
      echo "signifie HTTP 2xx, pas que la valeur ait eu un effet : comparer les traces et les jetons de"
      echo "complétion dans les rapports. \`Trace\` liste les fragments après lesquels une trace de"
      echo "raisonnement est revenue dans la réponse du fournisseur, lue sur le fil."
    else
      echo "## Vendor reasoning matrix"
      echo
      echo "M18 sends each fragment of the catalog's \`rawReasoning\` list to the vendor exactly as written,"
      echo "bypassing Orkeon's own judgement of what a model supports: the table shows what the vendor itself"
      echo "accepts, from the model's latest report that ran M18. \"Accepted\" means HTTP 2xx, not that the value"
      echo "had an effect: compare the traces and the completion tokens in the reports. \`Trace\` lists the"
      echo "fragments after which the vendor answer carried a reasoning trace, read on the wire."
    fi
    echo
    render_matrix_table "$lang"
  } > "$target.$$"
  mv -f "$target.$$" "$target"
  echo "$target"
}

if [[ "$ROOT" == "$KIT_DIR" && -d "$DOCS_DIR/reference" && -d "$DOCS_DIR/fr/reference" && "$count" -gt 0 ]]; then
  write_docs_page en "$DOCS_DIR/reference/llm-campaign-results.md"
  write_docs_page fr "$DOCS_DIR/fr/reference/llm-campaign-results.md"
fi
