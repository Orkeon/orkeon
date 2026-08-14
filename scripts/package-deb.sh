#!/usr/bin/env bash
# Builds the Debian package orkeon_<ver>_amd64.deb from the self-contained,
# tree-sitter-pruned linux-x64 publish of the `orkeon` CLI.
#
# Usage:
#   scripts/package-deb.sh [--version X.Y.Z[-suffix]] [--out artifacts/installers]
#                          [-c Release] [--stage DIR] [--keep-work]
#
# The publish / pruning / esbuild-fetch logic is NOT duplicated here: this
# script drives scripts/package-installers.sh --app-set cli --rids linux-x64 and
# remaps its staging tree onto the Debian layout. Pass --stage to reuse a
# staging tree already built by that script (CI publishes once, packages twice).
#
# Layout: payload in /usr/lib/orkeon, launcher /usr/bin/orkeon; the Orkeon
# Studio TUIs land in /usr/lib/orkeon-studio-{config,run} with launchers
# /usr/bin/orkeon-studio-{config,run}. Everything is self-contained, so the
# package depends on system libraries only — never on dotnet-runtime-*.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

VERSION=""
OUT="$REPO_ROOT/artifacts/installers"
CONFIG="Release"
STAGE_IN=""
KEEP_WORK=false

MAINTAINER="Orkeon Contributors <cyril@egnx.com>"
HOMEPAGE="https://github.com/Orkeon"
# apt resolves these at install time; the names differ across distributions, so
# each family is an alternation covering Debian 12/13 and Ubuntu 22.04→26.04.
#   ICU      — .NET needs it unless built with InvariantGlobalization;
#   OpenSSL  — Ubuntu 24.04 renamed libssl3 to libssl3t64 (time_t transition).
DEPENDS="libicu76 | libicu74 | libicu72 | libicu70, libssl3t64 | libssl3, zlib1g, libgcc-s1, libc6 (>= 2.34), ca-certificates"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    --out)     OUT="$2"; shift 2 ;;
    --stage)   STAGE_IN="$2"; shift 2 ;;
    --keep-work) KEEP_WORK=true; shift ;;
    -c|--configuration) CONFIG="$2"; shift 2 ;;
    -h|--help) sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

command -v dpkg-deb >/dev/null 2>&1 || { echo "dpkg-deb not found (install the 'dpkg' package)." >&2; exit 1; }

mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
WORK="$OUT/_deb-work"

# --- 1. Payload: reuse the CLI archive staging tree ---------------------------
if [[ -n "$STAGE_IN" ]]; then
  SRC_STAGE="$(cd "$STAGE_IN" && pwd)"
