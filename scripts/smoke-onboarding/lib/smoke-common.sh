# smoke-common.sh — shared spine of the released-artefact smokes.
#
# Sourced (never executed) by run-smoke-deb.sh (LIN-02) and run-smoke-tarball.sh
# (MAC-02). Everything that must stay identical between the two lives here: the
# PASS/FAIL bookkeeping, the payload whitelist, the doctor verdict, and the four
# behavioural steps (init, doctor, run, rag). Only the install and uninstall
# phases differ, and those stay in the callers.
#
# Portability: macos-latest still ships bash 3.2 as /bin/bash, so this file (and
# its callers) stay inside that dialect -- no `mapfile`, and never a bare
# `${#arr[@]}` on a possibly-empty array, which is an "unbound variable" error
# under `set -u` before bash 4.4. Accumulators that can legitimately stay empty
# are plain strings for that reason.
#
# Contract expected from a caller before invoking any smoke_step_* function:
#   FIXTURES     directory holding offline-crew.yaml, rag-corpus/, rag-settings.json
#   ORKEON_BIN   the CLI entry point to exercise (launcher, symlink or apphost)
#   LOG_DIR      writable directory for the per-step .out/.err captures
#   RUN_DIR      scratch working directory the CLI is invoked from
# Call smoke_init_bookkeeping once first.

# The grammars WIN-04's MSBuild pruning keeps (src/Directory.Build.targets,
# OrkeonTreeSitterKeptGrammars). Losing one must fail the smoke.
SMOKE_KEPT_GRAMMARS=(
  tree-sitter
  tree-sitter-typescript
  tree-sitter-tsx
  tree-sitter-python
  tree-sitter-c-sharp
  tree-sitter-go
  tree-sitter-rust
)

# doctor downgrades a missing esbuild / embedding model / grammar to a warning,
# so "no fail" alone would happily pass a stripped payload. These three must be
# green for the payload to count as intact.
SMOKE_STRICT_CHECKS="esbuild local-embeddings tree-sitter"

smoke_init_bookkeeping() {
  SMOKE_FAILURES=0
  SMOKE_STEPS=()
}

smoke_log()  { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
smoke_info() { printf '    %s\n' "$*"; }

smoke_pass() { SMOKE_STEPS+=("PASS|$1|${2:-}"); printf '    \033[1;32mPASS\033[0m %s %s\n' "$1" "${2:-}"; }
smoke_skip() { SMOKE_STEPS+=("SKIP|$1|${2:-}"); printf '    \033[1;33mSKIP\033[0m %s %s\n' "$1" "${2:-}"; }
smoke_fail() {
  SMOKE_STEPS+=("FAIL|$1|${2:-}")
  SMOKE_FAILURES=$(( SMOKE_FAILURES + 1 ))
  printf '    \033[1;31mFAIL\033[0m %s %s\n' "$1" "${2:-}"
}

# smoke_shared_library_extension — the native library suffix of this platform.
# macOS ships the tree-sitter grammars as .dylib, Linux as .so; the smoke has to
# look for the right one or every payload check fails for the wrong reason.
smoke_shared_library_extension() {
  case "$(uname -s)" in
    Darwin) echo ".dylib" ;;
    *)      echo ".so" ;;
  esac
}

# smoke_run_orkeon <label> <args...> — runs the CLI from RUN_DIR, capturing the
# two streams separately into LOG_DIR. Sets ORK_EC / ORK_OUT / ORK_ERR.
smoke_run_orkeon() {
  local label="$1"; shift
  ORK_OUT="$LOG_DIR/$label.out"
  ORK_ERR="$LOG_DIR/$label.err"
  ( cd "$RUN_DIR" && "$ORKEON_BIN" "$@" ) >"$ORK_OUT" 2>"$ORK_ERR"
  ORK_EC=$?
  return 0
}

# smoke_assert_payload <app-dir> <esbuild-path> — the payload checks shared by
# every packaging format: the embedding model and the tree-sitter grammars sit
# next to the apphost, esbuild wherever the format puts it.
smoke_assert_payload() {
  local app_dir="$1" esbuild="$2"
  local ext missing="" g
  ext="$(smoke_shared_library_extension)"

  [[ -x "$esbuild" ]] || missing="$missing $esbuild"
  [[ -f "$app_dir/LocalEmbeddingsModel/default/model.onnx" ]] || missing="$missing LocalEmbeddingsModel/default/model.onnx"
  [[ -f "$app_dir/LocalEmbeddingsModel/default/vocab.txt" ]] || missing="$missing LocalEmbeddingsModel/default/vocab.txt"

  for g in "${SMOKE_KEPT_GRAMMARS[@]}"; do
    [[ -f "$app_dir/lib${g}${ext}" ]] || missing="$missing lib${g}${ext}"
  done

  if [[ -n "$missing" ]]; then
    smoke_fail "payload" "missing from $app_dir:$missing"
    return 1
  fi
  smoke_pass "payload" "esbuild + BGE-micro-v2 model + ${#SMOKE_KEPT_GRAMMARS[@]} tree-sitter libraries present"
  return 0
}

