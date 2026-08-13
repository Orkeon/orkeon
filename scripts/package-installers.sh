#!/usr/bin/env bash
# Builds per-OS installer archives (tar.gz / zip) containing all Orkeon CLI
# executables, published per RID. Some ship self-contained (the `orkeon`
# onboarding binary, `orkeon-trading`), the rest framework-dependent — see APPS.
#
# Usage:
#   scripts/package-installers.sh [--version X.Y.Z[-suffix]] [--rids "linux-x64 ..."]
#                                 [--out artifacts/installers] [-c Release]
#                                 [--app-set full|cli] [--keep-stage all|none|"RIDS"]
#
# --app-set cli ships the `orkeon` CLI plus the Orkeon Studio apps for the
# platform (win-x64: `orkeon-studio`; linux-*: `orkeon-studio-config` +
# `orkeon-studio-run`; osx-*: CLI only) as orkeon-cli-<ver>-<rid>.{zip,tar.gz};
# full (the default) keeps the historical every-app archive.
#
# --keep-stage decides what becomes of the per-RID staging trees under
# $OUT/_stage once their archive has been written: `all` (the default) keeps
# every one of them, `none` deletes each tree as soon as its archive exists, and
# a space-separated RID list keeps only the trees named. The trees are
# by-products — the archive is the artifact — but package-deb.sh --stage reuses
# one instead of publishing it a second time, so pruning is opt-in and the
# caller states which trees still have a consumer. The full set alone leaves
# ~2.6 GB of them behind, which the release runner does not have to spare.
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
APP_SET="full"
KEEP_STAGE="all"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    --rids)    RIDS="$2"; shift 2 ;;
    --out)     OUT="$2"; shift 2 ;;
    --app-set) APP_SET="$2"; shift 2 ;;
    --keep-stage) KEEP_STAGE="$2"; shift 2 ;;
    -c|--configuration) CONFIG="$2"; shift 2 ;;
    -h|--help) sed -n '2,25p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

case "$APP_SET" in
  full|cli) ;;
  *) echo "Unknown --app-set '$APP_SET' (expected: full, cli)" >&2; exit 2 ;;
esac

# A RID kept by name but never built is a typo, and a typo here silently prunes
# the tree a downstream consumer was counting on. Fail on it instead.
case "$KEEP_STAGE" in
  all|none) ;;
  *)
    for keep_rid in $KEEP_STAGE; do
      [[ " $RIDS " == *" $keep_rid "* ]] \
        || { echo "--keep-stage names '$keep_rid', which is not among --rids ($RIDS)" >&2; exit 2; }
    done ;;
esac

# True when the staging tree of RID $1 must survive its archive (see --keep-stage).
stage_kept() { # $1=rid
  case "$KEEP_STAGE" in
    all)  return 0 ;;
    none) return 1 ;;
    *)    [[ " $KEEP_STAGE " == *" $1 "* ]] ;;
  esac
}

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

# --- App table: name | csproj (repo-relative) | apphost assembly name | self-contained | rids
# self-contained=true bundles the .NET runtime so end users need no SDK/runtime
# install. The `orkeon` CLI ships in two flavours from the *same* csproj:
#   - `orkeon`      self-contained — the onboarding channel, no .NET runtime needed;
#   - `orkeon-slim` framework-dependent — smaller, for devs who already have .NET 10.
# `orkeon-trading` opts into self-contained too; the remaining CLI tools stay
# framework-dependent. Both `orkeon` flavours share the one bundled esbuild
# (see fetch_esbuild below — fetched once per RID into libexec/esbuild-bin).
# The optional 5th column is a space-separated RID filter: empty = publish for
# every RID (the historical behaviour); non-empty = publish only for the listed
# RIDs. The WPF `orkeon-studio` is the motivating case — WPF cannot target
# non-Windows RIDs, so publishing it for linux/osx would fail the whole run.
APPS=(
  "orkeon|src/scripting/Orkeon.Scripting.Cli/Orkeon.Scripting.Cli.csproj|orkeon|true"
  "orkeon-slim|src/scripting/Orkeon.Scripting.Cli/Orkeon.Scripting.Cli.csproj|orkeon|false"
  "orkeon-repl|src/apps/Orkeon.ConsoleApp/Orkeon.ConsoleApp.csproj|Orkeon.ConsoleApp|false"
  "orkeon-trading|examples/runners/trading/Orkeon.Examples.Trading.Runner.csproj|Orkeon.Examples.Trading.Runner|true"
  "orkeon-interactive|examples/runners/interactive/Orkeon.Examples.Interactive.csproj|Orkeon.Examples.Interactive|false"
  "orkeon-tui-keytest|examples/runners/tui-keytest/Orkeon.Examples.TuiKeyTest.csproj|Orkeon.Examples.TuiKeyTest|false"
  "orkeon-claim-verify|examples/runners/interactive-claim-verification/Orkeon.Examples.Interactive.ClaimVerification.csproj|Orkeon.Examples.Interactive.ClaimVerification|false"
  "orkeon-spec-forge|examples/runners/interactive-interview-spec-forge/Orkeon.Examples.Interactive.InterviewSpecForge.csproj|Orkeon.Examples.Interactive.InterviewSpecForge|false"
  "orkeon-studio|src/apps/Orkeon.Studio.Wpf/Orkeon.Studio.Wpf.csproj|Orkeon.Studio|true|win-x64"
  "orkeon-studio-config|src/apps/Orkeon.Studio.Config/Orkeon.Studio.Config.csproj|Orkeon.Studio.Config|true|"
  "orkeon-studio-run|src/apps/Orkeon.Studio.Run/Orkeon.Studio.Run.csproj|Orkeon.Studio.Run|true|"
)

