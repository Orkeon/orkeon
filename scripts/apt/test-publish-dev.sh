#!/usr/bin/env bash
# Regression test of scripts/apt/publish-dev.sh, the dev channel of the apt repository,
# against a fake `gh`: no network, no token, no real key.
#
# It proves the version numbering (the same as publish.yml's NuGet dev builds, read out of
# publish.yml itself), and a sequence of dev builds through index -> upload -> push -> prune:
# the three newest builds are kept, an older one leaves the signed index in the same run
# that adds the new one, and its assets leave the `apt-dev` prerelease only afterwards,
# once the pushed index no longer lists them (the fake gh refuses to delete an asset the
# pushed index still lists). An asset the index points to is never replaced; a re-run, an
# older build and a wrong index publish or delete nothing.
#
# Needs dpkg-deb, gpg, gpgv and apt-ftparchive (apt-utils), like build-apt-index.sh;
# without apt-ftparchive but with docker it runs itself in a debian:13 container.
# Run: bash scripts/apt/test-publish-dev.sh
set -euo pipefail
export LC_ALL=C

here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
script="$here/publish-dev.sh"
IMAGE="debian:13"

failed=0
check() { # <description> <command...>
  if "${@:2}"; then echo "ok    $1"; else echo "FAIL  $1"; failed=$((failed + 1)); fi
}
exits()   { [ "$code" -eq "$1" ]; }
line()    { grep -qxF -- "$1" <<<"$out"; }
says()    { grep -qF -- "$1" <<<"$out"; }

# dexec <container> <command...>: as in test-build-apt-index.sh, the command runs detached
# and is polled through `docker cp`, because some docker CLI/daemon pairings drop the
# attached stream of an exec that lasts more than a second.
dexec() {
  local c="$1" tag="/tmp/dexec-$RANDOM$RANDOM" waited=0 st
  shift
  docker exec -d "$c" bash -c '"$@" > "$0.out" 2>&1; echo $? > "$0.rc"' "$tag" "$@"
  until st="$(docker cp "$c:$tag.rc" - 2>/dev/null | tar -xOf - 2>/dev/null)" && [ -n "$st" ]; do
    waited=$((waited + 1))
    [ "$waited" -lt 1800 ] || { echo "test-publish-dev: timed out in $c: $*" >&2; return 124; }
    sleep 1
  done
  docker cp "$c:$tag.out" - | tar -xOf -
  return "$st"
}

if ! command -v apt-ftparchive >/dev/null 2>&1 && [ "${1:-}" != --inside ]; then
  command -v docker >/dev/null 2>&1 || { echo "test-publish-dev: apt-ftparchive (apt-utils) or docker is needed" >&2; exit 1; }
  echo "# apt-ftparchive missing here: running in a $IMAGE container"
  name="apt-publish-dev-test-$$"
  trap 'docker rm -f "$name" >/dev/null 2>&1 || true' EXIT
  docker run -d --name "$name" "$IMAGE" sleep infinity >/dev/null
  dexec "$name" bash -c 'apt-get update -qq && DEBIAN_FRONTEND=noninteractive apt-get install -y -qq --no-install-recommends apt-utils gpg gpg-agent gpgv sqv >/dev/null' \
    || { echo "test-publish-dev: could not install apt-utils in $IMAGE" >&2; exit 1; }
  dexec "$name" mkdir -p /repo/scripts /repo/src /repo/.github/workflows
  docker cp "$here" "$name:/repo/scripts/apt"
  docker cp "$repo/src/Directory.Build.props" "$name:/repo/src/Directory.Build.props"
  docker cp "$repo/.github/workflows/publish.yml" "$name:/repo/.github/workflows/publish.yml"
  st=0
  dexec "$name" bash /repo/scripts/apt/test-publish-dev.sh --inside || st=$?
  exit "$st"
fi

