#!/usr/bin/env bash
# Builds per-OS installer archives (tar.gz / zip) containing all Orkeon CLI
# executables, framework-dependent, published per RID.
#
# Usage:
#   scripts/package-installers.sh [--version X.Y.Z[-suffix]] [--rids "linux-x64 ..."]
#                                 [--out artifacts/installers] [-c Release]
#
# Version resolution: --version > git describe (v-stripped) > src/Directory.Build.props.
# esbuild is fetched per-RID straight from the npm registry (no npm/node needed);
# the version comes from tools/scripting-esbuild/package-lock.json.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ASSETS="$REPO_ROOT/scripts/installer-assets"

VERSION=""
RIDS="linux-x64 linux-arm64 win-x64 osx-x64 osx-arm64"
OUT="$REPO_ROOT/artifacts/installers"
CONFIG="Release"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    --rids)    RIDS="$2"; shift 2 ;;
    --out)     OUT="$2"; shift 2 ;;
    -c|--configuration) CONFIG="$2"; shift 2 ;;
    -h|--help) sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

# --- Version -----------------------------------------------------------------
if [[ -z "$VERSION" ]]; then
  VERSION="$(git -C "$REPO_ROOT" describe --tags --abbrev=0 2>/dev/null | sed 's/^v//' || true)"
fi
if [[ -z "$VERSION" ]]; then
  props="$REPO_ROOT/src/Directory.Build.props"
  prefix="$(sed -n 's/.*<VersionPrefix>\(.*\)<\/VersionPrefix>.*/\1/p' "$props" | head -1)"
  suffix="$(sed -n 's/.*<VersionSuffix>\(.*\)<\/VersionSuffix>.*/\1/p' "$props" | head -1)"
  VERSION="${prefix}${suffix:+-$suffix}"
fi
[[ -n "$VERSION" ]] || { echo "Could not resolve a version; pass --version." >&2; exit 1; }

# --- esbuild version (single source of truth: the lockfile) -------------------
ESBUILD_VERSION="$(grep -A1 '"node_modules/esbuild"' "$REPO_ROOT/tools/scripting-esbuild/package-lock.json" 2>/dev/null \
  | sed -n 's/.*"version": "\([^"]*\)".*/\1/p' | head -1)"
ESBUILD_VERSION="${ESBUILD_VERSION:-0.24.0}"

# --- App table: name | csproj (repo-relative) | apphost assembly name ---------
APPS=(
  "orkeon|src/scripting/Orkeon.Scripting.Cli/Orkeon.Scripting.Cli.csproj|orkeon"
  "orkeon-repl|src/apps/Orkeon.ConsoleApp/Orkeon.ConsoleApp.csproj|Orkeon.ConsoleApp"
  "orkeon-examples|examples/runners/standard/Orkeon.Examples.Runner.csproj|Orkeon.Examples.Runner"
  "orkeon-trading|examples/runners/trading/Orkeon.Examples.Trading.Runner.csproj|Orkeon.Examples.Trading.Runner"
  "orkeon-interactive|examples/runners/interactive/Orkeon.Examples.Interactive.csproj|Orkeon.Examples.Interactive"
  "orkeon-tui-keytest|examples/runners/tui-keytest/Orkeon.Examples.TuiKeyTest.csproj|Orkeon.Examples.TuiKeyTest"
  "orkeon-claim-verify|examples/runners/interactive-claim-verification/Orkeon.Examples.Interactive.ClaimVerification.csproj|Orkeon.Examples.Interactive.ClaimVerification"
  "orkeon-spec-forge|examples/runners/interactive-interview-spec-forge/Orkeon.Examples.Interactive.InterviewSpecForge.csproj|Orkeon.Examples.Interactive.InterviewSpecForge"
)

# RID -> npm platform package for @esbuild/*
esbuild_npm_rid() {
  case "$1" in
    linux-x64)   echo "linux-x64" ;;
    linux-arm64) echo "linux-arm64" ;;
    win-x64)     echo "win32-x64" ;;
    osx-x64)     echo "darwin-x64" ;;
    osx-arm64)   echo "darwin-arm64" ;;
    *) echo ""; return 1 ;;
  esac
}

STAGE="$OUT/_stage"
CACHE="$OUT/_esbuild-cache"
mkdir -p "$OUT" "$STAGE" "$CACHE"

echo "==> Packaging Orkeon $VERSION (esbuild $ESBUILD_VERSION) for: $RIDS"

