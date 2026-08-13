#!/usr/bin/env bash
#
# run-smoke-tarball.sh — released-artefact smoke for the POSIX tar.gz archives
# (MAC-02).
#
# Extracts orkeon-cli-<ver>-<rid>.tar.gz, installs it with the bundled
# install.sh, walks the whole onboarding chain on the installed binary, then
# uninstalls and checks the removal is clean. Third sibling of run-smoke.ps1
# (WIN-06) and run-smoke-deb.sh (LIN-02): the behavioural steps, the payload
# whitelist and the doctor verdict all come from lib/smoke-common.sh, so the
# three can only differ where they must — the install and uninstall phases.
#
# What it exercises, end to end, on the *published* archive:
#   1. install.sh lays the payload out under <prefix>/lib/orkeon and symlinks the
#      launchers into <prefix>/bin;
#   2. the payload survived packaging (esbuild, embedding model, the 7
#      whitelisted tree-sitter grammars — WIN-04 pruning);
#   3. the launcher resolves through its symlink and finds its bundled esbuild;
#  3b. the Orkeon Studio TUIs are exactly where the RID filter says they should
#      be: installed and answering `--version` headless on a linux archive,
#      *absent altogether* from an osx CLI archive (STUDIO-08, spec §8.4) — see
#      --studio below;
#   4. `orkeon init --provider none --force` writes the per-user config (WIN-02);
#   5. `orkeon doctor --json` reports no failure, and the three payload-backed
#      checks are green rather than merely non-failing (WIN-03);
#   6. `orkeon run <offline crew>` exits 0 and prints the WIN-01 warning;
#   7. `orkeon rag ingest` + `orkeon rag search` retrieve with citations and
#      scores, fully offline;
#   8. `install.sh --uninstall` drops <prefix>/lib/orkeon and the symlinks, and
#      leaves ~/.config/Orkeon intact.
#
# On macOS this is also the Gatekeeper/code-signing probe: an unsigned or
# quarantined native library (libtree-sitter*.dylib, onnxruntime, the esbuild
# binary) kills the process at load time, so steps 5 to 7 fail here rather than
# in a user's terminal.
#
# Portable by construction: the platform only decides the native library suffix
# (.dylib vs .so). Running it on Linux against the linux-x64 CLI tarball is the
# supported local-development mode and exercises the identical code path.
#
# Usage:
#   scripts/smoke-onboarding/run-smoke-tarball.sh --tarball artifacts/installers/orkeon-cli-*-linux-x64.tar.gz
#
#   --tarball PATH  the archive to install (required).
#   --prefix DIR    install prefix handed to install.sh (default: $HOME/.local,
#                   what a real user gets).
#   --studio MODE   present | absent | auto (default). What the archive is
#                   supposed to carry of Orkeon Studio, i.e. which side of the
#                   RID filter this archive is on:
#                     present — the two TUI launchers must be installed and must
#                               answer --version headless (every linux archive,
#                               and the full app-set on osx: the TUIs have no RID
#                               filter, only the WPF app does);
#                     absent  — nothing named orkeon-studio* may exist anywhere
#                               (the osx *cli* archives — V1 ships no Studio on
#                               macOS). CI passes this explicitly for macOS so
#                               the guard cannot quietly turn into a no-op.
#                     auto    — deduced from the archive name.
#   --work-dir DIR  scratch directory (default: a mktemp -d, removed on success).
#   --keep          keep the scratch directory even on success.
#   --force         proceed even if <prefix>/lib/orkeon already exists. Without
#                   it the smoke refuses rather than delete-and-replace an
#                   install it did not create.
#
# Exit status: 0 when every step passed, 1 otherwise.

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FIXTURES="$SCRIPT_DIR/fixtures"

# shellcheck source=lib/smoke-common.sh
. "$SCRIPT_DIR/lib/smoke-common.sh"

TARBALL=""
PREFIX="$HOME/.local"
WORK_DIR=""
KEEP=false
FORCE=false
STUDIO_MODE="auto"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --tarball)  TARBALL="$2"; shift 2 ;;
    --prefix)   PREFIX="$2"; shift 2 ;;
    --work-dir) WORK_DIR="$2"; shift 2 ;;
    --studio)   STUDIO_MODE="$2"; shift 2 ;;
    --keep)     KEEP=true; shift ;;
    --force)    FORCE=true; shift ;;
    -h|--help)  sed -n '2,65p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

case "$STUDIO_MODE" in
  present|absent|auto) ;;
  *) echo "--studio must be present, absent or auto (got: $STUDIO_MODE)" >&2; exit 2 ;;
esac

if [[ -z "$TARBALL" ]]; then
  echo "--tarball is required." >&2
  exit 2