work="$(mktemp -d)"
trap 'GNUPGHOME="$work/key" gpgconf --kill all >/dev/null 2>&1 || true; rm -rf "$work"' EXIT
mkdir -p "$work/bin" "$work/debs" "$work/release" "$work/branch/dev"

# --- 1. Version numbering --------------------------------------------------------------
props() { # <prefix> <suffix>
  printf '<Project>\n  <PropertyGroup>\n    <VersionPrefix>%s</VersionPrefix>\n    <VersionSuffix>%s</VersionSuffix>\n  </PropertyGroup>\n</Project>\n' "$1" "$2" > "$work/props"
}
version() { code=0; out="$(bash "$script" version --props "$work/props" --run "$1" 2>&1)" || code=$?; }

# publish.yml's own "Resolve the dev version" step, run as written: the apt and NuGet dev
# builds of one CI run must carry one version.
awk '
  /- name: Resolve the dev version/ { found = 1 }
  found && /run: \|/ { match($0, /^ */); indent = RLENGTH; inrun = 1; next }
  inrun { match($0, /^ */); if ($0 !~ /^ *$/ && RLENGTH <= indent) exit; print substr($0, indent + 3) }
' "$repo/.github/workflows/publish.yml" > "$work/publish-yml-version.sh"
nuget_version() { # <run>: the version publish.yml computes from $work/props
  local d="$work/nuget"
  rm -rf "$d"; mkdir -p "$d/src"
  cp "$work/props" "$d/src/Directory.Build.props"
  (cd "$d" && CI_RUN="$1" GITHUB_OUTPUT="$d/out" bash "$work/publish-yml-version.sh" >/dev/null && sed -n 's/^version=//p' out)
}

echo "# version: a props version with a suffix"
props 1.0.0 rc.4
version 812
check "exits 0" exits 0
check "upstream 1.0.0-rc.4.dev.812" line "version=1.0.0-rc.4.dev.812"
check "Debian 1.0.0~rc.4.dev.812" line "deb_version=1.0.0~rc.4.dev.812"
check "publish.yml's step was found" test -s "$work/publish-yml-version.sh"
check "the same version as publish.yml's NuGet dev build" test "$(nuget_version 812)" = 1.0.0-rc.4.dev.812
check "dpkg: above 1.0.0~rc.4, below 1.0.0~rc.5 and 1.0.0" \
  bash -c 'dpkg --compare-versions 1.0.0~rc.4.dev.812 gt 1.0.0~rc.4 && dpkg --compare-versions 1.0.0~rc.4.dev.812 lt 1.0.0~rc.5 && dpkg --compare-versions 1.0.0~rc.4.dev.812 lt 1.0.0'
check "dpkg: dev.812 above dev.99 (numeric, not lexical)" dpkg --compare-versions 1.0.0~rc.4.dev.812 gt 1.0.0~rc.4.dev.99

echo "# version: a props version without a suffix moves to the next patch"
props 1.0.0 ""
version 900
check "upstream 1.0.1-dev.900" line "version=1.0.1-dev.900"
check "Debian 1.0.1~dev.900" line "deb_version=1.0.1~dev.900"
check "the same version as publish.yml" test "$(nuget_version 900)" = 1.0.1-dev.900
check "dpkg: above 1.0.0, below 1.0.1" bash -c 'dpkg --compare-versions 1.0.1~dev.900 gt 1.0.0 && dpkg --compare-versions 1.0.1~dev.900 lt 1.0.1'

echo "# version: refusals"
props 1.0 rc.4
version 5
check "a prefix that is not Major.Minor.Patch fails" exits 1
props 1.0.0 rc.4
version 0
check "run number 0 is a usage error" exits 2
version abc
check "a run number that is no number is a usage error" exits 2
props 1.0.0 rc_4
version 5
check "a suffix a Debian version cannot carry fails" exits 1