# --------------------------------------------------------------------------- #
# Behavioural steps — identical across formats.
# --------------------------------------------------------------------------- #

# smoke_step_init — `orkeon init --provider none --force` (WIN-02). Sets
# SMOKE_CONFIG_FILE / SMOKE_CONFIG_DIR / SMOKE_CONFIG_BACKUP for the caller's
# teardown, and backs up any config already on the machine.
smoke_step_init() {
  smoke_log "orkeon init --provider none --force"
  SMOKE_CONFIG_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/Orkeon"
  SMOKE_CONFIG_FILE="$SMOKE_CONFIG_DIR/appsettings.json"
  SMOKE_CONFIG_BACKUP=""
  if [[ -f "$SMOKE_CONFIG_FILE" ]]; then
    SMOKE_CONFIG_BACKUP="$LOG_DIR/../appsettings.json.pre-smoke"
    cp "$SMOKE_CONFIG_FILE" "$SMOKE_CONFIG_BACKUP"
    smoke_info "existing user config backed up to $SMOKE_CONFIG_BACKUP"
  fi

  smoke_run_orkeon init init --provider none --force
  if [[ $ORK_EC -ne 0 ]]; then
    tail -n 10 "$ORK_ERR" | sed 's/^/    | /'
    smoke_fail "init" "exit $ORK_EC (expected 0)"
  elif [[ -f "$SMOKE_CONFIG_FILE" ]]; then
    smoke_pass "init" "wrote $SMOKE_CONFIG_FILE"
  elif [[ -f "$RUN_DIR/Orkeon/appsettings.json" ]]; then
    # Environment.SpecialFolder.ApplicationData resolves to "" when $HOME/.config
    # does not exist yet, and init then writes a *relative* Orkeon/appsettings.json
    # into the working directory. Real defect, not a packaging one — report it as
    # such rather than papering over it with a mkdir.
    smoke_fail "init" "config written relative to the cwd ($RUN_DIR/Orkeon/appsettings.json) — $HOME/.config does not exist, so SpecialFolder.ApplicationData resolved to an empty path"
  else
    smoke_fail "init" "exit 0 but no config at $SMOKE_CONFIG_FILE"
  fi
}

# smoke_step_doctor — `orkeon doctor --json` (WIN-03): no check may report fail,
# and the three payload-backed checks must be ok rather than merely non-failing.
smoke_step_doctor() {
  smoke_log "orkeon doctor --json"
  smoke_run_orkeon doctor doctor --json
  if [[ $ORK_EC -gt 1 ]]; then
    tail -n 10 "$ORK_ERR" | sed 's/^/    | /'
    smoke_fail "doctor" "exit $ORK_EC (expected 0 or 1)"
    return
  fi

  local verdict
  verdict="$(python3 - "$ORK_OUT" "$SMOKE_STRICT_CHECKS" <<'PY'
import json, sys

strict = sys.argv[2].split()

try:
    with open(sys.argv[1], encoding="utf-8") as handle:
        results = json.load(handle)
except (OSError, ValueError) as exc:
    print(f"FAIL|doctor --json did not produce parsable JSON: {exc}")
    raise SystemExit(0)

if not isinstance(results, list) or not results:
    print("FAIL|doctor --json returned an empty or non-array payload")
    raise SystemExit(0)

by_check = {entry.get("check"): entry for entry in results}
problems = [
    f"{entry.get('check')}: {entry.get('detail')}"
    for entry in results
    if entry.get("status") == "fail"
]
problems += [
    f"{name}: expected ok, got {by_check[name].get('status')} ({by_check[name].get('detail')})"
    for name in strict
    if name in by_check and by_check[name].get("status") != "ok"
]
problems += [f"{name}: check absent from the report" for name in strict if name not in by_check]

if problems:
    print("FAIL|" + "; ".join(problems))
else:
    warned = [e.get("check") for e in results if e.get("status") == "warn"]
    print(f"PASS|{len(results)} checks, no failure, strict checks green"
          + (f" (warnings tolerated: {', '.join(warned)})" if warned else ""))
PY
)"

  if [[ "${verdict%%|*}" == "PASS" ]]; then
    smoke_pass "doctor" "${verdict#*|}"
  else
    head -n 20 "$ORK_OUT" | sed 's/^/    | /'
    smoke_fail "doctor" "${verdict#*|}"
  fi
}

