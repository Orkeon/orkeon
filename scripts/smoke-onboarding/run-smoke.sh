#!/usr/bin/env bash
#
# run-smoke.sh — Onboarding smoke test for Orkeon (P3-4).
#
# Executable, measurable implementation of the "new arrival ≤ 20 min" acceptance
# criterion (remediation-onboarding-examples-experiments-2026-07-10.md §2). It
# walks the three documented onboarding paths on a clean checkout, times each,
# and reports PASS / FAIL / SKIP:
#
#   A — from sources : docs/getting-started/run-your-first-example.md
#       clone → copy an appsettings.*.local.json.example → run a bundled crew
#       through the `orkeon` CLI (`orkeon run <crew.yaml>`).
#   B — experiments  : experiments/09-factures-extraction/run.sh
#       (1) missing settings must print the actionable `cp …` message and exit 1;
#       (2) after `cp` of the template it must start and fail cleanly on the LLM
#       endpoint (or complete when a real key is supplied).
#   C — no SDK       : self-contained Releases binary (ORKEON_SMOKE_RELEASE_URL)
#       or the container image (ORKEON_SMOKE_IMAGE / a local `orkeon-runners`
#       image) — run the same bundled crew without compiling anything.
#
# Success WITHOUT a real key: the whole onboarding chain is exercised (build,
# strict config load, crew construction with zero "tool not found" warnings, LLM
# pre-flight probe) and the ONLY thing missing is a reachable endpoint + key —
# signalled by the runner's actionable message and exit code 2. To make that
# signal deterministic regardless of the CI network (egress or not), the no-key
# runs redirect the LLM BaseUrl to an unreachable port via ORKEON_Llm__BaseUrl.
#
# Success WITH a real key: export ORKEON_SMOKE_SETTINGS=/abs/path/appsettings.json
# (a working profile). The no-key redirect is then dropped and every scenario is
# asserted on a full run (exit 0).
#
# The script is self-contained and CI-safe: it depends on no pre-existing local
# state and removes every file it copies (notably the appsettings copies in
# examples/ and experiments/), leaving `git status` clean outside output/.
#
# Exit status: 0 if no non-skipped scenario failed, 1 otherwise.

set -uo pipefail

# --------------------------------------------------------------------------- #
# Configuration (all overridable from the environment)
# --------------------------------------------------------------------------- #
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# A bundled, self-contained (embedded data) showcase crew — same one across A/C.
SHOWCASE_CONFIG="examples/01-enterprise/01-research-assistant/config.yaml"
DEEPSEEK_EXAMPLE="examples/appsettings/appsettings.deepseek.local.json.example"

# Real settings (working key) → full-run assertions (exit 0). Optional.
ORKEON_SMOKE_SETTINGS="${ORKEON_SMOKE_SETTINGS:-}"

# Scenario C sources (optional; scenario is SKIPped when none is usable).
ORKEON_SMOKE_RELEASE_URL="${ORKEON_SMOKE_RELEASE_URL:-}"
ORKEON_SMOKE_IMAGE="${ORKEON_SMOKE_IMAGE:-}"

# Per-run timeout (seconds). Default 600 — the first source build + a virtiofs
# runner start can take a couple of minutes on a slow (WSL2) filesystem.
ORKEON_SMOKE_TIMEOUT="${ORKEON_SMOKE_TIMEOUT:-600}"

# Deterministic "unreachable endpoint" for the no-key probe. Must fail *fast*:
# a loopback port nobody listens on refuses the connection instantly, so the
# runner's TCP pre-flight probe reports the endpoint unreachable and exits 2 —
# whether or not the CI runner has network egress. (A filtered *remote* port is
# a poor choice: the connect hangs on a long timeout instead of failing fast.)
UNREACHABLE_BASEURL="http://127.0.0.1:1"