echo "# version: the repository's own props"
code=0; out="$(bash "$script" version --props "$repo/src/Directory.Build.props" --run 7 2>&1)" || code=$?
check "exits 0" exits 0
check "the asset version has no ~ and the Debian one has no -" \
  bash -c 'v=$(sed -n "s/^version=//p" <<<"$0"); d=$(sed -n "s/^deb_version=//p" <<<"$0"); [[ "$v" != *"~"* && "$d" != *-* && "${v//-/\~}" = "$d" ]]' "$out"

# --- 2. Fixtures: throwaway key, tiny packages, fake gh ----------------------------------
export GNUPGHOME="$work/key"
mkdir -m 700 "$GNUPGHOME"
gpg --batch --quiet --pinentry-mode loopback --passphrase '' --quick-gen-key "Orkeon Archive Test Key <test@orkeon.invalid>" ed25519 cert 2d 2>/dev/null
fpr="$(gpg --batch --with-colons --list-keys 2>/dev/null | awk -F: '$1 == "fpr" { print $10; exit }')"
gpg --batch --quiet --pinentry-mode loopback --passphrase '' --quick-add-key "$fpr" ed25519 sign 2d 2>/dev/null
gpg --batch --quiet --export "$fpr" > "$work/archive.gpg" 2>/dev/null
APT_SIGNING_KEY="$(gpg --batch --quiet --pinentry-mode loopback --passphrase '' --armor --export-secret-subkeys "$fpr" 2>/dev/null)"
export APT_SIGNING_KEY
unset GNUPGHOME

make_deb() { # <package> <version> <arch> [salt]: $work/debs/<package>_<upstream>_<arch>.deb
  local root file
  root="$(mktemp -d)"
  file="$work/debs/$1_${2//\~/-}_$3.deb"
  mkdir -p "$root/DEBIAN" "$root/usr/bin"
  printf '#!/bin/sh\necho "%s %s%s"\n' "$1" "$2" "${4:-}" > "$root/usr/bin/$1-test"
  chmod 0755 "$root/usr/bin/$1-test"
  printf 'Package: %s\nVersion: %s\nArchitecture: %s\nMaintainer: Orkeon Contributors <arion@orkeon.org>\nDescription: test package\n test package of test-publish-dev.sh\n' \
    "$1" "$2" "$3" > "$root/DEBIAN/control"
  find "$root" -exec touch -h -d @1700000000 {} +
  SOURCE_DATE_EPOCH=1700000000 dpkg-deb --root-owner-group -Zgzip --build "$root" "$file" >/dev/null
  rm -rf "$root"
  echo "$file"
}
for v in 9 10 11 12 13; do
  make_deb orkeon "1.0.0~rc.4.dev.$v" amd64 >/dev/null
  make_deb orkeon "1.0.0~rc.4.dev.$v" arm64 >/dev/null
done
make_deb orkeon 1.0.0~rc.5.dev.20 amd64 >/dev/null
make_deb orkeon 1.0.0~rc.5.dev.20 arm64 >/dev/null
make_deb orkeon-archive-keyring 2026.10.01 all >/dev/null

# The fake gh: the Release is $work/release (absent until created), one file per asset.
# upload never clobbers; delete-asset refuses an asset the pushed index ($FAKE_BRANCH/dev/
# Packages) still lists, which is the ordering the channel promises.
cat > "$work/bin/gh" <<'FAKE'
#!/usr/bin/env bash
set -euo pipefail
rel="$FAKE_RELEASE"
log() { echo "$*" >> "$FAKE_LOG"; }
case "$1 $2" in
  "release view")
    [ -f "$rel.created" ] || { echo "release not found" >&2; exit 1; }
    if [ "${6:-}" = "--jq" ]; then ls -1 "$rel" 2>/dev/null || true; fi ;;
  "release create")
    [ -f "$rel.created" ] && { echo "already exists" >&2; exit 1; }
    mkdir -p "$rel"; touch "$rel.created"; log "CREATE $*" ;;
  "release upload")
    name="$(basename "$4")"
    [ -f "$rel.created" ] || { echo "release not found" >&2; exit 1; }
    [ ! -e "$rel/$name" ] || { echo "asset under the same name already exists" >&2; exit 1; }
    [ "${5:-}" != "--clobber" ] || { log "CLOBBER $name"; exit 1; }
    cp "$4" "$rel/$name"; log "UPLOAD $name" ;;
  "release delete-asset")
    [ -e "$rel/$4" ] || { echo "asset not found" >&2; exit 1; }
    if grep -qxF "Filename: releases/download/$3/$4" "$FAKE_BRANCH/dev/Packages" 2>/dev/null; then
      log "DELETE-WHILE-INDEXED $4"; exit 1
    fi
    rm "$rel/$4"; log "DELETE $4" ;;
  *) echo "fake gh: unexpected call: $*" >&2; exit 1 ;;