# smoke_step_run — offline crew on the echo fallback (WIN-01): exit 0 and the
# actionable `orkeon init` warning on stderr.
smoke_step_run() {
  smoke_log "orkeon run (offline crew, echo fallback)"
  cp "$FIXTURES/offline-crew.yaml" "$RUN_DIR/offline-crew.yaml"
  smoke_run_orkeon run run offline-crew.yaml
  if [[ $ORK_EC -ne 0 ]]; then
    tail -n 10 "$ORK_ERR" | sed 's/^/    | /'
    smoke_fail "run" "exit $ORK_EC (expected 0 — the echo fallback makes the run deterministic)"
  elif grep -q 'orkeon init' "$ORK_ERR"; then
    smoke_pass "run" "exit 0 and the WIN-01 warning points at \`orkeon init\`"
  else
    tail -n 10 "$ORK_ERR" | sed 's/^/    | /'
    smoke_fail "run" "exit 0 but no \`orkeon init\` warning on stderr"
  fi
}

# smoke_step_rag — offline ingest + search. The answer text is empty without an
# LLM; the citations and their scores are the retrieval evidence, and that is
# all this asserts.
smoke_step_rag() {
  smoke_log "orkeon rag ingest / search (offline)"
  cp -R "$FIXTURES/rag-corpus" "$RUN_DIR/rag-corpus"
  cp "$FIXTURES/rag-settings.json" "$RUN_DIR/rag-settings.json"

  smoke_run_orkeon rag-ingest rag ingest --settings rag-settings.json --collection smoke --source 'rag-corpus/*.md'
  if [[ $ORK_EC -ne 0 ]]; then
    tail -n 10 "$ORK_ERR" | sed 's/^/    | /'
    smoke_fail "rag-ingest" "exit $ORK_EC (expected 0)"
  elif grep -qE 'Chunks: [1-9][0-9]* created' "$ORK_OUT"; then
    smoke_pass "rag-ingest" "$(grep -m1 'Chunks:' "$ORK_OUT" | sed 's/^- //')"
  else
    tail -n 10 "$ORK_OUT" | sed 's/^/    | /'
    smoke_fail "rag-ingest" "exit 0 but no chunk was created"
  fi

  smoke_run_orkeon rag-search rag search 'What does orkeon doctor do?' --settings rag-settings.json --collection smoke
  if [[ $ORK_EC -ne 0 ]]; then
    tail -n 10 "$ORK_ERR" | sed 's/^/    | /'
    smoke_fail "rag-search" "exit $ORK_EC (expected 0)"
  elif grep -q '^Sources:' "$ORK_OUT" && grep -qE '^- \[[0-9]+\] .* \(score: [0-9.]+\)' "$ORK_OUT"; then
    smoke_pass "rag-search" "$(grep -cE '^- \[[0-9]+\]' "$ORK_OUT") citation(s) with scores"
  else
    tail -n 10 "$ORK_OUT" | sed 's/^/    | /'
    smoke_fail "rag-search" "exit 0 but no citation with a score in the output"
  fi
}

# smoke_restore_user_config — leave the machine as we found it: restore a config
# we shadowed, or drop the one we created. Call after the uninstall assertions,
# which need it in place.
smoke_restore_user_config() {
  if [[ -n "${SMOKE_CONFIG_BACKUP:-}" ]]; then
    cp "$SMOKE_CONFIG_BACKUP" "$SMOKE_CONFIG_FILE"
    smoke_info "restored the pre-existing user config"
  elif [[ -n "${SMOKE_CONFIG_FILE:-}" ]]; then
    rm -f "$SMOKE_CONFIG_FILE"
    rmdir "$SMOKE_CONFIG_DIR" 2>/dev/null || true
    smoke_info "removed the config this smoke created"
  fi
}

# smoke_summary <banner> — prints the per-step table; returns 0/1 for the caller
# to exit with.
smoke_summary() {
  local banner="$1" step status label note
  smoke_log "Summary"
  # ${arr[@]:-} rather than ${arr[@]}: safe on bash 3.2 under `set -u` even if no
  # step was ever recorded.
  for step in "${SMOKE_STEPS[@]:-}"; do
    [[ -n "$step" ]] || continue
    IFS='|' read -r status label note <<<"$step"
    printf '    %-6s %-18s %s\n' "$status" "$label" "$note"
  done

  echo
  if [[ "$SMOKE_FAILURES" -eq 0 ]]; then
    printf '\033[1;32m%s PASSED\033[0m\n' "$banner"
    return 0
  fi
  printf '\033[1;31m%s FAILED\033[0m (%s step(s))\n' "$banner" "$SMOKE_FAILURES"
  return 1
}
