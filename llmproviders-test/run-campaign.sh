#!/usr/bin/env bash
# Runs the LLM provider test protocol against real APIs and archives the proof.
#
# Usage:
#   run-campaign.sh --provider openai --model gpt-5.6-sol
#   run-campaign.sh --provider openai --model 'gpt-5.6-*' --max-models 3
#   run-campaign.sh --provider deepseek,zai,ollama --parallel
#   run-campaign.sh --all --config providers.local.json
#   run-campaign.sh --all --config providers.local.json --dry-run
#
# These campaigns spend real credits on real accounts. --dry-run resolves everything and
# prints the plan without placing a single call; use it first.
#
# API keys are never accepted as an argument — the harness refuses them there and this
# script does not work around it. A key arrives through the environment, or through the
# `apiKey` field of a configuration file this script refuses to read unless it is named
# *.local.json or *.secrets.json (both gitignored).
set -euo pipefail

KIT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$KIT_DIR/.." && pwd)"
CLI_PROJECT="$REPO_ROOT/src/scripting/Orkeon.Scripting.Cli/Orkeon.Scripting.Cli.csproj"
CATALOG="$KIT_DIR/lib/catalog.json"

PROVIDER=""
ALL=0
MODEL=""
MODES=""
CONFIG=""
BASE_URL=""
API_VERSION=""
WORKSPACE_ID=""
API_KEY_ENV=""
MAX_MODELS=""
DRY_RUN=0
NO_RECAP=0
PARALLEL=0
OUT="$KIT_DIR"
CONFIGURATION="Debug"
# Empty means "let the harness decide": the probe already defaults to 180 s and temperature 0,
# and duplicating those numbers here would let the two drift apart silently.
TIMEOUT=""
TEMPERATURE=""

DEFAULT_MAX_MODELS=5
# M11 (long context) and M14 (end-to-end crew) are manual on purpose — see the kit README.
ALL_MODES="M1,M2,M3,M4,M5,M6,M7,M8,M9,M10,M12,M13"

log()  { printf '%s\n' "$*" >&2; }
warn() { printf '⚠  %s\n' "$*" >&2; }
die()  { printf 'run-campaign: %s\n' "$*" >&2; exit 2; }

usage() {
  cat <<'HELP'
Runs the LLM provider test protocol against real APIs and archives the proof.

Usage:
  run-campaign.sh --provider openai --model gpt-5.6-sol
  run-campaign.sh --provider openai --model 'gpt-5.6-*' --max-models 3
  run-campaign.sh --provider deepseek,zai,ollama --parallel
  run-campaign.sh --all --config providers.local.json
  run-campaign.sh --all --config providers.local.json --dry-run

Options:
  -p, --provider <keys>  one key, or several separated by commas
                         openai | anthropic | ollama | azure | groq | together | qwen
                         | deepseek | kimi | mistral | huggingface | zai
      --all              every provider declared in the configuration (needs --config)
      --parallel         run the selected providers concurrently. Each provider's own models
                         still run one after another: they share its rate limit, and racing
                         them would measure the throttle rather than the protocol. Output is
                         buffered per provider and printed in the order you named them, so a
                         parallel run reads exactly like a sequential one.
  -m, --model <id|glob>  a model identifier, or a pattern such as 'gpt-5.6-*'
      --modes M1,M8      default: every mode the harness supports
  -c, --config <file>    campaign configuration — see providers.schema.json
  -u, --base-url <url>   endpoint override (Azure, regional mirrors)
      --api-version <v>  Azure api-version, deployment mode only
      --workspace-id <w> workspace id for workspace-scoped keys (Anthropic identity-linked)
  -k, --api-key-env <V>  name of the environment variable holding the API key
      --max-models <n>   cap on how far a wildcard may expand (default 5)
      --dry-run          resolve and print the plan; place no call, write nothing
      --no-recap         skip the index rebuild — for parallel runs; rebuild once at the end
                         with lib/recap.sh <out>
      --timeout <s>      per-request timeout, default 180 (a cold local model must load first)
      --temperature <t>  sampling temperature, default 0 — pinned so a verdict is reproducible.
                         Raise it only for a model that rejects a pinned temperature.
  -o, --out <dir>        report root (default: this directory)

These campaigns spend real credits on real accounts. --dry-run resolves everything and
prints the plan without placing a single call; use it first.

API keys are never accepted as an argument — the harness refuses them there and this
script does not work around it. A key arrives through the environment, or through the
`apiKey` field of a configuration file this script refuses to read unless it is named
*.local.json or *.secrets.json (both gitignored).
HELP
}

