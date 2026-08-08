#!/usr/bin/env bash
#
# run-smoke-deb.sh — released-artefact smoke for the Debian package (LIN-02).
#
# Installs orkeon_<ver>_amd64.deb through apt on a bare runner, walks the whole
# onboarding chain on the installed binary, then removes the package and checks
# the removal is clean. Sibling of run-smoke.ps1 (WIN-06) and run-smoke-tarball.sh
# (MAC-02): the behavioural steps, the payload whitelist and the doctor verdict
# all come from lib/smoke-common.sh, so the three can only differ where they
# must — the install and uninstall phases.
#
# What it exercises, end to end, on the *published* payload:
#   1. apt resolves the package's Depends on a stock image (no dotnet repo);
#   2. the payload survived packaging (esbuild, embedding model, the 7 whitelisted
#      tree-sitter grammars — WIN-04 pruning);
#   3. `orkeon init --provider none --force` writes the per-user config (WIN-02);
#   4. `orkeon doctor --json` reports no ❌, and the three payload-backed checks
#      are green, not merely non-failing (that is what makes a missing esbuild /
#      model / grammar fail the smoke — doctor only warns on those);
#   5. `orkeon run <offline crew>` exits 0 and prints the WIN-01 warning;
#   6. `orkeon rag ingest` + `orkeon rag search` retrieve with citations and
#      scores, fully offline;
#   7. `apt-get remove` drops /usr/bin/orkeon and leaves ~/.config/Orkeon intact.
#
# Usage:
#   scripts/smoke-onboarding/run-smoke-deb.sh --deb artifacts/installers/orkeon_*_amd64.deb
#   scripts/smoke-onboarding/run-smoke-deb.sh --orkeon /path/to/orkeon   # degraded, see below
#
#   --deb PATH      the package to install (apt install / apt remove phases run).
#   --orkeon PATH   skip apt entirely and smoke an already-available binary. The
#                   degraded mode used for local validation on a machine where
#                   installing a package is not an option; steps 1, 2 and 7 are
#                   reported SKIP.
#   --work-dir DIR  scratch directory (default: a mktemp -d, removed on success).
#   --keep          keep the scratch directory even on success.
#
# Exit status: 0 when every non-skipped step passed, 1 otherwise.

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FIXTURES="$SCRIPT_DIR/fixtures"

# shellcheck source=lib/smoke-common.sh
. "$SCRIPT_DIR/lib/smoke-common.sh"

DEB_PATH=""
ORKEON_BIN=""
WORK_DIR=""
KEEP=false

INSTALLED_PREFIX="/usr/lib/orkeon"
INSTALLED_BIN="/usr/bin/orkeon"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --deb)      DEB_PATH="$2"; shift 2 ;;
    --orkeon)   ORKEON_BIN="$2"; shift 2 ;;
    --work-dir) WORK_DIR="$2"; shift 2 ;;
    --keep)     KEEP=true; shift ;;
    -h|--help)  sed -n '2,38p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

if [[ -z "$DEB_PATH" && -z "$ORKEON_BIN" ]]; then
  echo "One of --deb or --orkeon is required." >&2
  exit 2
fi

smoke_init_bookkeeping

CREATED_WORK_DIR=false
cleanup() {
  if [[ "$CREATED_WORK_DIR" == true && "$KEEP" != true && "$SMOKE_FAILURES" -eq 0 && -n "$WORK_DIR" ]]; then
    rm -rf -- "$WORK_DIR" 2>/dev/null || true
  fi
}
trap cleanup EXIT

if [[ -z "$WORK_DIR" ]]; then
  WORK_DIR="$(mktemp -d)"
  CREATED_WORK_DIR=true
fi
mkdir -p "$WORK_DIR"
# mktemp -d creates 0700 directories: _apt cannot traverse the parent even when
# the staged .deb itself is 644, so apt falls back to a root fetch with a noisy
# sandbox warning. Opening the work dir keeps the sandboxed path quiet.
chmod 755 "$WORK_DIR"
WORK_DIR="$(cd "$WORK_DIR" && pwd)"
LOG_DIR="$WORK_DIR/logs"
RUN_DIR="$WORK_DIR/run"
mkdir -p "$LOG_DIR" "$RUN_DIR"

# --------------------------------------------------------------------------- #
# 1. Install through apt
# --------------------------------------------------------------------------- #
smoke_log "Orkeon .deb smoke — work dir: $WORK_DIR"