fetch_esbuild() { # $1=rid $2=dest-dir
  local npmrid dest tgz pkgdir bin
  npmrid="$(esbuild_npm_rid "$1")" || { echo "No esbuild mapping for RID $1" >&2; return 1; }
  dest="$2"
  pkgdir="$CACHE/$npmrid-$ESBUILD_VERSION"
  if [[ ! -d "$pkgdir" ]]; then
    tgz="$CACHE/$npmrid-$ESBUILD_VERSION.tgz"
    echo "    fetching @esbuild/$npmrid@$ESBUILD_VERSION"
    curl -fsSL "https://registry.npmjs.org/@esbuild/$npmrid/-/$npmrid-$ESBUILD_VERSION.tgz" -o "$tgz"
    mkdir -p "$pkgdir"
    tar -xzf "$tgz" -C "$pkgdir"
  fi
  mkdir -p "$dest"
  if [[ "$1" == win-* ]]; then
    bin="$pkgdir/package/esbuild.exe"
    cp "$bin" "$dest/esbuild.exe"
  else
    bin="$pkgdir/package/bin/esbuild"
    cp "$bin" "$dest/esbuild"
    chmod +x "$dest/esbuild"
  fi
}

make_zip() { # $1=parent-dir $2=folder-name $3=zip-path
  if command -v zip >/dev/null 2>&1; then
    (cd "$1" && zip -rq "$3" "$2")
  else
    (cd "$1" && python3 -c "
import shutil, sys
shutil.make_archive(sys.argv[1].removesuffix('.zip'), 'zip', '.', sys.argv[2])
" "$3" "$2")
  fi
}

for RID in $RIDS; do
  PKG="orkeon-$VERSION-$RID"
  ROOT="$STAGE/$PKG"
  rm -rf "$ROOT"
  mkdir -p "$ROOT/bin" "$ROOT/libexec"
  echo "==> $RID"

  for entry in "${APPS[@]}"; do
    IFS='|' read -r name csproj apphost <<<"$entry"
    echo "    publish $name"
    dotnet publish "$REPO_ROOT/$csproj" -c "$CONFIG" -r "$RID" --self-contained false \
      -p:Version="$VERSION" -p:SkipScriptingNpmInstall=true \
      -p:ErrorOnDuplicatePublishOutputFiles=false \
      -o "$ROOT/libexec/$name" --nologo -v quiet

    # Launcher wrapper
    if [[ "$RID" == win-* ]]; then
      sed -e "s/{{APP}}/$name/g" -e "s/{{APPHOST}}/$apphost/g" \
        "$ASSETS/wrapper.cmd.tmpl" > "$ROOT/bin/$name.cmd"
    else
      sed -e "s/{{APP}}/$name/g" -e "s/{{APPHOST}}/$apphost/g" \
        "$ASSETS/wrapper.sh.tmpl" > "$ROOT/bin/$name"
      chmod +x "$ROOT/bin/$name" "$ROOT/libexec/$name/$apphost"
    fi
  done

  fetch_esbuild "$RID" "$ROOT/libexec/esbuild-bin"

  # Docs + installer
  sed -e "s/{{VERSION}}/$VERSION/g" -e "s/{{RID}}/$RID/g" \
    "$ASSETS/README.archive.md.tmpl" > "$ROOT/README.md"
  cp "$REPO_ROOT/LICENSE.md" "$ROOT/LICENSE.md"
  if [[ "$RID" == win-* ]]; then
    cp "$ASSETS/install.ps1" "$ROOT/install.ps1"
  else
    cp "$ASSETS/install.sh" "$ROOT/install.sh"
    chmod +x "$ROOT/install.sh"
  fi

  # Archive
  if [[ "$RID" == win-* ]]; then
    rm -f "$OUT/$PKG.zip"
    make_zip "$STAGE" "$PKG" "$OUT/$PKG.zip"
    echo "    -> $OUT/$PKG.zip"
  else
    tar -czf "$OUT/$PKG.tar.gz" -C "$STAGE" "$PKG"
    echo "    -> $OUT/$PKG.tar.gz"
  fi
done

(cd "$OUT" && { ls *.tar.gz *.zip 2>/dev/null | xargs -r sha256sum > SHA256SUMS; })
echo "==> Done. Artifacts in $OUT:"
(cd "$OUT" && ls -lh *.tar.gz *.zip SHA256SUMS 2>/dev/null)