# ── Arguments ────────────────────────────────────────────────────────────────

while [[ $# -gt 0 ]]; do
  case "$1" in
    -p|--provider)    PROVIDER="$2"; shift 2 ;;
    --all)            ALL=1; shift ;;
    -m|--model)       MODEL="$2"; shift 2 ;;
    --modes)          MODES="$2"; shift 2 ;;
    -c|--config)      CONFIG="$2"; shift 2 ;;
    -u|--base-url)    BASE_URL="$2"; shift 2 ;;
    --api-version)    API_VERSION="$2"; shift 2 ;;
    --workspace-id)   WORKSPACE_ID="$2"; shift 2 ;;
    -k|--api-key-env) API_KEY_ENV="$2"; shift 2 ;;
    --max-models)     MAX_MODELS="$2"; shift 2 ;;
    --dry-run)        DRY_RUN=1; shift ;;
    --no-recap)       NO_RECAP=1; shift ;;
    --parallel)       PARALLEL=1; shift ;;
    --timeout)        TIMEOUT="$2"; shift 2 ;;
    --temperature)    TEMPERATURE="$2"; shift 2 ;;
    -o|--out)         OUT="$2"; shift 2 ;;
    --configuration)  CONFIGURATION="$2"; shift 2 ;;
    -h|--help)        usage; exit 0 ;;
    *)                die "unknown option '$1' (try --help)" ;;
  esac
done

[[ -n "$PROVIDER" || "$ALL" -eq 1 ]] || die "pass --provider <key> or --all (try --help)"
[[ -z "$PROVIDER" || "$ALL" -eq 0 ]] || die "--provider and --all are mutually exclusive"
[[ "$ALL" -eq 0 || -n "$CONFIG" ]]   || die "--all needs --config: the provider list comes from it"

command -v jq >/dev/null 2>&1 || die "jq is required (used to read the campaign JSON)."

# A dry run promises to write nothing, and creating the output tree would already break that
# promise on a fresh --out. Normalise the path without touching the filesystem instead.
if [[ "$DRY_RUN" -eq 1 ]]; then
  OUT="$(cd "$(dirname "$OUT")" 2>/dev/null && pwd || printf '%s' "$(dirname "$OUT")")/$(basename "$OUT")"
else
  OUT="$(mkdir -p "$OUT" && cd "$OUT" && pwd)"
fi

# ── Configuration file ───────────────────────────────────────────────────────

# A configuration carrying a literal key must be named so that .gitignore catches it. This
# guard is the difference between a convention and a rule.
if [[ -n "$CONFIG" ]]; then
  [[ -f "$CONFIG" ]] || die "configuration file not found: $CONFIG"
  jq -e . "$CONFIG" >/dev/null 2>&1 || die "configuration file is not valid JSON: $CONFIG"

  if jq -e '[.providers[]? | select(has("apiKey"))] | length > 0' "$CONFIG" >/dev/null; then
    case "$(basename "$CONFIG")" in
      *.local.json|*.secrets.json) ;;
      *) die "$CONFIG declares a literal apiKey but is not named *.local.json or *.secrets.json — \
that name is what keeps it out of git. Rename it, or switch to apiKeyEnv." ;;
    esac
  fi
fi

# Reads a value from the config: provider-level, then defaults, then the supplied fallback.
cfg() {
  local provider="$1" field="$2" fallback="${3:-}"
  local value=""
  if [[ -n "$CONFIG" ]]; then
    value=$(jq -r --arg p "$provider" --arg f "$field" \
      '(.providers[$p][$f] // .defaults[$f] // empty) | if type == "array" then join(",") else tostring end' \
      "$CONFIG")
  fi
  printf '%s' "${value:-$fallback}"
}

# The variable each vendor's own SDK reads, resolved through the catalogue's alias table.
#
# The built-in default used to be ORKEON_LLM_API_KEY for every provider, so any run without
# a --config looked for a variable nobody exports. The probe refused and said so — on a
# stderr the kit discarded — and the campaign was archived as an empty report
# (deepseek-v4-pro, 2026-08-02). A default that is wrong for all twelve providers is not a
# default. These are guesses in the sense that any convention is one, and `-k` or the
# configuration still wins; what matters is that the guess is right often enough that the
# common case needs no flag.
catalog_field() {
  local provider="$1" field="$2" canonical
  [[ -f "$CATALOG" ]] || return 0
  canonical=$(jq -r --arg p "$provider" '.providers[$p].aliasOf // $p' "$CATALOG")
  jq -r --arg p "$canonical" --arg f "$field" '.providers[$p][$f] // empty' "$CATALOG"
}

