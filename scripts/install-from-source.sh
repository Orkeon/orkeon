#!/usr/bin/env bash
# Installs Orkeon from this clone, in one command (Linux and macOS;
# scripts/install-from-source.ps1 is the same contract for Windows).
#
#   git clone https://github.com/Orkeon/orkeon.git
#   cd orkeon
#   ./scripts/install-from-source.sh
#
# What it does, in this order:
#   1. checks what the build needs -- every missing tool is named at once, with
#      where to get it, before anything is compiled;
#   2. computes the version of this checkout (scripts/resolve-version.sh):
#      <props version>.local.<commit date> off a tag, never the name of a release;
#   3. builds the tree of an archive for this machine, without writing the archive
#      (scripts/package-installers.sh --no-archive);
#   4. installs that tree with the install.sh every archive carries, and marks the
#      installation `source`, the channel `orkeon doctor` then names.
#
# It installs what the released archive installs -- same layout, same launchers,
# esbuild included -- and nothing else: no runtime, no tool, no package is installed
# on the machine for you.
#
# Usage:
#   scripts/install-from-source.sh [--app-set cli|full] [--prefix DIR] [--modify-path]
#                                  [--uninstall] [--dry-run]
#
# --app-set      cli (the default): the `orkeon` CLI and, on Linux, the two Orkeon
#                Studio terminal apps. full: every launcher (the REPL, the service host).
# --prefix       where install.sh installs. Default: ~/.local (no sudo needed).
# --modify-path  passed to install.sh: appends <prefix>/bin to your shell rc files.
# --uninstall    removes what is installed under --prefix. Builds nothing.
# --dry-run      checks the prerequisites, prints the version and the commands it
#                would run, and stops there.
#
# Run it again after a `git pull`: the installation is replaced.
#
# Exit: 0 installed (or would be, with --dry-run); 1 a prerequisite is missing, or the
# build or the install failed; 2 on a usage error.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

APP_SET="cli"
PREFIX="${HOME}/.local"
MODIFY_PATH=0
UNINSTALL=0
DRY_RUN=0

usage() { echo "install-from-source: $*" >&2; exit 2; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --app-set)     [[ $# -ge 2 ]] || usage "--app-set takes cli or full"; APP_SET="$2"; shift 2 ;;
    --prefix)      [[ $# -ge 2 ]] || usage "--prefix takes a directory"; PREFIX="$2"; shift 2 ;;
    --prefix=*)    PREFIX="${1#--prefix=}"; shift ;;
    --modify-path) MODIFY_PATH=1; shift ;;
    --uninstall)   UNINSTALL=1; shift ;;
    --dry-run)     DRY_RUN=1; shift ;;
    -h|--help)     sed -n '2,38p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) usage "unknown argument $1" ;;
  esac
done

case "$APP_SET" in
  cli|full) ;;
  *) usage "--app-set takes cli or full, got '$APP_SET'" ;;
esac

step() { printf '\n==> %s\n' "$*"; }

# --- Uninstall: nothing to build -------------------------------------------------
# The installed tree carries its own install.sh for this (it travels with the rest).
if [[ "$UNINSTALL" -eq 1 ]]; then
  installed="$PREFIX/lib/orkeon/install.sh"
  if [[ ! -f "$installed" ]]; then
    echo "Nothing to uninstall: no Orkeon installation under $PREFIX (looked for $installed)." >&2
    exit 1
  fi
  if [[ "$DRY_RUN" -eq 1 ]]; then
    echo "Would run: sh $installed --prefix $PREFIX --uninstall"
    exit 0
  fi
  exec sh "$installed" --prefix "$PREFIX" --uninstall
fi

# --- The machine this installs for -----------------------------------------------
case "$(uname -s)" in
  Linux)  os="linux" ;;
  Darwin) os="osx" ;;
  *)
    echo "install-from-source: no Orkeon archive is built for '$(uname -s)'. On Windows, run scripts\\install-from-source.ps1 from PowerShell 7." >&2
    exit 1 ;;
esac
case "$(uname -m)" in
  x86_64|amd64)  arch="x64" ;;
  aarch64|arm64) arch="arm64" ;;
  *)
    echo "install-from-source: no Orkeon archive is built for the '$(uname -m)' architecture (x64 and arm64 are)." >&2
    exit 1 ;;
esac
RID="$os-$arch"

# --- Prerequisites, all of them, before anything is compiled -----------------------
# One list of what is missing, each line with where to get it -- not one error per run.
SDK_WANTED="$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$REPO_ROOT/global.json" 2>/dev/null | head -n1)"
SDK_WANTED="${SDK_WANTED:-10.0.300}"
SDK_LINE="${SDK_WANTED%.*}"

missing=()
if ! command -v dotnet >/dev/null 2>&1; then
  missing+=("the .NET SDK $SDK_WANTED or a later $SDK_LINE.x (global.json pins it) -- https://dotnet.microsoft.com/download/dotnet/$SDK_LINE")