if [[ -n "$DEB_PATH" ]]; then
  if [[ ! -f "$DEB_PATH" ]]; then
    smoke_fail "apt-install" "package not found: $DEB_PATH"
    smoke_summary "DEB SMOKE"
    exit 1
  fi
  DEB_PATH="$(cd "$(dirname "$DEB_PATH")" && pwd)/$(basename "$DEB_PATH")"

  # apt drops privileges to _apt to fetch even a local file; a package sitting in
  # a home directory it cannot read makes the install fail with a confusing
  # sandbox error. Stage it somewhere world-readable first.
  STAGED_DEB="$WORK_DIR/$(basename "$DEB_PATH")"
  cp "$DEB_PATH" "$STAGED_DEB"
  chmod 644 "$STAGED_DEB"

  smoke_info "apt-get install $(basename "$STAGED_DEB")"
  if sudo apt-get update -qq >"$LOG_DIR/apt-update.log" 2>&1 \
     && sudo apt-get install -y "$STAGED_DEB" >"$LOG_DIR/apt-install.log" 2>&1; then
    smoke_pass "apt-install" "Depends resolved on a stock image"
  else
    tail -n 20 "$LOG_DIR/apt-install.log" 2>/dev/null | sed 's/^/    | /'
    smoke_fail "apt-install" "apt-get install failed (see $LOG_DIR/apt-install.log)"
    smoke_summary "DEB SMOKE"
    exit 1
  fi

  if [[ -x "$INSTALLED_BIN" ]]; then
    smoke_pass "launcher" "$INSTALLED_BIN installed and executable"
  else
    smoke_fail "launcher" "$INSTALLED_BIN missing after install"
  fi
  ORKEON_BIN="$INSTALLED_BIN"
  PAYLOAD_ROOT="$INSTALLED_PREFIX"
else
  smoke_skip "apt-install" "--orkeon given: smoking an already-available binary"
  smoke_skip "launcher" "--orkeon given"
  if [[ ! -x "$ORKEON_BIN" ]]; then
    smoke_fail "launcher" "not executable: $ORKEON_BIN"
    smoke_summary "DEB SMOKE"
    exit 1
  fi
  ORKEON_BIN="$(cd "$(dirname "$ORKEON_BIN")" && pwd)/$(basename "$ORKEON_BIN")"
  PAYLOAD_ROOT="$(dirname "$ORKEON_BIN")"
fi

# --------------------------------------------------------------------------- #
# 2. Payload content
# --------------------------------------------------------------------------- #
# The Debian layout flattens the apphost into /usr/lib/orkeon with esbuild in a
# sibling subdirectory, where the tar.gz keeps the archive's libexec/ tree.
smoke_log "Package payload"
smoke_assert_payload "$PAYLOAD_ROOT" "$PAYLOAD_ROOT/esbuild-bin/esbuild"

# --------------------------------------------------------------------------- #
# 3-6. init / doctor / run / rag — shared with the tarball smoke
# --------------------------------------------------------------------------- #
smoke_step_init
smoke_step_doctor
smoke_step_run
smoke_step_rag

# --------------------------------------------------------------------------- #
# 7. apt-get remove — binary gone, user config intact
# --------------------------------------------------------------------------- #
smoke_log "apt-get remove orkeon"
if [[ -n "$DEB_PATH" ]]; then
  if sudo apt-get remove -y orkeon >"$LOG_DIR/apt-remove.log" 2>&1; then
    leftovers=""
    [[ -e "$INSTALLED_BIN" ]] && leftovers="$leftovers $INSTALLED_BIN still present;"
    # /usr/lib/orkeon may survive as an empty directory (dpkg keeps directories it
    # did not create alone); a leftover *payload* is the regression to catch.
    [[ -e "$INSTALLED_PREFIX/orkeon" ]] && leftovers="$leftovers $INSTALLED_PREFIX/orkeon still present;"
    if [[ -z "$leftovers" ]]; then
      smoke_pass "apt-remove" "$INSTALLED_BIN and the payload are gone"
    else
      smoke_fail "apt-remove" "$leftovers"
    fi
  else
    tail -n 20 "$LOG_DIR/apt-remove.log" 2>/dev/null | sed 's/^/    | /'
    smoke_fail "apt-remove" "apt-get remove failed (see $LOG_DIR/apt-remove.log)"
  fi

  if [[ -f "$SMOKE_CONFIG_FILE" ]]; then
    smoke_pass "config-preserved" "$SMOKE_CONFIG_FILE survived the removal"
  else
    smoke_fail "config-preserved" "$SMOKE_CONFIG_FILE was removed — user configuration must never be touched"
  fi
else
  smoke_skip "apt-remove" "--orkeon given: nothing was installed"
  smoke_skip "config-preserved" "--orkeon given"
fi

smoke_restore_user_config

# --------------------------------------------------------------------------- #
# Summary
# --------------------------------------------------------------------------- #
if [[ "$SMOKE_FAILURES" -ne 0 ]]; then
  smoke_info "logs kept in $LOG_DIR"
fi
smoke_summary "DEB SMOKE"
exit $?