fi
if [[ ! -f "$TARBALL" ]]; then
  echo "Archive not found: $TARBALL" >&2
  exit 2
fi
TARBALL="$(cd "$(dirname "$TARBALL")" && pwd)/$(basename "$TARBALL")"

mkdir -p "$PREFIX"
PREFIX="$(cd "$PREFIX" && pwd)"
LIB_DIR="$PREFIX/lib/orkeon"
BIN_DIR="$PREFIX/bin"

# install.sh does a delete-and-replace of $LIB_DIR, and the uninstall step below
# removes it outright. Refuse to run over an install we did not create — on a
# developer machine that would silently destroy a working setup.
if [[ -d "$LIB_DIR" && "$FORCE" != true ]]; then
  echo "Refusing to run: $LIB_DIR already exists." >&2
  echo "This smoke installs and then uninstalls, which would destroy it." >&2
  echo "Pass --prefix <scratch dir> to install elsewhere, or --force to proceed anyway." >&2
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
WORK_DIR="$(cd "$WORK_DIR" && pwd)"
LOG_DIR="$WORK_DIR/logs"
RUN_DIR="$WORK_DIR/run"
EXTRACT_DIR="$WORK_DIR/extract"
mkdir -p "$LOG_DIR" "$RUN_DIR" "$EXTRACT_DIR"

# --------------------------------------------------------------------------- #
# 1. Extract
# --------------------------------------------------------------------------- #
smoke_log "Orkeon tar.gz smoke — $(uname -s) — work dir: $WORK_DIR"
smoke_info "archive: $TARBALL"
smoke_info "prefix:  $PREFIX"

if ! tar -xzf "$TARBALL" -C "$EXTRACT_DIR" 2>"$LOG_DIR/extract.err"; then
  tail -n 10 "$LOG_DIR/extract.err" | sed 's/^/    | /'
  smoke_fail "extract" "tar -xzf failed"
  smoke_summary "TARBALL SMOKE"
  exit 1
fi

# No `mapfile` here: macos-latest still ships bash 3.2, which does not have it.
ROOT_COUNT=0
ARCHIVE_ROOT=""
while IFS= read -r candidate; do
  ROOT_COUNT=$(( ROOT_COUNT + 1 ))
  ARCHIVE_ROOT="$candidate"
done < <(find "$EXTRACT_DIR" -mindepth 1 -maxdepth 1 -type d)

if [[ "$ROOT_COUNT" -ne 1 ]]; then
  smoke_fail "extract" "expected exactly one top-level folder in the archive, found $ROOT_COUNT"
  smoke_summary "TARBALL SMOKE"
  exit 1
fi
VERSION="unknown"
[[ -f "$ARCHIVE_ROOT/VERSION" ]] && VERSION="$(tr -d '[:space:]' < "$ARCHIVE_ROOT/VERSION")"
smoke_pass "extract" "$(basename "$ARCHIVE_ROOT") (VERSION $VERSION)"

# --------------------------------------------------------------------------- #
# 2. install.sh
# --------------------------------------------------------------------------- #
smoke_log "install.sh --prefix $PREFIX"
if [[ ! -x "$ARCHIVE_ROOT/install.sh" ]]; then
  smoke_fail "install" "install.sh missing or not executable in the archive root"
  smoke_summary "TARBALL SMOKE"
  exit 1
fi

if ( cd "$ARCHIVE_ROOT" && ./install.sh --prefix "$PREFIX" ) >"$LOG_DIR/install.out" 2>&1; then
  sed 's/^/    | /' "$LOG_DIR/install.out"
  smoke_pass "install" "installed to $LIB_DIR"
else
  tail -n 20 "$LOG_DIR/install.out" | sed 's/^/    | /'
  smoke_fail "install" "install.sh exited non-zero (see $LOG_DIR/install.out)"
  smoke_summary "TARBALL SMOKE"
  exit 1
fi

# --------------------------------------------------------------------------- #
# 3. Payload + symlink
# --------------------------------------------------------------------------- #
smoke_log "Installed payload"
APP_DIR="$LIB_DIR/libexec/orkeon"
smoke_assert_payload "$APP_DIR" "$LIB_DIR/libexec/esbuild-bin/esbuild"

