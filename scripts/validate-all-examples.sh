#!/usr/bin/env bash
#
# validate-all-examples.sh — dry-run every bundled example crew.
#
# Builds the `orkeon` CLI and the trading example runner ONCE, then runs each example's
# config.yaml through their `--validate` dry-run (resolve settings, build the host, load
# the crew with strict tool resolution — no LLM probe, no kickoff). The trading runner
# is used for the 03-finance-trading examples (they reference the trading tool pack);
# the `orkeon` CLI (`orkeon run <yaml> --validate`) is used for everything else.
#
# Output: a per-config OK/FAIL table plus a final tally. Exits non-zero if any FAIL.
#
# NOTE: the FULL 105-config sweep is meant for CI (examples-ci.yml), NOT for local dev
# runs. On a slow/virtiofs filesystem each --validate spends 1-7 min purely loading the
# host, so the whole set can take hours. Locally, use --sample N (or --configs) to smoke
# a handful of representative crews.
#
# Usage:
#   scripts/validate-all-examples.sh                 # full sweep (CI)
#   scripts/validate-all-examples.sh --sample 8      # ~1 config per category, up to N
#   scripts/validate-all-examples.sh --configs a,b   # exactly these configs
#
# Options:
#   --sample <N>             validate one config per category (in category order), up to N.
#   --configs <csv>          validate exactly this comma-separated list of config paths
#                            (absolute, repo-relative, or examples/-relative).
#
# Environment overrides:
#   VALIDATE_JOBS=<n>        parallel workers (default 4). Each --validate is I/O-wait
#                            bound, so overlapping them cuts wall time substantially.
#   VALIDATE_TIMEOUT=<sec>   per-config timeout (default 300).
#   VALIDATE_FILTER=<regex>  only validate configs whose path matches this regex.
#   VALIDATE_EXCLUDES=<csv>  comma/space/newline-separated substrings; any config whose
#                            path matches one is skipped (for interactive examples whose
#                            load genuinely needs a live console). Empty by default.
#   VALIDATE_SAMPLE=<N>      same as --sample N.
#   VALIDATE_SKIP_BUILD=1    reuse existing Release binaries (skip the build step).
#   VALIDATE_RESULT_DIR=<d>  keep per-config results in <d> (survives a killed parent).
#
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT"

JOBS="${VALIDATE_JOBS:-4}"
PER_TIMEOUT="${VALIDATE_TIMEOUT:-300}"
FILTER="${VALIDATE_FILTER:-}"
EXCLUDES="${VALIDATE_EXCLUDES:-}"
SAMPLE="${VALIDATE_SAMPLE:-}"
EXPLICIT_CONFIGS=""

ORK_PROJ="src/scripting/Orkeon.Scripting.Cli/Orkeon.Scripting.Cli.csproj"
TRD_PROJ="examples/runners/trading/Orkeon.Examples.Trading.Runner.csproj"
ORK_DLL="src/scripting/Orkeon.Scripting.Cli/bin/Release/net10.0/orkeon.dll"
TRD_DLL="examples/runners/trading/bin/Release/net10.0/Orkeon.Examples.Trading.Runner.dll"

# ---------------------------------------------------------------------------
# Worker mode: `validate-all-examples.sh __worker <config> <resultfile>`
# Validates a single config and writes "OK|FAIL<TAB>config<TAB>detail" to resultfile.
# ---------------------------------------------------------------------------
if [[ "${1:-}" == "__worker" ]]; then
    config="$2"
    resultfile="$3"

    # Trading crews load through the dedicated trading runner (`--config <yaml>`);
    # everything else validates through the `orkeon` CLI (`orkeon run <yaml> --validate`,
    # config passed positionally after the `run` verb).
    if [[ "$config" == *"/03-finance-trading/"* ]]; then
        cmd=(dotnet "$TRD_DLL" --config "$config" --validate)
    else
        cmd=(dotnet "$ORK_DLL" run "$config" --validate)
    fi

    # Capture combined output; --validate prints "VALIDATION OK:"/"VALIDATION FAILED:".
    out="$(timeout "$PER_TIMEOUT" "${cmd[@]}" 2>&1)"
    code=$?

    if [[ $code -eq 0 ]]; then
        detail="$(printf '%s\n' "$out" | grep -m1 'VALIDATION OK:' | sed 's/.*(\(.*\)).*/\1/')"
        printf 'OK\t%s\t%s\n' "$config" "$detail" > "$resultfile"
    elif [[ $code -eq 124 ]]; then
        printf 'FAIL\t%s\ttimeout after %ss\n' "$config" "$PER_TIMEOUT" > "$resultfile"
    else
        # First meaningful error line (missing tool, YAML error, ...).
        detail="$(printf '%s\n' "$out" | grep -m1 -iE 'error|not found|unknown|VALIDATION FAILED' | head -c 300)"
        [[ -z "$detail" ]] && detail="exit $code"
        printf 'FAIL\t%s\t%s\n' "$config" "$detail" > "$resultfile"
    fi
    exit 0