# --------------------------------------------------------------------------- #
# Bookkeeping
# --------------------------------------------------------------------------- #
declare -A RESULT   # scenario -> PASS|FAIL|SKIP
declare -A SECS     # scenario -> duration (integer seconds)
declare -A NOTE     # scenario -> one-line explanation
CLEANUP=()          # files/dirs to remove on exit

cleanup() {
  local p
  for p in "${CLEANUP[@]:-}"; do
    [ -n "$p" ] && rm -rf -- "$p" 2>/dev/null || true
  done
}
trap cleanup EXIT

log()  { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
info() { printf '    %s\n' "$*"; }

# has_registry_gap <logfile> — true if the runner reported an unresolved tool,
# i.e. the onboarding chain itself is broken (not just a missing key).
has_registry_gap() {
  grep -Eiq "not found in registry|tool .* not found|could not be resolved" "$1"
}

# probe_unreachable <logfile> — true if the runner's LLM pre-flight probe reported
# the endpoint unreachable. The runner reuses exit code 2 for other failures (e.g.
# a bad VFS mount), so the no-key PASS requires this message, not just the code.
# Observed wording (Orkeon.Hosting RunnerExecution):
#   "ERROR: No reachable LLM endpoint (endpoint ... refused the connection)."
#   "LLM endpoint unreachable (connection refused): ..."
probe_unreachable() {
  grep -Eiq "No reachable LLM endpoint|LLM endpoint unreachable" "$1"
}

# --------------------------------------------------------------------------- #
# Scenario A — from sources (orkeon CLI)
# --------------------------------------------------------------------------- #
scenario_a() {
  log "Scenario A — from sources (orkeon CLI)"
  local start=$SECONDS
  local settings out log
  out="$(mktemp -d)"; CLEANUP+=("$out")
  log="$(mktemp)";    CLEANUP+=("$log")

  local -a env_prefix=()
  if [ -n "$ORKEON_SMOKE_SETTINGS" ]; then
    settings="$ORKEON_SMOKE_SETTINGS"
    info "Using real settings: $settings (expecting a full run, exit 0)"
  else
    # Faithful to the README: copy the DeepSeek template (no key), then redirect
    # the endpoint so the probe deterministically reports "unreachable".
    settings="$REPO_ROOT/examples/appsettings/appsettings.deepseek.smoke.json"
    cp "$REPO_ROOT/$DEEPSEEK_EXAMPLE" "$settings"; CLEANUP+=("$settings")
    env_prefix=(env "ORKEON_Llm__BaseUrl=$UNREACHABLE_BASEURL")
    info "Copied $DEEPSEEK_EXAMPLE (no key); endpoint redirected to an unreachable port"
  fi

  info "Running the research-assistant crew via the orkeon CLI …"
  # YAML crews don't need esbuild; skip the scripting npm bootstrap during build.
  "${env_prefix[@]}" timeout "$ORKEON_SMOKE_TIMEOUT" \
    dotnet run --project "$REPO_ROOT/src/scripting/Orkeon.Scripting.Cli" -c Release \
      -p:SkipScriptingNpmInstall=true -- \
      run "$REPO_ROOT/$SHOWCASE_CONFIG" \
      --settings "$settings" \
      --mount "$out:/output:rw" \
      -v 1 >"$log" 2>&1
  local ec=$?
  SECS[A]=$(( SECONDS - start ))
  tail -n 15 "$log" | sed 's/^/    | /'

  evaluate_run A "$ec" "$log"
}

# --------------------------------------------------------------------------- #
# Scenario B — experiments (09-factures-extraction/run.sh)
# --------------------------------------------------------------------------- #
scenario_b() {
  log "Scenario B — experiments (09-factures-extraction/run.sh)"
  local start=$SECONDS
  local exp="$REPO_ROOT/experiments/09-factures-extraction"

  if [ ! -f "$exp/run.sh" ]; then
    RESULT[B]=SKIP; SECS[B]=$(( SECONDS - start ))
    NOTE[B]="experiments submodule not checked out (run: git submodule update --init experiments)"
    info "${NOTE[B]}"
    return
  fi

  # B.1 — missing settings must print the actionable message and exit 1.
  info "B.1 — run.sh with no settings copied (expect actionable 'cp …' + exit 1)"
  local log1; log1="$(mktemp)"; CLEANUP+=("$log1")
  ( cd "$exp" && bash run.sh json ) >"$log1" 2>&1
  local ec1=$?
  tail -n 6 "$log1" | sed 's/^/    | /'
  if [ "$ec1" -ne 1 ] || ! grep -q "settings manquants" "$log1" || ! grep -q "^ *cp " "$log1"; then
    RESULT[B]=FAIL; SECS[B]=$(( SECONDS - start ))
    NOTE[B]="B.1 did not produce the actionable message + exit 1 (got exit $ec1)"
    info "B.1 FAIL — ${NOTE[B]}"
    return
  fi
  info "B.1 OK — actionable message + exit 1"

  # B.2 — copy the template, then run. Restore the pristine appsettings/ after.
  local tpl="$exp/appsettings/appsettings.deepseek.local.json.example"
  local dst="$exp/appsettings/appsettings.deepseek.local.json"
  info "B.2 — cp the template and run (source mode; no committed state left behind)"
  cp "$tpl" "$dst"; CLEANUP+=("$dst")

  local log2; log2="$(mktemp)"; CLEANUP+=("$log2")
  local -a env_prefix=()
  local -a run_args=(json --mode source)
  if [ -n "$ORKEON_SMOKE_SETTINGS" ]; then
    run_args+=(--settings "$ORKEON_SMOKE_SETTINGS")
    info "Using real settings (expecting a full run, exit 0)"
  else
    env_prefix=(env "ORKEON_Llm__BaseUrl=$UNREACHABLE_BASEURL")
    info "No key; endpoint redirected to an unreachable port"
  fi

  "${env_prefix[@]}" timeout "$ORKEON_SMOKE_TIMEOUT" \
    bash -c 'cd "$1" && shift && exec bash run.sh "$@"' _ "$exp" "${run_args[@]}" \
    >"$log2" 2>&1
  local ec2=$?
  # Remove the copy immediately so `git status` in the submodule stays clean.
  rm -f "$dst"
  SECS[B]=$(( SECONDS - start ))
  tail -n 15 "$log2" | sed 's/^/    | /'

  evaluate_run B "$ec2" "$log2"
}

# --------------------------------------------------------------------------- #
# Scenario C — no SDK (self-contained binary or container image)
# --------------------------------------------------------------------------- #
scenario_c() {
  log "Scenario C — no SDK (binary / container)"
  local start=$SECONDS

  if [ -n "$ORKEON_SMOKE_RELEASE_URL" ]; then
    scenario_c_binary "$start"; return
  fi

  # Resolve a container image: explicit override, else a locally-built one.
  local image="$ORKEON_SMOKE_IMAGE"
  if [ -z "$image" ] && command -v docker >/dev/null 2>&1; then
    local cand
    for cand in orkeon-runners:test orkeon-runners:latest ghcr.io/orkeon/orkeon-runners:latest; do
      if docker image inspect "$cand" >/dev/null 2>&1; then image="$cand"; break; fi
    done
  fi

  if [ -z "$image" ]; then
    RESULT[C]=SKIP; SECS[C]=$(( SECONDS - start ))
    NOTE[C]="no release binary (ORKEON_SMOKE_RELEASE_URL) and no runnable image (ORKEON_SMOKE_IMAGE / local orkeon-runners) — publish pending"
    info "${NOTE[C]}"
    return
  fi
  if ! command -v docker >/dev/null 2>&1; then
    RESULT[C]=SKIP; SECS[C]=$(( SECONDS - start ))
    NOTE[C]="image $image requested but docker is not available"
    info "${NOTE[C]}"
    return
  fi

  scenario_c_container "$image" "$start"
}

scenario_c_container() {
  local image="$1" start="$2"
  info "Using container image: $image"
  local out settings log
  out="$(mktemp -d)"; CLEANUP+=("$out")
  log="$(mktemp)";    CLEANUP+=("$log")

  local -a docker_env=()
  if [ -n "$ORKEON_SMOKE_SETTINGS" ]; then
    settings="$ORKEON_SMOKE_SETTINGS"
    info "Using real settings: $settings (expecting a full run, exit 0)"
  else
    settings="$(mktemp)"; CLEANUP+=("$settings")
    cp "$REPO_ROOT/$DEEPSEEK_EXAMPLE" "$settings"
    docker_env=(-e "ORKEON_Llm__BaseUrl=$UNREACHABLE_BASEURL")
    info "Copied DeepSeek template (no key); endpoint redirected to an unreachable port"
  fi

  info "Running the research-assistant crew inside the container …"
  # The image forwards to the `orkeon` CLI, so the crew config is passed
  # positionally after the `run` verb (same convention as scenarios A/binary).
  timeout "$ORKEON_SMOKE_TIMEOUT" \
    docker run --rm "${docker_env[@]}" \
      -v "$out:/output" \
      -v "$settings:/app/appsettings.smoke.json:ro" \
      "$image" \
      run "$SHOWCASE_CONFIG" \
      --settings /app/appsettings.smoke.json \
      --mount /output:/output:rw \
      -v 1 >"$log" 2>&1
  local ec=$?
  SECS[C]=$(( SECONDS - start ))
  tail -n 15 "$log" | sed 's/^/    | /'

  evaluate_run C "$ec" "$log"
}

scenario_c_binary() {
  local start="$1"
  info "Downloading self-contained runner: $ORKEON_SMOKE_RELEASE_URL"
  local work bin out settings log
  work="$(mktemp -d)"; CLEANUP+=("$work")
  out="$(mktemp -d)";  CLEANUP+=("$out")
  log="$(mktemp)";     CLEANUP+=("$log")

  if ! curl -fsSL "$ORKEON_SMOKE_RELEASE_URL" -o "$work/pkg" 2>>"$log"; then
    RESULT[C]=FAIL; SECS[C]=$(( SECONDS - start ))
    NOTE[C]="download failed: $ORKEON_SMOKE_RELEASE_URL"
    info "${NOTE[C]}"; return
  fi
  case "$ORKEON_SMOKE_RELEASE_URL" in
    *.tar.gz|*.tgz) tar -xzf "$work/pkg" -C "$work" ;;
    *.zip)          unzip -q "$work/pkg" -d "$work" ;;
    *)              cp "$work/pkg" "$work/orkeon"; chmod +x "$work/orkeon" ;;
  esac
  bin="$(find "$work" -maxdepth 2 -type f -name 'orkeon' -perm -u+x | head -n1)"
  if [ -z "$bin" ]; then
    RESULT[C]=FAIL; SECS[C]=$(( SECONDS - start ))
    NOTE[C]="no runnable binary found inside $ORKEON_SMOKE_RELEASE_URL"
    info "${NOTE[C]}"; return
  fi

  local -a env_prefix=()
  if [ -n "$ORKEON_SMOKE_SETTINGS" ]; then
    settings="$ORKEON_SMOKE_SETTINGS"
  else
    settings="$(mktemp)"; CLEANUP+=("$settings")
    cp "$REPO_ROOT/$DEEPSEEK_EXAMPLE" "$settings"
    env_prefix=(env "ORKEON_Llm__BaseUrl=$UNREACHABLE_BASEURL")
  fi

  info "Running the research-assistant crew from the downloaded binary …"
  "${env_prefix[@]}" timeout "$ORKEON_SMOKE_TIMEOUT" \
    "$bin" \
      run "$REPO_ROOT/$SHOWCASE_CONFIG" \
      --settings "$settings" \
      --mount "$out:/output:rw" \
      -v 1 >"$log" 2>&1
  local ec=$?
  SECS[C]=$(( SECONDS - start ))
  tail -n 15 "$log" | sed 's/^/    | /'

  evaluate_run C "$ec" "$log"
}