esac
FAKE
chmod +x "$work/bin/gh"
export PATH="$work/bin:$PATH" FAKE_RELEASE="$work/release/apt-dev" FAKE_LOG="$work/gh.log" FAKE_BRANCH="$work/branch"
touch "$FAKE_LOG"

pd() { code=0; out="$(bash "$script" "$@" 2>&1)" || code=$?; }
deb() { echo "$work/debs/orkeon_1.0.0-$1_$2.deb"; }
keyring_deb="$work/debs/orkeon-archive-keyring_2026.10.01_all.deb"
asset_on_release() { [ -e "$FAKE_RELEASE/$1" ]; }
indexed() { grep -qxF "Filename: releases/download/apt-dev/$1" "$work/branch/dev/Packages"; }
versions() { awk '/^Package: orkeon$/{p=1} /^Package: orkeon-/{p=0} p && /^Version:/{print $2}' "$work/branch/dev/Packages" | sort -u | tr '\n' ' '; }
logged() { grep -qxF -- "$1" "$FAKE_LOG"; }
no_log() { ! grep -qE -- "$1" "$FAKE_LOG"; }

# One dev build as apt-dev.yml runs it: index from the published dev/, upload, push, prune.
T0=$(( $(date -u +%s) - 3600 ))
n=0
build() { # <suffix e.g. rc.4.dev.10>
  n=$((n + 1))
  rm -rf "$work/out"
  : > "$FAKE_LOG"
  pd index --current "$work/branch/dev" --out "$work/out" --keyring "$work/archive.gpg" \
     --date-epoch "$((T0 + n))" "$(deb "$1" amd64)" "$(deb "$1" arm64)" "$keyring_deb"
  index_out="$out"; index_code="$code"
  status="$(sed -n 's/^status=//p' <<<"$out")"
  [ "$index_code" -eq 0 ] || return 0
  [ "$status" = published ] || return 0
  pd upload --published "$work/branch/dev/Packages" "$(deb "$1" amd64)" "$(deb "$1" arm64)" "$keyring_deb"
  upload_out="$out"
  [ "$code" -eq 0 ] || return 0
  rm -rf "$work/branch/dev"; cp -a "$work/out" "$work/branch/dev"   # the push
  pd prune --index "$work/branch/dev/Packages" --apply
  prune_out="$out"; prune_code="$code"
  [ "$prune_code" -eq 0 ] || echo "prune failed: $prune_out"
}

echo "# ensure-release: created once, on the given commit, prerelease, never latest"
sha=0123456789abcdef0123456789abcdef01234567
pd ensure-release --target "$sha"
check "exits 0" exits 0
check "created as a prerelease, not latest, on the commit" grep -qF "CREATE release create apt-dev --prerelease --latest=false --target $sha " "$FAKE_LOG"
: > "$FAKE_LOG"
pd ensure-release --target "$sha"
check "a second call creates nothing" no_log CREATE
check "... and says the tag stays" says "its tag is left where it is"
pd ensure-release --target main
check "a target that is no full sha is a usage error" exits 2