fi

# ---------------------------------------------------------------------------
# Main — parse CLI args (env vars already provide defaults).
# ---------------------------------------------------------------------------
while [[ $# -gt 0 ]]; do
    case "$1" in
        --sample)   SAMPLE="${2:-}"; shift 2 ;;
        --configs)  EXPLICIT_CONFIGS="${2:-}"; shift 2 ;;
        -h|--help)  grep -E '^#( |$)' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "ERROR: unknown argument '$1' (see --help)" >&2; exit 2 ;;
    esac
done

if [[ -n "$SAMPLE" && ! "$SAMPLE" =~ ^[0-9]+$ ]]; then
    echo "ERROR: --sample expects a positive integer, got '$SAMPLE'" >&2; exit 2
fi

echo "==> validate-all-examples (jobs=$JOBS, per-config timeout=${PER_TIMEOUT}s)"

if [[ "${VALIDATE_SKIP_BUILD:-0}" != "1" ]]; then
    echo "==> Building runners (Release)..."
    # YAML crews don't need esbuild; skip the scripting npm bootstrap during build.
    if ! dotnet build "$ORK_PROJ" -c Release --nologo -v quiet -p:SkipScriptingNpmInstall=true; then
        echo "ERROR: failed to build orkeon CLI" >&2; exit 3
    fi
    if ! dotnet build "$TRD_PROJ" -c Release --nologo -v quiet; then
        echo "ERROR: failed to build trading runner" >&2; exit 3
    fi
fi

if [[ ! -f "$ORK_DLL" || ! -f "$TRD_DLL" ]]; then
    echo "ERROR: runner binaries not found (build failed or VALIDATE_SKIP_BUILD set too early)" >&2
    echo "       orkeon CLI: $ORK_DLL" >&2
    echo "       trading   : $TRD_DLL" >&2
    exit 3
fi

# Collect the 105 bundled example configs (numbered category dirs only; the lone
# others/effect/.../fixtures/config.yaml is an unrelated test fixture).
mapfile -t ALL_CONFIGS < <(find examples -name config.yaml | grep -E 'examples/0[0-9]-' | sort)

# Normalise excludes into a newline list of substrings.
declare -a EXCL=()
if [[ -n "$EXCLUDES" ]]; then
    IFS=', ' read -r -a EXCL <<< "$(printf '%s' "$EXCLUDES" | tr '\n' ' ')"
fi

is_excluded() {
    local cfg="$1" e
    for e in "${EXCL[@]:-}"; do
        [[ -n "$e" && "$cfg" == *"$e"* ]] && return 0
    done
    return 1
}

# Category key = the top-level numbered dir, e.g. "examples/03-finance-trading/.." -> "03-finance-trading".
category_of() { local p="${1#examples/}"; printf '%s' "${p%%/*}"; }

declare -a CONFIGS=()
declare -a SKIPPED=()

if [[ -n "$EXPLICIT_CONFIGS" ]]; then
    # Explicit list wins over discovery. Accept absolute, repo-relative, or examples/-relative.
    IFS=', ' read -r -a WANTED <<< "$(printf '%s' "$EXPLICIT_CONFIGS" | tr '\n' ' ')"
    for w in "${WANTED[@]}"; do
        [[ -z "$w" ]] && continue
        if [[ -f "$w" ]]; then CONFIGS+=("$w")
        elif [[ -f "examples/$w" ]]; then CONFIGS+=("examples/$w")
        else echo "WARNING: config not found, skipping: $w" >&2; fi
    done