# --------------------------------------------------------------------------- #
# Shared verdict logic for a runner invocation.
#   With a real key   → PASS iff exit 0.
#   Without a key      → PASS iff the LLM pre-flight probe reported the endpoint
#                        unreachable (exit 2) AND no tool-registry gap appeared.
# --------------------------------------------------------------------------- #
evaluate_run() {
  local sc="$1" ec="$2" log="$3"

  if [ "$ec" -eq 124 ]; then
    RESULT[$sc]=FAIL
    NOTE[$sc]="timed out after ${ORKEON_SMOKE_TIMEOUT}s"
    info "$sc FAIL — ${NOTE[$sc]}"
    return
  fi

  if has_registry_gap "$log"; then
    RESULT[$sc]=FAIL
    NOTE[$sc]="onboarding chain broken — a tool was not found in the registry"
    info "$sc FAIL — ${NOTE[$sc]}"
    return
  fi

  if [ -n "$ORKEON_SMOKE_SETTINGS" ]; then
    if [ "$ec" -eq 0 ]; then
      RESULT[$sc]=PASS; NOTE[$sc]="full run completed (exit 0) with real settings"
    else
      RESULT[$sc]=FAIL; NOTE[$sc]="real-settings run exited $ec (expected 0)"
    fi
  else
    if [ "$ec" -eq 2 ] && probe_unreachable "$log"; then
      RESULT[$sc]=PASS
      NOTE[$sc]="chain OK up to the LLM call; probe reported endpoint unreachable + exit 2 (key/endpoint is the only missing piece)"
    elif [ "$ec" -eq 2 ]; then
      RESULT[$sc]=FAIL
      NOTE[$sc]="exit 2 but not from the LLM probe (no 'endpoint unreachable' message) — likely a mount/config error"
    elif [ "$ec" -eq 0 ]; then
      RESULT[$sc]=FAIL
      NOTE[$sc]="exit 0 without a key is suspicious (empty/false success)"
    else
      RESULT[$sc]=FAIL
      NOTE[$sc]="expected the unreachable-endpoint probe signal (exit 2 + message), got exit $ec"
    fi
  fi
  info "$sc ${RESULT[$sc]} — ${NOTE[$sc]}"
}