else
  rm -rf "$WORK"
  mkdir -p "$WORK"
  echo "==> Publishing the CLI payload via package-installers.sh (--app-set cli, linux-x64)"
  "$REPO_ROOT/scripts/package-installers.sh" \
    --app-set cli --rids linux-x64 --out "$WORK" -c "$CONFIG" \
    ${VERSION:+--version "$VERSION"}
  # One staging root per RID; the version is baked into its name, so glob it and
  # read the version back from the VERSION marker rather than resolving twice.
  mapfile -t stages < <(find "$WORK/_stage" -mindepth 1 -maxdepth 1 -type d -name 'orkeon-cli-*-linux-x64')
  [[ ${#stages[@]} -eq 1 ]] || { echo "Expected exactly one linux-x64 staging tree under $WORK/_stage, found ${#stages[@]}" >&2; exit 1; }
  SRC_STAGE="${stages[0]}"
fi

[[ -x "$SRC_STAGE/libexec/orkeon/orkeon" ]] || { echo "Missing apphost $SRC_STAGE/libexec/orkeon/orkeon" >&2; exit 1; }
[[ -f "$SRC_STAGE/libexec/esbuild-bin/esbuild" ]] || { echo "Missing $SRC_STAGE/libexec/esbuild-bin/esbuild" >&2; exit 1; }

if [[ -z "$VERSION" ]]; then
  [[ -f "$SRC_STAGE/VERSION" ]] || { echo "No VERSION marker in $SRC_STAGE; pass --version." >&2; exit 1; }
  VERSION="$(tr -d '[:space:]' < "$SRC_STAGE/VERSION")"
fi
[[ -n "$VERSION" ]] || { echo "Could not resolve a version; pass --version." >&2; exit 1; }

# dpkg orders `~` before everything, including the empty string: 0.9.2~beta thus
# sorts *before* the 0.9.2 final, which is what a pre-release must do.
DEB_VERSION="${VERSION//-/\~}"
PKG_DIR="$OUT/_deb-stage/orkeon_${DEB_VERSION}_amd64"
DEB_PATH="$OUT/orkeon_${DEB_VERSION}_amd64.deb"

echo "==> Staging orkeon $DEB_VERSION (upstream $VERSION) for amd64"
rm -rf "$PKG_DIR"
mkdir -p "$PKG_DIR/DEBIAN" "$PKG_DIR/usr/bin" "$PKG_DIR/usr/lib/orkeon" "$PKG_DIR/usr/share/doc/orkeon"

cp -R "$SRC_STAGE/libexec/orkeon/." "$PKG_DIR/usr/lib/orkeon/"
mkdir -p "$PKG_DIR/usr/lib/orkeon/esbuild-bin"
cp "$SRC_STAGE/libexec/esbuild-bin/esbuild" "$PKG_DIR/usr/lib/orkeon/esbuild-bin/esbuild"

# --- 1b. Orkeon Studio TUI payloads (STUDIO-07) --------------------------------
# The linux cli staging tree ships the two Terminal.Gui Studio apps next to the
# CLI (see cli_set_includes in package-installers.sh). Both are self-contained,
# so Depends stays free of dotnet-runtime-* — the package invariant holds.
STUDIO_APPS="orkeon-studio-config orkeon-studio-run"
studio_apphost() { # $1=app-name -> apphost file name (AssemblyName)
  case "$1" in
    orkeon-studio-config) echo "Orkeon.Studio.Config" ;;
    orkeon-studio-run)    echo "Orkeon.Studio.Run" ;;
  esac
}
for app in $STUDIO_APPS; do
  apphost="$(studio_apphost "$app")"
  [[ -x "$SRC_STAGE/libexec/$app/$apphost" ]] || { echo "Missing apphost $SRC_STAGE/libexec/$app/$apphost (staging tree predates STUDIO-07?)" >&2; exit 1; }
  mkdir -p "$PKG_DIR/usr/lib/$app"
  cp -R "$SRC_STAGE/libexec/$app/." "$PKG_DIR/usr/lib/$app/"
done

# --- 2. Launcher --------------------------------------------------------------
# Same contract as scripts/installer-assets/wrapper.sh.tmpl (bundled esbuild,
# then exec the apphost) minus the symlink walk: an installed package always
# lives at a fixed prefix, so the path is hard-coded.
cat > "$PKG_DIR/usr/bin/orkeon" <<'EOF'
#!/bin/sh
# Orkeon launcher — installed by the orkeon Debian package.
# Points the scripting toolchain at the bundled esbuild, then execs the apphost.
ORKEON_HOME=/usr/lib/orkeon
if [ -z "${ORKEON_ESBUILD_PATH:-}" ] && [ -x "$ORKEON_HOME/esbuild-bin/esbuild" ]; then
  ORKEON_ESBUILD_PATH="$ORKEON_HOME/esbuild-bin/esbuild"
  export ORKEON_ESBUILD_PATH
fi
exec "$ORKEON_HOME/orkeon" "$@"
EOF

# Studio TUI launchers — no esbuild indirection needed (the TUIs never invoke
# the scripting toolchain themselves; they spawn /usr/bin/orkeon, which does).
for app in $STUDIO_APPS; do
  apphost="$(studio_apphost "$app")"
  cat > "$PKG_DIR/usr/bin/$app" <<EOF
#!/bin/sh
# $app launcher — installed by the orkeon Debian package.
exec /usr/lib/$app/$apphost "\$@"
EOF
done

# --- 3. Documentation ---------------------------------------------------------
cat > "$PKG_DIR/usr/share/doc/orkeon/copyright" <<EOF
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: Orkeon
Source: $HOMEPAGE

Files: *
Copyright: 2024 Orkeon Contributors
License: MIT

License: MIT
 Permission is hereby granted, free of charge, to any person obtaining a copy
 of this software and associated documentation files (the "Software"), to deal
 in the Software without restriction, including without limitation the rights
 to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 copies of the Software, and to permit persons to whom the Software is
 furnished to do so, subject to the following conditions:
 .
 The above copyright notice and this permission notice shall be included in all
 copies or substantial portions of the Software.
 .
 THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 SOFTWARE.
EOF

# SOURCE_DATE_EPOCH keeps the changelog stamp reproducible when CI sets it.
if [[ -n "${SOURCE_DATE_EPOCH:-}" ]]; then
  CHANGELOG_DATE="$(date -u -R -d "@$SOURCE_DATE_EPOCH")"
else
  CHANGELOG_DATE="$(date -R)"
fi
cat > "$PKG_DIR/usr/share/doc/orkeon/changelog.Debian" <<EOF
orkeon ($DEB_VERSION) unstable; urgency=medium

  * Orkeon CLI $VERSION, packaged self-contained (bundled .NET runtime,
    bundled esbuild, tree-sitter grammars pruned to the supported set).

 -- $MAINTAINER  $CHANGELOG_DATE
EOF
gzip -9n "$PKG_DIR/usr/share/doc/orkeon/changelog.Debian"

# --- 4. Permissions -----------------------------------------------------------
# Reset everything explicitly: the staging tree may come off an NTFS mount where
# every file reads 0777, and dpkg-deb records whatever mode it finds.
find "$PKG_DIR" -type d -exec chmod 755 {} +
find "$PKG_DIR" -type f -exec chmod 644 {} +
chmod 755 "$PKG_DIR/usr/bin/orkeon" \
          "$PKG_DIR/usr/lib/orkeon/orkeon" \
          "$PKG_DIR/usr/lib/orkeon/esbuild-bin/esbuild"
find "$PKG_DIR/usr/lib/orkeon" -type f -name '*.so' -exec chmod 755 {} +
if [[ -f "$PKG_DIR/usr/lib/orkeon/createdump" ]]; then
  chmod 755 "$PKG_DIR/usr/lib/orkeon/createdump"
fi
for app in $STUDIO_APPS; do
  apphost="$(studio_apphost "$app")"
  chmod 755 "$PKG_DIR/usr/bin/$app" "$PKG_DIR/usr/lib/$app/$apphost"
  find "$PKG_DIR/usr/lib/$app" -type f -name '*.so' -exec chmod 755 {} +
  if [[ -f "$PKG_DIR/usr/lib/$app/createdump" ]]; then
    chmod 755 "$PKG_DIR/usr/lib/$app/createdump"
  fi
done

# --- 4b. Payload dedup --------------------------------------------------------
# dpkg preserves hard links, so the byte-identical files the three
# self-contained payloads share (.NET runtime, common Orkeon assemblies) are
# stored and unpacked once. Runs after the permission reset so the shared
# inodes keep the settled modes, and before Installed-Size so the estimate
# reflects what dpkg actually lays down.
"$REPO_ROOT/scripts/hardlink-dedup.sh" "$PKG_DIR/usr/lib"

# --- 5. Control ---------------------------------------------------------------
# Policy 5.6.20: installed size is an estimate in KiB, excluding DEBIAN/.
INSTALLED_SIZE="$(du -sk --exclude=DEBIAN "$PKG_DIR" | cut -f1)"
cat > "$PKG_DIR/DEBIAN/control" <<EOF
Package: orkeon
Version: $DEB_VERSION
Architecture: amd64
Section: devel
Priority: optional
Maintainer: $MAINTAINER
Installed-Size: $INSTALLED_SIZE
Depends: $DEPENDS
Homepage: $HOMEPAGE
Description: multi-agent AI orchestration framework and CLI
 Orkeon builds teams of LLM agents that collaborate on a goal: sequential,
 hierarchical, parallel, consensual, graph and autonomous orchestration, a
 tool system, memory providers and a retrieval-augmented generation pipeline.
 .
 The orkeon command line tool can run TypeScript-scripted crews (orkeon run),
 ingest, search and evaluate a RAG corpus (orkeon rag ingest / rag search /
 rag eval), scaffold a new workspace (orkeon init) and diagnose an install
 (orkeon doctor).
 .
 The package also ships the Orkeon Studio console apps: orkeon-studio-config
 (guided appsettings editor) and orkeon-studio-run (crew launcher with live
 logs), both built on Terminal.Gui.
 .
 This package is self-contained: the .NET runtime and the esbuild toolchain
 ship inside it, so no .NET installation or third-party repository is needed.
EOF
chmod 644 "$PKG_DIR/DEBIAN/control"

# --- 6. Build -----------------------------------------------------------------
rm -f "$DEB_PATH"
dpkg-deb --build --root-owner-group "$PKG_DIR" "$DEB_PATH"

# SHA256SUMS is shared with the archive artifacts: refresh our line, keep theirs.
(
  cd "$OUT"
  deb="$(basename "$DEB_PATH")"
  touch SHA256SUMS
  grep -v "  $deb\$" SHA256SUMS > SHA256SUMS.tmp || true
  sha256sum "$deb" >> SHA256SUMS.tmp
  sort -k2 SHA256SUMS.tmp > SHA256SUMS
  rm -f SHA256SUMS.tmp
)

# _deb-stage is kept (the package tree, inspectable like package-installers.sh's
# _stage); _deb-work is not — it holds a second copy of the publish plus a
# tar.gz that is *not* a release artifact and would only be mistaken for one.
if [[ "$KEEP_WORK" != true && -z "$STAGE_IN" ]]; then
  rm -rf "$WORK"
fi

ts_count="$(find "$PKG_DIR/usr/lib/orkeon" -name 'libtree-sitter*' | wc -l)"
echo "==> Done: $DEB_PATH"
echo "    installed size: ${INSTALLED_SIZE} KiB, tree-sitter libraries: $ts_count"
ls -lh "$DEB_PATH"