echo "# first build on an empty channel: rc.4.dev.10"
build rc.4.dev.10
check "index exits 0" test "$index_code" -eq 0
check "status=published" test "$status" = published
check "both architectures and the keyring indexed" bash -c 'grep -c "^Filename: releases/download/apt-dev/" "$0" | grep -qx 3' "$work/branch/dev/Packages"
check "the three assets uploaded" test "$(grep -c '^UPLOAD ' "$FAKE_LOG")" -eq 3
check "nothing deleted" no_log '^DELETE'
check "Suite raw/apt/dev" grep -qx 'Suite: raw/apt/dev' "$work/branch/dev/Release"

echo "# rc.4.dev.11 and rc.4.dev.12: three builds, nothing leaves"
build rc.4.dev.11
build rc.4.dev.12
check "status=published" test "$status" = published
check "versions 10, 11, 12" test "$(versions)" = "1.0.0~rc.4.dev.10 1.0.0~rc.4.dev.11 1.0.0~rc.4.dev.12 "
check "the keyring asset was kept, never uploaded again" grep -qF "kept       orkeon-archive-keyring_2026.10.01_all.deb" <<<"$upload_out"
check "no upload of the keyring after the first build" no_log '^UPLOAD orkeon-archive-keyring'
check "nothing deleted" no_log '^DELETE'

echo "# rc.4.dev.13: the fourth build pushes the oldest out"
build rc.4.dev.13
check "status=published" test "$status" = published
check "yanked=1.0.0~rc.4.dev.10" grep -qxF "yanked=1.0.0~rc.4.dev.10" <<<"$index_out"
check "the index keeps 11, 12, 13" test "$(versions)" = "1.0.0~rc.4.dev.11 1.0.0~rc.4.dev.12 1.0.0~rc.4.dev.13 "
check "the keyring stays in the index" indexed orkeon-archive-keyring_2026.10.01_all.deb
check "dev.10 assets deleted (amd64)" logged "DELETE orkeon_1.0.0-rc.4.dev.10_amd64.deb"
check "dev.10 assets deleted (arm64)" logged "DELETE orkeon_1.0.0-rc.4.dev.10_arm64.deb"
check "only those two deleted" test "$(grep -c '^DELETE ' "$FAKE_LOG")" -eq 2
check "never an asset the pushed index lists" no_log 'DELETE-WHILE-INDEXED|CLOBBER'
check "the uploads came before the deletions" \
  bash -c 'u=$(grep -n "^UPLOAD " "$0" | tail -1 | cut -d: -f1); d=$(grep -n "^DELETE " "$0" | head -1 | cut -d: -f1); [ "$u" -lt "$d" ]' "$FAKE_LOG"
check "the keyring asset is still on the Release" asset_on_release orkeon-archive-keyring_2026.10.01_all.deb
check "the Release holds exactly the indexed assets" \
  test "$(ls -1 "$FAKE_RELEASE" | tr '\n' ' ')" = "$(sed -n 's|^Filename: releases/download/apt-dev/||p' "$work/branch/dev/Packages" | sort | tr '\n' ' ')"

echo "# a re-run of rc.4.dev.13 publishes nothing"
build rc.4.dev.13
check "index exits 0" test "$index_code" -eq 0
check "status=unchanged" test "$status" = unchanged
check "no upload, no deletion" no_log '^(UPLOAD|DELETE)'

echo "# an older build (rc.4.dev.9, a late re-run) publishes nothing"
build rc.4.dev.9
check "index exits 0" test "$index_code" -eq 0
check "status=older" test "$status" = older
check "no upload, no deletion" no_log '^(UPLOAD|DELETE)'
check "the index still holds 11, 12, 13" test "$(versions)" = "1.0.0~rc.4.dev.11 1.0.0~rc.4.dev.12 1.0.0~rc.4.dev.13 "