elif ! (cd "$REPO_ROOT" && dotnet --version >/dev/null 2>&1); then
  # dotnet itself judges global.json: it answers --version only when an installed SDK
  # satisfies the pin. A runtime without an SDK fails here too.
  have="$( (cd / && dotnet --list-sdks 2>/dev/null) | sed 's/ .*//' | tr '\n' ' ' | sed 's/ $//')"
  missing+=("a .NET SDK that satisfies global.json: $SDK_WANTED or a later $SDK_LINE.x (installed: ${have:-none}) -- https://dotnet.microsoft.com/download/dotnet/$SDK_LINE")
fi
command -v git >/dev/null 2>&1 \
  || missing+=("git, which dates the version of this build -- https://git-scm.com/downloads")
command -v python3 >/dev/null 2>&1 \
  || missing+=("Python 3 (python3), which copies the .NET runtime's license and notices into the tree -- https://www.python.org/downloads/")
command -v tar >/dev/null 2>&1 \
  || missing+=("tar, which unpacks the esbuild binary the tree bundles -- your system's package manager (it ships with every Linux distribution and with macOS)")
command -v curl >/dev/null 2>&1 \
  || missing+=("curl, which fetches that esbuild binary from registry.npmjs.org -- https://curl.se/download.html, or your system's package manager")
command -v openssl >/dev/null 2>&1 \
  || missing+=("openssl, which checks that binary against the hash package-lock.json pins -- your system's package manager")

if [[ ${#missing[@]} -gt 0 ]]; then
  {
    echo "install-from-source: this machine lacks ${#missing[@]} thing(s) the build needs. Nothing was built."
    for item in "${missing[@]}"; do echo "  - $item"; done
    echo "Install what is listed, then run this script again. It installs none of it for you."
  } >&2
  exit 1
fi

# --- Version -----------------------------------------------------------------------
VERSION="$(bash "$REPO_ROOT/scripts/resolve-version.sh" --props "$REPO_ROOT/src/Directory.Build.props")" || VERSION=""
if [[ -z "$VERSION" ]]; then
  echo "install-from-source: scripts/resolve-version.sh gave no version for this checkout (see its message above)." >&2
  exit 1
fi

if [[ "$APP_SET" == "cli" ]]; then PKG_PREFIX="orkeon-cli"; else PKG_PREFIX="orkeon"; fi
OUT="$REPO_ROOT/artifacts/installers"
TREE="$OUT/_stage/$PKG_PREFIX-$VERSION-$RID"

PACK=(bash "$REPO_ROOT/scripts/package-installers.sh" --app-set "$APP_SET" --rids "$RID" --version "$VERSION" --out "$OUT" --no-archive)
INSTALL=(sh "$TREE/install.sh" --prefix "$PREFIX")
[[ "$MODIFY_PATH" -eq 1 ]] && INSTALL+=(--modify-path)

echo "Orkeon $VERSION, from the sources in $REPO_ROOT ($APP_SET set, $RID)."

# Debian and Ubuntu have a shorter way to the same builds: said, never imposed.
if [[ -r /etc/os-release ]] && grep -Eqi '^(ID|ID_LIKE)=.*(debian|ubuntu)' /etc/os-release; then
  echo "Debian / Ubuntu: the apt repository serves the builds of main that CI validated, on its 'dev'"
  echo "channel (docs/guides/install-with-apt.md). This script carries on and installs this clone."
fi

if [[ "$DRY_RUN" -eq 1 ]]; then
  echo "Prerequisites: all present. --dry-run: nothing is built, nothing is installed. Would run:"
  echo "  ${PACK[*]}"
  echo "  ${INSTALL[*]}"
  exit 0
fi

# --- Build the tree ------------------------------------------------------------------
step "Building the $RID tree (several minutes: one self-contained publish per application)"
echo "The first run restores NuGet packages from nuget.org and fetches the esbuild binary from"
echo "registry.npmjs.org; a later run downloads nothing that is already cached."
if ! "${PACK[@]}"; then
  echo "install-from-source: the build stopped (its message is above). Nothing was installed." >&2
  exit 1
fi

if [[ ! -f "$TREE/install.sh" ]]; then
  echo "install-from-source: package-installers.sh ended without the tree it was asked for ($TREE)." >&2
  exit 1
fi

# The channel is said by whoever packs (Orkeon.Constants.FileSystem.InstallChannels):
# package-installers.sh wrote `tarball`, the word of the archive this tree would have
# become. It never became one: this installation comes from a clone.
printf 'source\n' > "$TREE/INSTALL-CHANNEL"

# --- Install it ------------------------------------------------------------------------
step "Installing to $PREFIX"
if ! "${INSTALL[@]}"; then
  # install.sh's own sentence is above, as it wrote it.
  echo "install-from-source: install.sh did not complete; the tree it was installing is $TREE." >&2
  exit 1
fi

echo
echo "Orkeon $VERSION is installed from the sources (channel: source)."
echo "Open a new terminal, then: orkeon --version"
echo "After a 'git pull', run this script again: it replaces the installation."