# True when the app's RID filter (5th column) admits $2; empty filter = all RIDs.
rid_allowed() { # $1=rids-filter $2=rid
  [[ -z "$1" || " $1 " == *" $2 "* ]]
}

# --app-set cli ships the onboarding binary plus the Orkeon Studio apps for the
# platform: win-* adds the WPF `orkeon-studio`, linux-* adds the two TUIs
# (`orkeon-studio-config` / `orkeon-studio-run`), osx-* stays CLI-only (V1 —
# the Homebrew channel does not ship Studio yet). Same staging layout, same
# wrappers, same installer — the sets stay structurally interchangeable for
# install.sh/ps1, which iterate over whatever bin/ contains.
cli_set_includes() { # $1=app-name $2=rid
  case "$1" in
    orkeon) return 0 ;;
    orkeon-studio) [[ "$2" == win-* ]] ;;
    orkeon-studio-config|orkeon-studio-run) [[ "$2" == linux-* ]] ;;
    *) return 1 ;;
  esac
}

if [[ "$APP_SET" == "cli" ]]; then
  ORKEON_ENTRIES=0
  for entry in "${APPS[@]}"; do
    [[ "${entry%%|*}" == "orkeon" ]] && ORKEON_ENTRIES=$((ORKEON_ENTRIES + 1))
  done
  [[ "$ORKEON_ENTRIES" -eq 1 ]] || { echo "Expected exactly one 'orkeon' entry in APPS, found $ORKEON_ENTRIES" >&2; exit 1; }
  PKG_PREFIX="orkeon-cli"
else
  PKG_PREFIX="orkeon"
fi

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

# make_zip and the python fallback cd into the stage dir, so OUT must be
# absolute (CI passes a relative --out, which broke the zip step).
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"

STAGE="$OUT/_stage"
CACHE="$OUT/_esbuild-cache"
mkdir -p "$STAGE" "$CACHE"

echo "==> Packaging Orkeon $VERSION ($APP_SET set, esbuild $ESBUILD_VERSION) for: $RIDS"

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
  PKG="$PKG_PREFIX-$VERSION-$RID"
  ROOT="$STAGE/$PKG"
  rm -rf "$ROOT"
  mkdir -p "$ROOT/bin" "$ROOT/libexec"
  echo "==> $RID"

  for entry in "${APPS[@]}"; do
    IFS='|' read -r name csproj apphost selfcontained rids <<<"$entry"
    selfcontained="${selfcontained:-false}"
    if ! rid_allowed "${rids:-}" "$RID"; then
      echo "    skip $name (RID filter: ${rids})"
      continue
    fi
    if [[ "$APP_SET" == "cli" ]] && ! cli_set_includes "$name" "$RID"; then
      continue
    fi
    echo "    publish $name (self-contained=$selfcontained)"
    dotnet publish "$REPO_ROOT/$csproj" -c "$CONFIG" -r "$RID" \
      --self-contained "$selfcontained" -p:PublishTrimmed=false \
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
  # Plain-text version marker: install.ps1 reads it for the Add/Remove Programs
  # entry, and it lets a user identify an already-extracted tree.
  printf '%s\n' "$VERSION" > "$ROOT/VERSION"
  # Reference config only. The live one lives in %APPDATA%\Orkeon (or
  # $XDG_CONFIG_HOME/orkeon); this copy is here to be read, not loaded.
  cp "$REPO_ROOT/examples/appsettings/appsettings.json" "$ROOT/appsettings.sample.json"
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

  # Drop the staging tree now that its archive exists, unless the caller kept
  # this RID (--keep-stage). Freeing it here rather than after the loop is what
  # makes it useful: the disk peak becomes the kept trees plus the one being
  # built, instead of one tree per RID.
  if ! stage_kept "$RID"; then
    rm -rf "$ROOT"
    echo "    pruned staging tree $ROOT"
  fi
done

# `|| true`: with a single --rids one of the globs matches nothing, and under
# `set -o pipefail` a failing ls would abort the script after all the work is
# already done. *.deb keeps the checksums complete when package-deb.sh has
# dropped its package in the same output directory.
(cd "$OUT" && { ls *.tar.gz *.zip *.deb 2>/dev/null || true; } | xargs -r sha256sum > SHA256SUMS)
echo "==> Done. Artifacts in $OUT:"
(cd "$OUT" && ls -lh *.tar.gz *.zip *.deb SHA256SUMS 2>/dev/null || true)