echo "# a leftover asset no index lists is replaced; an indexed one never is"
: > "$FAKE_LOG"
echo "partial upload of a failed run" > "$FAKE_RELEASE/orkeon_1.0.0-rc.5.dev.20_amd64.deb"
pd upload --published "$work/branch/dev/Packages" "$(deb rc.5.dev.20 amd64)" "$(deb rc.4.dev.13 amd64)"
check "exits 0" exits 0
check "the leftover is replaced" says "replaced   orkeon_1.0.0-rc.5.dev.20_amd64.deb"
check "... by the built bytes" cmp -s "$FAKE_RELEASE/orkeon_1.0.0-rc.5.dev.20_amd64.deb" "$(deb rc.5.dev.20 amd64)"
check "the indexed dev.13 asset is kept untouched" says "kept       orkeon_1.0.0-rc.4.dev.13_amd64.deb"
check "... never deleted" no_log 'DELETE orkeon_1.0.0-rc.4.dev.13'
rm -f "$FAKE_RELEASE/orkeon_1.0.0-rc.5.dev.20_amd64.deb"

echo "# prune: a leftover the index does not list goes; a dry run deletes nothing"
: > "$FAKE_LOG"
cp "$(deb rc.4.dev.9 amd64)" "$FAKE_RELEASE/"
pd prune --index "$work/branch/dev/Packages"
check "dry run lists the leftover" line "would delete orkeon_1.0.0-rc.4.dev.9_amd64.deb"
check "... and only it" test "$(grep -c '^would delete' <<<"$out")" -eq 1
check "... deletes nothing" no_log '^DELETE'
pd prune --index "$work/branch/dev/Packages" --apply
check "--apply deletes it" logged "DELETE orkeon_1.0.0-rc.4.dev.9_amd64.deb"

echo "# prune refuses an index that lists no orkeon asset"
: > "$FAKE_LOG"
printf 'Package: orkeon-archive-keyring\nVersion: 2026.10.01\nArchitecture: all\nFilename: releases/download/apt-dev/orkeon-archive-keyring_2026.10.01_all.deb\n\n' > "$work/wrong-Packages"
pd prune --index "$work/wrong-Packages" --apply
check "exits 1" exits 1
check "deletes nothing" no_log '^DELETE'
pd prune --index "$work/missing-Packages" --apply
check "a missing index exits 1" exits 1

echo "# the next prerelease's dev build: rc.5.dev.20 above every rc.4 build"
build rc.5.dev.20
check "status=published" test "$status" = published
check "yanked=1.0.0~rc.4.dev.11" grep -qxF "yanked=1.0.0~rc.4.dev.11" <<<"$index_out"
check "the index keeps 12, 13 and rc.5.dev.20" test "$(versions)" = "1.0.0~rc.4.dev.12 1.0.0~rc.4.dev.13 1.0.0~rc.5.dev.20 "
check "dev.11 assets deleted after the push" bash -c 'grep -qx "DELETE orkeon_1.0.0-rc.4.dev.11_amd64.deb" "$0" && grep -qx "DELETE orkeon_1.0.0-rc.4.dev.11_arm64.deb" "$0"' "$FAKE_LOG"
check "never an asset the pushed index lists" no_log 'DELETE-WHILE-INDEXED|CLOBBER'

echo "# index refuses a non-empty --out and two orkeon versions"
mkdir -p "$work/full" && touch "$work/full/x"
pd index --current "$work/branch/dev" --out "$work/full" --keyring "$work/archive.gpg" "$(deb rc.4.dev.13 amd64)"
check "non-empty --out exits 1" exits 1
rm -rf "$work/out2"
pd index --current "$work/branch/dev" --out "$work/out2" --keyring "$work/archive.gpg" "$(deb rc.4.dev.13 amd64)" "$(deb rc.4.dev.12 arm64)"
check "two versions exit 1" exits 1

if [ "$failed" -gt 0 ]; then echo "test-publish-dev: ${failed} check(s) failed"; exit 1; fi
echo "test-publish-dev passed"
