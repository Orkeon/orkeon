#!/usr/bin/env bash
# Regression test of scripts/check-release-assets.sh, the guard that keeps the
# release assets and their checksum manifests in agreement name for name. Each
# case builds a throw-away asset directory and states whether the guard must pass
# or fail, and on what. No network, no build. Run: bash scripts/test-check-release-assets.sh
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
guard="$here/check-release-assets.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

failed=0
check() { # <description> <command...>
  if "${@:2}"; then echo "ok    $1"; else echo "FAIL  $1"; failed=$((failed + 1)); fi
}
says() { grep -qF -- "$1" <<<"$out"; }
exits() { [ "$code" -eq "$1" ]; }

# A well-formed set: what the installers job leaves behind, MSIs in their own manifest.
fresh() {
  rm -rf "$work/d"; mkdir -p "$work/d"
  (
    cd "$work/d"
    echo deb > orkeon_1.0.0-rc.5_amd64.deb
    echo arm > orkeon_1.0.0-rc.5_arm64.deb
    echo tgz > orkeon-1.0.0-rc.5-linux-x64.tar.gz
    echo zip > orkeon-cli-1.0.0-rc.5-win-x64.zip
    echo sbom > orkeon-1.0.0-rc.5.sbom.cdx.json
    echo msi > orkeon-1.0.0-rc.5-win-x64.msi
    echo stray > notes.txt   # not an uploaded asset: outside the publish globs
    sha256sum orkeon_*.deb orkeon-*.tar.gz orkeon-*.zip orkeon-*.sbom.cdx.json > SHA256SUMS
    sha256sum orkeon-*.msi > SHA256SUMS.msi
  )
}
run() { # <guard arguments...>
  code=0
  out="$("$guard" "$@" 2>&1)" || code=$?
}

fresh; run "$work/d" SHA256SUMS SHA256SUMS.msi
check "a consistent set passes" exits 0
check "  and counts its six assets" says "OK: 6 asset(s)"

fresh; run "$work/d" SHA256SUMS
check "an MSI without its manifest fails" exits 1
check "  naming the unlisted asset" says "asset 'orkeon-1.0.0-rc.5-win-x64.msi' is listed by no manifest"

fresh
(cd "$work/d" && mv orkeon_1.0.0-rc.5_amd64.deb 'orkeon_1.0.0~rc.5_amd64.deb' \
  && sed -i 's/orkeon_1.0.0-rc.5_amd64.deb/orkeon_1.0.0~rc.5_amd64.deb/' SHA256SUMS)
run "$work/d" SHA256SUMS SHA256SUMS.msi
check "a '~' in an asset name fails, even when the manifest agrees" exits 1
check "  naming the rewritten character" says "asset name 'orkeon_1.0.0~rc.5_amd64.deb' holds a character GitHub rewrites"

fresh
(cd "$work/d" && mv 'orkeon-1.0.0-rc.5-linux-x64.tar.gz' 'orkeon 1.0.0-rc.5-linux-x64.tar.gz' \
  && sed -i 's/orkeon-1.0.0-rc.5-linux-x64.tar.gz/orkeon 1.0.0-rc.5-linux-x64.tar.gz/' SHA256SUMS)
run --no-hash "$work/d" SHA256SUMS SHA256SUMS.msi
check "a space in an asset name fails" exits 1

# What rc.3 and rc.4 shipped: the Release renamed the .deb, its manifest line kept the '~'.
fresh
(cd "$work/d" && sed -i 's/orkeon_1.0.0-rc.5_amd64.deb/orkeon_1.0.0~rc.5_amd64.deb/' SHA256SUMS \
  && mv orkeon_1.0.0-rc.5_amd64.deb orkeon_1.0.0.rc.5_amd64.deb)
printf '%s\n' orkeon_1.0.0.rc.5_amd64.deb orkeon_1.0.0-rc.5_arm64.deb orkeon-1.0.0-rc.5-linux-x64.tar.gz \
  orkeon-cli-1.0.0-rc.5-win-x64.zip orkeon-1.0.0-rc.5.sbom.cdx.json orkeon-1.0.0-rc.5-win-x64.msi \
  SHA256SUMS SHA256SUMS.msi > "$work/published.txt"
run --assets "$work/published.txt" "$work/d" SHA256SUMS SHA256SUMS.msi
check "a published Release whose .deb was renamed fails" exits 1
check "  naming the asset no line covers" says "asset 'orkeon_1.0.0.rc.5_amd64.deb' is listed by no manifest"
check "  and the line no asset matches" says "'orkeon_1.0.0~rc.5_amd64.deb' is listed but is no asset"
check "  never skipping the missing file in silence" says "SHA256SUMS does not verify"

fresh
printf '%s\n' orkeon_1.0.0-rc.5_amd64.deb orkeon_1.0.0-rc.5_arm64.deb orkeon-1.0.0-rc.5-linux-x64.tar.gz \
  orkeon-cli-1.0.0-rc.5-win-x64.zip orkeon-1.0.0-rc.5.sbom.cdx.json orkeon-1.0.0-rc.5-win-x64.msi \
  notes-extra.bin SHA256SUMS SHA256SUMS.msi > "$work/p2.txt"
run --assets "$work/p2.txt" "$work/d" SHA256SUMS SHA256SUMS.msi
check "a published asset outside every manifest fails, whatever its name" exits 1
check "  naming it" says "asset 'notes-extra.bin' is listed by no manifest"

fresh; (cd "$work/d" && echo tampered > orkeon_1.0.0-rc.5_arm64.deb)
run "$work/d" SHA256SUMS SHA256SUMS.msi
check "an asset whose bytes changed fails" exits 1
check "  naming it" says "orkeon_1.0.0-rc.5_arm64.deb: FAILED"

fresh; (cd "$work/d" && echo "not a checksum line" >> SHA256SUMS)
run "$work/d" SHA256SUMS SHA256SUMS.msi
check "a malformed manifest line fails" exits 1

fresh; (cd "$work/d" && sha256sum orkeon_1.0.0-rc.5_arm64.deb >> SHA256SUMS.msi)
run "$work/d" SHA256SUMS SHA256SUMS.msi
check "an asset listed by two manifests fails" exits 1
check "  saying so" says "listed 2 times"

fresh; run "$work/d" SHA256SUMS SHA256SUMS.msi missing.sums
check "a manifest that does not exist fails" exits 1

if [ "$failed" -ne 0 ]; then echo "$failed check(s) failed."; exit 1; fi
echo "All checks passed."
