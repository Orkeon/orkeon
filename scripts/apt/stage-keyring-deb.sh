#!/usr/bin/env bash
# Puts orkeon-archive-keyring_<version>_all.deb into a release's asset directory, built once
# per keyring version: when an earlier published v* Release already carries that file, its
# published bytes are attached again rather than a rebuild, so one version always has one
# SHA-256 (the apt index refuses a second one, and a runner's newer dpkg-deb could produce
# other bytes).
#
# Usage:
#   scripts/apt/stage-keyring-deb.sh --out <dir> [--repo <owner/name>] [--published <file.deb>]
#
# The package is built from installers/apt (scripts/package-keyring-deb.sh) in every case,
# as the reference. When a published copy exists (searched with gh among the published
# Releases whose tag starts with v, the oldest one wins; or given with --published, for
# tests), the keyring file inside both must be the same: the copy is then used as is. A
# different keyring under the same version fails: installers/apt/keyring.version must change
# with the certificate. Without a published copy, the new build is used.
# The file lands in <dir> and its line in <dir>/SHA256SUMS is refreshed.
#
# Needs dpkg-deb, gpg, sha256sum, and gh (GH_TOKEN) unless --published is given.
set -euo pipefail
export LC_ALL=C

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT=""
REPO="${GITHUB_REPOSITORY:-Orkeon/orkeon}"
PUBLISHED=""

die()   { echo "stage-keyring-deb: $*" >&2; exit 1; }
usage() { echo "stage-keyring-deb: $*" >&2; exit 2; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --out)       [[ $# -ge 2 ]] || usage "$1 needs a value"; OUT="$2"; shift 2 ;;
    --repo)      [[ $# -ge 2 ]] || usage "$1 needs a value"; REPO="$2"; shift 2 ;;
    --published) [[ $# -ge 2 ]] || usage "$1 needs a value"; PUBLISHED="$2"; shift 2 ;;
    -h|--help) sed -n '2,19p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) usage "unknown argument: $1" ;;
  esac
done
[[ -n "$OUT" ]] || usage "--out is required"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"

VERSION="$(tr -d '[:space:]' < "$REPO_ROOT/installers/apt/keyring.version")"
ASSET="orkeon-archive-keyring_${VERSION}_all.deb"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

bash "$REPO_ROOT/scripts/package-keyring-deb.sh" --out "$WORK/build" >&2
[[ -f "$WORK/build/$ASSET" ]] || die "package-keyring-deb.sh did not produce $ASSET"

origin=""
if [[ -n "$PUBLISHED" ]]; then
  [[ -f "$PUBLISHED" ]] || die "no such file: $PUBLISHED"
  origin="given with --published"
  cp "$PUBLISHED" "$WORK/published.deb"
else
  command -v gh >/dev/null 2>&1 || die "gh not found (or give --published)"
  origin="$(gh api "repos/$REPO/releases" --paginate \
    --jq ".[] | select(.draft == false) | select(.tag_name | startswith(\"v\")) | select(any(.assets[]; .name == \"$ASSET\")) | .tag_name" \
    | tail -n1)" || die "cannot list the Releases of $REPO"
  if [[ -n "$origin" ]]; then
    gh release download "$origin" --repo "$REPO" --pattern "$ASSET" --dir "$WORK/dl" \
      || die "cannot download $ASSET from $origin"
    mv "$WORK/dl/$ASSET" "$WORK/published.deb"
  fi
fi

keyring_of() { dpkg-deb --fsys-tarfile "$1" | tar -xOf - ./usr/share/keyrings/orkeon-archive-keyring.gpg; }

if [[ -n "$origin" ]]; then
  [[ "$(dpkg-deb -f "$WORK/published.deb" Package)" == orkeon-archive-keyring && \
     "$(dpkg-deb -f "$WORK/published.deb" Version)" == "$VERSION" ]] \
    || die "the published $ASSET ($origin) is not orkeon-archive-keyring $VERSION"
  if ! cmp -s <(keyring_of "$WORK/published.deb") <(keyring_of "$WORK/build/$ASSET"); then
    die "refused: $ASSET is already published ($origin) with another keyring than installers/apt/orkeon-archive-keyring.asc. A changed certificate needs a new installers/apt/keyring.version."
  fi
  if cmp -s "$WORK/published.deb" "$WORK/build/$ASSET"; then
    echo "stage-keyring-deb: $ASSET already published ($origin); the rebuild is byte-identical, the published file is attached again" >&2
  else
    echo "stage-keyring-deb: $ASSET already published ($origin); same keyring, other package bytes from this runner's tools: the published file is attached again, never the rebuild" >&2
  fi
  cp "$WORK/published.deb" "$OUT/$ASSET"
else
  echo "stage-keyring-deb: $ASSET is new (no published Release carries it): attaching the new build" >&2
  cp "$WORK/build/$ASSET" "$OUT/$ASSET"
fi

(
  cd "$OUT"
  touch SHA256SUMS
  grep -v "  $ASSET\$" SHA256SUMS > SHA256SUMS.tmp || true
  sha256sum "$ASSET" >> SHA256SUMS.tmp
  sort -k2 SHA256SUMS.tmp > SHA256SUMS
  rm -f SHA256SUMS.tmp
)
echo "$OUT/$ASSET"