# install.sh symlinks every launcher in <lib>/bin into <prefix>/bin. The launcher
# walks that symlink to find its root, which is what lets the bundled esbuild be
# discovered — assert the link exists and points where we think it does.
LINK="$BIN_DIR/orkeon"
if [[ -L "$LINK" ]]; then
  target="$(readlink "$LINK")"
  case "$target" in
    "$LIB_DIR"/*) smoke_pass "symlink" "$LINK -> $target" ;;
    *)            smoke_fail "symlink" "$LINK points outside the install: $target" ;;
  esac
else
  smoke_fail "symlink" "$LINK is not a symlink (install.sh did not link the launcher)"
fi

# A fresh session: put <prefix>/bin on the PATH and let the shell resolve the
# command, exactly as a user would after opening a new terminal.
export PATH="$BIN_DIR:$PATH"
RESOLVED="$(command -v orkeon 2>/dev/null || true)"
if [[ "$RESOLVED" == "$LINK" ]]; then
  smoke_pass "resolve" "orkeon resolves to $RESOLVED"
else
  smoke_fail "resolve" "orkeon did not resolve to $LINK (got: ${RESOLVED:-nothing})"
fi
# Exercise the symlink even when resolution disagreed, so the later steps still
# produce signal rather than cascading on a PATH detail.
ORKEON_BIN="$LINK"

# --------------------------------------------------------------------------- #
# 3b. Orkeon Studio — present or absent, per the RID filter (STUDIO-08)
# --------------------------------------------------------------------------- #
# `auto` reads the archive's own name, which encodes both the app set and the
# RID: only the *cli* set on osx ships without Studio, because the WPF app is the
# single entry with a RID filter (win-x64) while the two TUIs have none and
# therefore ride along in the full app-set everywhere. CI passes --studio
# explicitly, so a rename of the archives can never silently downgrade the macOS
# guard into "nothing to check".
if [[ "$STUDIO_MODE" == auto ]]; then
  case "$(basename "$ARCHIVE_ROOT")" in
    orkeon-cli-*-osx-*) STUDIO_MODE=absent ;;
    *)                  STUDIO_MODE=present ;;
  esac
  smoke_info "--studio auto -> $STUDIO_MODE (from $(basename "$ARCHIVE_ROOT"))"
fi

if [[ "$STUDIO_MODE" == present ]]; then
  smoke_step_studio_present "$BIN_DIR"
else
  # Both the archive as published and the tree install.sh laid down: a Studio
  # binary that reached either one is the RID-filter regression this guards.
  smoke_step_studio_absent "$ARCHIVE_ROOT" "$LIB_DIR" "$BIN_DIR"
fi

# --------------------------------------------------------------------------- #
# 4-7. init / doctor / run / rag — shared with the deb smoke
# --------------------------------------------------------------------------- #
smoke_step_init
smoke_step_doctor
smoke_step_run
smoke_step_rag

# --------------------------------------------------------------------------- #
# 8. install.sh --uninstall
# --------------------------------------------------------------------------- #
smoke_log "install.sh --uninstall"
# install.sh copies itself into the install tree precisely so it can be re-run
# once the extracted archive is gone — that installed copy is what a user has,
# so that is the one under test.
INSTALLED_SH="$LIB_DIR/install.sh"
if [[ -f "$INSTALLED_SH" ]]; then
  smoke_pass "uninstall-copy" "install.sh travelled into $LIB_DIR"
  UNINSTALL_SH="$INSTALLED_SH"
else
  smoke_fail "uninstall-copy" "install.sh was not copied into $LIB_DIR (a user could not uninstall)"
  UNINSTALL_SH="$ARCHIVE_ROOT/install.sh"
fi

if sh "$UNINSTALL_SH" --prefix "$PREFIX" --uninstall >"$LOG_DIR/uninstall.out" 2>&1; then
  sed 's/^/    | /' "$LOG_DIR/uninstall.out"
  leftovers=""
  [[ -e "$LIB_DIR" ]] && leftovers="$leftovers $LIB_DIR still exists;"
  [[ -e "$LINK" ]] && leftovers="$leftovers $LINK still exists;"
  if [[ -z "$leftovers" ]]; then
    smoke_pass "uninstall" "lib directory and launcher symlinks removed"
  else
    smoke_fail "uninstall" "$leftovers"
  fi
else
  tail -n 20 "$LOG_DIR/uninstall.out" | sed 's/^/    | /'
  smoke_fail "uninstall" "install.sh --uninstall exited non-zero"
fi

if [[ -f "$SMOKE_CONFIG_FILE" ]]; then
  smoke_pass "config-preserved" "$SMOKE_CONFIG_FILE survived the uninstall"
else
  smoke_fail "config-preserved" "$SMOKE_CONFIG_FILE was removed — user configuration must never be touched"
fi

smoke_restore_user_config

# --------------------------------------------------------------------------- #
# Summary
# --------------------------------------------------------------------------- #
if [[ "$SMOKE_FAILURES" -ne 0 ]]; then
  smoke_info "logs kept in $LOG_DIR"
fi
smoke_summary "TARBALL SMOKE"
exit $?