else
    for cfg in "${ALL_CONFIGS[@]}"; do
        [[ -n "$FILTER" && ! "$cfg" =~ $FILTER ]] && continue
        if is_excluded "$cfg"; then SKIPPED+=("$cfg"); continue; fi
        CONFIGS+=("$cfg")
    done

    # --sample N: keep the first config of each distinct category (category order),
    # capped at N — a fast, representative smoke set spanning the example taxonomy.
    if [[ -n "$SAMPLE" ]]; then
        declare -a SAMPLED=()
        declare -A SEEN_CAT=()
        for cfg in "${CONFIGS[@]}"; do
            cat="$(category_of "$cfg")"
            [[ -n "${SEEN_CAT[$cat]:-}" ]] && continue
            SEEN_CAT[$cat]=1
            SAMPLED+=("$cfg")
            [[ "${#SAMPLED[@]}" -ge "$SAMPLE" ]] && break
        done
        CONFIGS=("${SAMPLED[@]}")
        echo "==> Sample mode: ${#CONFIGS[@]} config(s), one per category (cap $SAMPLE)."
    fi
fi

total=${#CONFIGS[@]}
echo "==> Validating $total config(s)$([[ ${#SKIPPED[@]} -gt 0 ]] && echo ", skipping ${#SKIPPED[@]} excluded")..."
if [[ $total -eq 0 ]]; then
    echo "ERROR: no configs selected" >&2; exit 3
fi

# Results land one file per config. Honour VALIDATE_RESULT_DIR for observability
# (progress survives a killed parent); otherwise use a self-cleaning temp dir.
if [[ -n "${VALIDATE_RESULT_DIR:-}" ]]; then
    RESULT_DIR="$VALIDATE_RESULT_DIR"
    mkdir -p "$RESULT_DIR"
else
    RESULT_DIR="$(mktemp -d)"
    trap 'rm -rf "$RESULT_DIR"' EXIT
fi

# Fan out across $JOBS workers. Each worker self-invokes this script in __worker mode.
idx=0
for cfg in "${CONFIGS[@]}"; do
    idx=$((idx + 1))
    # bash "$0" (not bare "$0"): survives a checkout without the exec bit,
    # e.g. a CI runner invoking this script via `bash scripts/...`.
    bash "$0" __worker "$cfg" "$RESULT_DIR/$idx.res" &
    # Throttle: wait for a slot once JOBS workers are running.
    while [[ "$(jobs -r | wc -l)" -ge "$JOBS" ]]; do wait -n; done
done
wait

# ---------------------------------------------------------------------------
# Report
# ---------------------------------------------------------------------------
ok=0; fail=0
declare -a FAILS=()
echo
echo "STATUS  CONFIG                                                          DETAIL"
echo "------  --------------------------------------------------------------  ------"
# Emit rows in config order.
idx=0
for cfg in "${CONFIGS[@]}"; do
    idx=$((idx + 1))
    line="$(cat "$RESULT_DIR/$idx.res" 2>/dev/null)"
    status="${line%%$'\t'*}"
    rest="${line#*$'\t'}"
    detail="${rest#*$'\t'}"
    short="${cfg#examples/}"
    if [[ "$status" == "OK" ]]; then
        ok=$((ok + 1))
        printf 'OK      %-62s  %s\n' "$short" "$detail"
    else
        fail=$((fail + 1))
        FAILS+=("$short :: $detail")
        printf 'FAIL    %-62s  %s\n' "$short" "$detail"
    fi
done

echo
echo "==> Result: $ok OK / $fail FAIL (of $total)"
if [[ ${#SKIPPED[@]} -gt 0 ]]; then
    echo "==> Skipped (excluded): ${#SKIPPED[@]}"
fi
if [[ $fail -gt 0 ]]; then
    echo
    echo "Failures:"
    for f in "${FAILS[@]}"; do echo "  - $f"; done
    exit 1
fi
echo "==> All examples validated."
exit 0