# --------------------------------------------------------------------------- #
# Run
# --------------------------------------------------------------------------- #
cd "$REPO_ROOT"
log "Orkeon onboarding smoke — repo: $REPO_ROOT"
if [ -n "$ORKEON_SMOKE_SETTINGS" ]; then
  info "Mode: REAL key (ORKEON_SMOKE_SETTINGS set) — asserting full runs (exit 0)"
else
  info "Mode: NO key — asserting the onboarding chain up to the LLM probe (exit 2)"
fi
info "Per-run timeout: ${ORKEON_SMOKE_TIMEOUT}s"

scenario_a
scenario_b
scenario_c

# --------------------------------------------------------------------------- #
# Summary
# --------------------------------------------------------------------------- #
log "Summary"
printf '    %-3s  %-6s  %8s   %s\n' "SC" "RESULT" "SECONDS" "NOTE"
printf '    %-3s  %-6s  %8s   %s\n' "--" "------" "-------" "----"
fail=0
total=0
for sc in A B C; do
  r="${RESULT[$sc]:-SKIP}"
  d="${SECS[$sc]:-0}"
  total=$(( total + d ))
  printf '    %-3s  %-6s  %8s   %s\n' "$sc" "$r" "$d" "${NOTE[$sc]:-}"
  [ "$r" = FAIL ] && fail=1
done
printf '    %-3s  %-6s  %8s\n' "" "TOTAL" "$total"

echo
if [ "$fail" -eq 0 ]; then
  printf '\033[1;32mSMOKE PASSED\033[0m (no non-skipped scenario failed)\n'
  exit 0
else
  printf '\033[1;31mSMOKE FAILED\033[0m (at least one scenario failed)\n'
  exit 1
fi