catalog_key_env() { catalog_field "$1" "apiKeyEnv"; }

# A parameter value one MODEL demands, from the per-model registry. Per model and not per
# provider on purpose: gpt-5.6-sol demands temperature 1 and reasoning_effort none while
# gpt-4o-mini, same provider, rejects reasoning_effort outright (both measured 2026-08-30).
# A provider-level pin was exactly one campaign away from breaking the other model.
catalog_model_param() {
  local provider="$1" model="$2" field="$3" canonical
  [[ -f "$CATALOG" ]] || return 0
  canonical=$(jq -r --arg p "$provider" '.providers[$p].aliasOf // $p' "$CATALOG")
  jq -r --arg p "$canonical" --arg m "$model" --arg f "$field"     '.providers[$p].requiredParams[$m][$f] // empty' "$CATALOG"
}

# ── The orkeon CLI ───────────────────────────────────────────────────────────

# Resolved on first use, not up front: a dry run that only expands literal model names never
# needs the CLI, and building it would be a side effect on a run that promised none.
ORKEON=()

resolve_orkeon() {
  [[ ${#ORKEON[@]} -eq 0 ]] || return 0

  if [[ -n "${ORKEON_BIN:-}" ]]; then ORKEON=("$ORKEON_BIN"); return 0; fi
  if command -v orkeon >/dev/null 2>&1; then ORKEON=(orkeon); return 0; fi

  local dll="$REPO_ROOT/src/scripting/Orkeon.Scripting.Cli/bin/$CONFIGURATION/net10.0/orkeon.dll"
  if [[ ! -f "$dll" ]]; then
    log "Building the orkeon CLI once ($CONFIGURATION) …"
    dotnet build "$CLI_PROJECT" -c "$CONFIGURATION" -v q --nologo >&2
  fi
  [[ -f "$dll" ]] || die "could not locate or build the orkeon CLI. Set ORKEON_BIN to point at it."
  ORKEON=(dotnet "$dll")
}

COMMIT="$(git -C "$REPO_ROOT" rev-parse --short HEAD 2>/dev/null || echo "")"

# ── Model resolution ─────────────────────────────────────────────────────────

# Live catalogue first, declared list as the fallback. A hand-maintained list is exactly
# what let four providers ship a retired default model for months.
resolve_models() {
  local provider="$1" pattern="$2" key_env="$3" base_url="$4"
  local declared discovered

  declared=$(cfg "$provider" "models" | tr ',' '\n' | grep -v '^$' || true)

  if [[ -z "$pattern" ]]; then
    if [[ -n "$declared" ]]; then
      printf '%s\n' "$declared"
    else
      # Neither --model nor a configuration entry: fall back to the catalogue's own default
      # for this provider. Without it, `--provider a,b,c` resolved nothing and the whole
      # point of naming several providers at once was lost to a per-provider -m.
      #
      # These identifiers come from the matrix sections 6.x, not from the providers'
      # compiled-in defaults — six of those are flagged retired or wrong (G-01..G-04, G-07,
      # G-08), so inheriting them would hand every second campaign a model its own API no
      # longer serves. Azure declares none on purpose: deployments are account-specific.
      catalog_field "$provider" "defaultModel"
    fi
    return
  fi

  if [[ "$pattern" != *"*"* && "$pattern" != *"?"* ]]; then
    printf '%s\n' "$pattern"
    return
  fi

  resolve_orkeon
  local args=(llm models -p "$provider" --filter "$pattern" -k "$key_env")
  [[ -n "$base_url" ]] && args+=(-u "$base_url")

  # A catalogue listing is one identifier per line and identifiers never contain whitespace.
  # Anything else is a diagnostic that leaked into the stream, and treating it as a model
  # name would launch a campaign against nonsense.
  if discovered=$("${ORKEON[@]}" "${args[@]}" 2>/dev/null | grep -vE '\s' || true) \
     && [[ -n "$discovered" ]]; then
    printf '%s\n' "$discovered"
    return
  fi

  warn "$provider: catalogue lookup failed — falling back to the models declared in the configuration."
  [[ -n "$declared" ]] || return

  # bash's own pattern matching, not a hand-rolled glob-to-regex translation: translating by
  # hand needs `?` escaped as a literal in one pass and re-read as a metacharacter in the
  # next, and gets it wrong. Case-insensitive to match what the CLI's own filter does.
  local candidate
  shopt -s nocasematch
  while IFS= read -r candidate; do
    # shellcheck disable=SC2053  # unquoted on purpose: $pattern is a glob, not a literal.
    [[ "$candidate" == $pattern ]] && printf '%s\n' "$candidate"
  done <<< "$declared"
  shopt -u nocasematch
}

# ── One campaign ─────────────────────────────────────────────────────────────

FAILURES=0
RAN=0
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

run_one() {
  local provider="$1" model="$2" modes="$3" key_env="$4" base_url="$5" api_version="$6" workspace_id="$7"
  local slug stamp target raw err args status

  slug=$(printf '%s' "$model" | tr -c 'A-Za-z0-9._-' '_')
  stamp=$(date -u +%Y-%m-%d-%H%M%S)
  target="$OUT/$provider/$stamp-$slug.md"

  if [[ "$DRY_RUN" -eq 1 ]]; then
    printf '  would run  %-12s %-42s modes=%s  →  %s\n' \
      "$provider" "$model" "$modes" "${target#"$OUT"/}"
    return 0
  fi

  resolve_orkeon
  args=(llm probe -p "$provider" -m "$model" --modes "$modes" --format json -k "$key_env")
  [[ -n "$base_url" ]] && args+=(-u "$base_url")
  [[ -n "$api_version" ]] && args+=(--api-version "$api_version")
  [[ -n "$workspace_id" ]] && args+=(--workspace-id "$workspace_id")
  [[ -n "$COMMIT" ]] && args+=(--commit "$COMMIT")
  [[ -n "$TIMEOUT" ]] && args+=(--timeout "$TIMEOUT")

  # Temperature 0 is the default because a pinned temperature makes a verdict reproducible.
  # Some models refuse it outright — `kimi-k2.6` answers every call with
  # `invalid temperature: only 1 is allowed for this model`, which cost a whole campaign
  # (2026-08-03: ten of twelve modes red, one cause). A provider that cannot take 0 declares
  # what it can take in the catalogue, and the report prints the value it actually used, so a
  # verdict is never silently less reproducible than it looks.
  local temperature="$TEMPERATURE"
  [[ -z "$temperature" ]] && temperature=$(catalog_model_param "$provider" "$model" "temperature")
  [[ -n "$temperature" ]] && args+=(--temperature "$temperature")

  # Same shape for the base thinking effort: gpt-5.6-sol refuses function tools on
  # chat/completions unless reasoning is explicitly off (2026-08-30). Declared per model in
  # the catalogue's requiredParams, printed by the report header; M7 keeps probing thinking
  # with its own explicit effort.
  local thinking_effort
  thinking_effort=$(catalog_model_param "$provider" "$model" "thinkingEffort")
  [[ -n "$thinking_effort" ]] && args+=(--thinking-effort "$thinking_effort")

  # M7's own effort, where a model's supported set excludes the default "low"
  # (mistral-medium-2604: only high or none, 2026-08-30).
  local m7_effort
  m7_effort=$(catalog_model_param "$provider" "$model" "m7Effort")
  [[ -n "$m7_effort" ]] && args+=(--m7-effort "$m7_effort")

  raw="$TMP_DIR/$provider-$slug.json"
  err="$TMP_DIR/$provider-$slug.stderr"
  log "▶ $provider / $model  [$modes]"

  # The probe exits non-zero when a mode fails. That is a result, not a crash: capture the
  # output either way, and let the report say what broke.
  set +e
  "${ORKEON[@]}" "${args[@]}" > "$raw" 2>"$err"
  status=$?
  set -e

  # Always returns 0: a broken campaign is recorded in FAILURES, not raised. Under `set -e`
  # a non-zero return here would abort the whole run, so one unreachable provider would cost
  # us every provider after it in a --all sweep.
  #
  # The emptiness check is not redundant with the parse: on jq 1.6 an empty file is zero
  # inputs, so `jq -e .` prints nothing and exits 0 — the guard waved through a probe that
  # had written nothing at all, and the campaign was archived as a report with every field
  # blank and a ✅ in its journal fragment (deepseek-v4-pro, 2026-08-02). jq 1.7 returns 4
  # for the same file, so the hole was version-dependent on top of being silent. Asserting
  # on the fields the report actually reads is what closes it for good.
  if [[ ! -s "$raw" ]] || ! jq -e 'has("modes") and has("passed")' "$raw" >/dev/null 2>&1; then
    warn "$provider / $model: the probe produced no usable JSON (exit $status)."
    if [[ -s "$err" ]]; then sed 's/^/    /' "$err" >&2; fi
    FAILURES=$((FAILURES + 1))
    return 0
  fi

  "$KIT_DIR/lib/report.sh" "$raw" "$target" >/dev/null
  RAN=$((RAN + 1))

  local passed failed skipped
  passed=$(jq -r '.passed' "$raw"); failed=$(jq -r '.failed' "$raw"); skipped=$(jq -r '.notApplicable' "$raw")
  log "  → $passed ✅  $failed ❌  $skipped ➖   ${target#"$OUT"/}"
  [[ "$failed" -eq 0 ]] || FAILURES=$((FAILURES + 1))
  return 0
}

run_provider() {
  local provider="$1"
  local modes key_env base_url api_version workspace_id cap models key_value

  # Command line wins over the configuration, which wins over the built-in default — the
  # usual precedence, so an operator can override one provider without editing the file.
  modes="${MODES:-$(cfg "$provider" "modes" "$ALL_MODES")}"
  key_env="${API_KEY_ENV:-$(cfg "$provider" "apiKeyEnv" "$(catalog_key_env "$provider")")}"
  key_env="${key_env:-ORKEON_LLM_API_KEY}"
  base_url="${BASE_URL:-$(cfg "$provider" "baseUrl")}"
  api_version="${API_VERSION:-$(cfg "$provider" "apiVersion")}"
  workspace_id="${WORKSPACE_ID:-$(cfg "$provider" "workspaceId")}"
  cap="${MAX_MODELS:-$(cfg "$provider" "maxModels" "$DEFAULT_MAX_MODELS")}"

  # A literal key from the configuration is exported for the child process only: never onto
  # a command line, where `ps` would show it to every user on the machine.
  key_value=""
  if [[ -n "$CONFIG" ]]; then
    key_value=$(jq -r --arg p "$provider" '.providers[$p].apiKey // empty' "$CONFIG")
  fi
  if [[ -n "$key_value" ]]; then
    export "$key_env=$key_value"
  fi

  mapfile -t models < <(resolve_models "$provider" "$MODEL" "$key_env" "$base_url")

  if [[ ${#models[@]} -eq 0 ]]; then
    warn "$provider: no model resolved — declare one under providers.$provider.models, or pass --model."
    FAILURES=$((FAILURES + 1))
    [[ -z "$key_value" ]] || unset "$key_env"
    return
  fi

  if [[ ${#models[@]} -gt $cap ]]; then
    # Say what was dropped. A silent cap reads as full coverage.
    warn "$provider: ${#models[@]} models matched, capping at $cap (--max-models). Not exercised: ${models[*]:$cap}"
    models=("${models[@]:0:$cap}")
  fi

  for model in "${models[@]}"; do
    run_one "$provider" "$model" "$modes" "$key_env" "$base_url" "$api_version" "$workspace_id"
  done

  # A vision companion run, when the provider's default model cannot see and it declares one
  # that can. This is the practical half of D-03: capabilities are declared per provider,
  # reality is per model, so a single default model can never answer M9 for a provider whose
  # sight lives in a different identifier. Measured twice on 2026-08-02 — `glm-5.2` returns
  # code 1210 while `glm-4.6v-flash` reads the image, and `llama3.2` refuses multimodal while
  # `llava` reads it.
  #
  # The default model still runs M9 and still scores its red: that red *is* the D-03 evidence
  # and deleting it would hide the mismatch the matrix exists to track. The companion adds the
  # complementary fact — that Orkeon's own multimodal path works — which no single run gives.
  #
  # Only on the automatic path. An explicit --model is a choice, and second-guessing it would
  # spend credits the caller did not ask to spend.
  if [[ -z "$MODEL" && ",$modes," == *",M9,"* ]]; then
    local vision
    vision=$(catalog_field "$provider" "visionModel")
    if [[ -n "$vision" ]] && ! printf '%s\n' "${models[@]}" | grep -qxF "$vision"; then
      log "  ↳ $provider declares a vision model — running M9 on $vision as well"
      run_one "$provider" "$vision" "M9" "$key_env" "$base_url" "$api_version" "$workspace_id"
    fi
  fi

  [[ -z "$key_value" ]] || unset "$key_env"
}

# ── Drive ────────────────────────────────────────────────────────────────────

if [[ "$DRY_RUN" -eq 1 ]]; then
  log "Dry run — resolving the plan, placing no call."
  log ""
fi

if [[ "$ALL" -eq 1 ]]; then
  mapfile -t providers < <(jq -r '.providers | keys[]' "$CONFIG")
else
  # `--provider a,b,c`. Splitting here rather than asking the caller for one run per provider
  # keeps the campaign a single unit of work: one index rebuild, one exit status, one place
  # that knows what was attempted.
  IFS=',' read -r -a providers <<< "$PROVIDER"
  for i in "${!providers[@]}"; do
    providers[i]="${providers[i]#"${providers[i]%%[![:space:]]*}"}"
    providers[i]="${providers[i]%"${providers[i]##*[![:space:]]}"}"
  done
fi

for provider in "${providers[@]}"; do
  [[ -n "$provider" ]] || die "--provider has an empty entry: check for a stray comma"
done

# A dry run resolves catalogues and prints a plan; running that concurrently would only
# scramble the plan's order for no gain, since nothing is being waited on.
if [[ "$PARALLEL" -eq 1 && "$DRY_RUN" -eq 0 && ${#providers[@]} -gt 1 ]]; then
  # Build the CLI here, before forking. Left to the children, three of them would race to
  # produce the same assembly and collide in obj/ — and that failure would surface as a
  # provider error, sending the reader after the wrong thing entirely.
  resolve_orkeon

  pids=()
  for i in "${!providers[@]}"; do
    provider="${providers[i]}"
    # Indexed, not named: the same provider may legitimately appear twice with different
    # models, and two children writing one buffer would interleave into nonsense.
    (
      set +e
      run_provider "$provider" >"$TMP_DIR/par-$i.out" 2>&1
      printf '%s %s\n' "$RAN" "$FAILURES" >"$TMP_DIR/par-$i.tally"
    ) &
    pids+=("$!")
    log "▶ $provider — started"
  done

  log ""
  wait "${pids[@]}" 2>/dev/null || true

  # Replayed in the order the caller named them, so a parallel run reads like a sequential
  # one. Interleaving live would be honest about the timing and useless as a report.
  for i in "${!providers[@]}"; do
    [[ ! -s "$TMP_DIR/par-$i.out" ]] || cat "$TMP_DIR/par-$i.out" >&2
    if [[ -s "$TMP_DIR/par-$i.tally" ]]; then
      read -r ran failed < "$TMP_DIR/par-$i.tally"
      RAN=$((RAN + ran))
      FAILURES=$((FAILURES + failed))
    else
      # No tally means the child died before it could write one — a crash, not a failed mode.
      # Counting it as a failure is what keeps the exit status honest.
      warn "${providers[i]}: the campaign process ended without reporting a result."
      FAILURES=$((FAILURES + 1))
    fi
  done
else
  for provider in "${providers[@]}"; do
    run_provider "$provider"
  done
fi

if [[ "$DRY_RUN" -eq 1 ]]; then
  log ""
  log "Nothing was called and nothing was written."
  exit 0
fi

log ""
if [[ "$NO_RECAP" -eq 1 ]]; then
  # Two campaigns launched in parallel both finish holding a snapshot of the reports taken
  # before the other one landed. Whichever rebuilds last writes a complete but stale index.
  # --no-recap defers it so the caller rebuilds once, after both have finished.
  log "$RAN campaign(s) archived under $OUT — index NOT rebuilt (--no-recap)."
  log "Rebuild it once every parallel run has finished:  lib/recap.sh $OUT"
else
  "$KIT_DIR/lib/recap.sh" "$OUT" >/dev/null
  log "$RAN campaign(s) archived under $OUT — index rebuilt."
fi

if [[ "$FAILURES" -gt 0 ]]; then
  log "$FAILURES campaign(s) reported a failed mode. That is the point of the exercise; read the reports."
  exit 1
fi
