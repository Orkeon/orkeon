#!/usr/bin/env bash
# Downloads the Debian packages a PUBLISHED GitHub Release carries, and checks each one
# before it may enter an apt index.
#
# Usage:
#   scripts/apt/fetch-release-debs.sh --tag <tag> [--tag <tag>]... --out <dir>
#                                     [--repo <owner/name>] [--attest]
#
# For every tag: the Release must be published (a draft's assets answer 404 to users, so a
# Filename pointing at one is never written). Its assets named orkeon_<v>_<amd64|arm64>.deb
# and orkeon-archive-keyring_<v>_all.deb are downloaded to <dir>/<tag>/, and each one must:
#   - exist under that exact name with the size the Release lists (the Filename apt will
#     request is releases/download/<tag>/<asset name>);
#   - have the SHA-256 its line in the Release's SHA256SUMS gives. A line naming the asset
#     with "~" where the published name has "." is accepted (GitHub renamed such assets on
#     upload before release.yml stopped producing them): same digest, other name.
#     SHA256SUMS is a check only, never the source of a digest;
#   - with --attest, pass `gh attestation verify` (build provenance of this repository).
# Any difference stops everything. The accepted packages are appended to <dir>/debs.list,
# one "<absolute file>=<tag>/<asset>" line each: the --deb argument of build-apt-index.sh.
#
# Needs gh (GH_TOKEN in a workflow), sha256sum, stat.
set -euo pipefail
export LC_ALL=C

TAGS=()
OUT=""
REPO="${GITHUB_REPOSITORY:-Orkeon/orkeon}"
ATTEST=false

die()   { echo "fetch-release-debs: $*" >&2; exit 1; }
usage() { echo "fetch-release-debs: $*" >&2; exit 2; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --tag)    [[ $# -ge 2 ]] || usage "$1 needs a value"; TAGS+=("$2"); shift 2 ;;
    --out)    [[ $# -ge 2 ]] || usage "$1 needs a value"; OUT="$2"; shift 2 ;;
    --repo)   [[ $# -ge 2 ]] || usage "$1 needs a value"; REPO="$2"; shift 2 ;;
    --attest) ATTEST=true; shift ;;
    -h|--help) sed -n '2,22p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) usage "unknown argument: $1" ;;
  esac
done
[[ ${#TAGS[@]} -ge 1 ]] || usage "at least one --tag is required"
[[ -n "$OUT" ]] || usage "--out is required"
command -v gh >/dev/null 2>&1 || die "gh not found"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"

for tag in "${TAGS[@]}"; do
  [[ "$tag" =~ ^[A-Za-z0-9][A-Za-z0-9._+-]*$ ]] || die "invalid tag '$tag'"
  dir="$OUT/$tag"
  mkdir -p "$dir"
  meta="$dir/.release.json"
  gh release view "$tag" --repo "$REPO" --json isDraft,assets \
    --jq '"draft \(.isDraft)", (.assets[] | "asset \(.name) \(.size)")' > "$meta" \
    || die "$tag: no Release found in $REPO"
  grep -qx 'draft false' "$meta" \
    || die "$tag: the Release is a draft; its assets are not public, nothing may be indexed before it is published"

  mapfile -t debs < <(awk '$1 == "asset" && ($2 ~ /^orkeon_[^\/]+_(amd64|arm64)\.deb$/ || $2 ~ /^orkeon-archive-keyring_[^\/]+_all\.deb$/) { print $2 }' "$meta")
  [[ ${#debs[@]} -ge 1 ]] || die "$tag: the Release carries no orkeon .deb"
  grep -q '^asset SHA256SUMS ' "$meta" || die "$tag: the Release has no SHA256SUMS to check the packages against"

  patterns=(--pattern SHA256SUMS)
  for asset in "${debs[@]}"; do patterns+=(--pattern "$asset"); done
  gh release download "$tag" --repo "$REPO" --dir "$dir" --clobber "${patterns[@]}" \
    || die "$tag: download failed"

  for asset in "${debs[@]}"; do
    file="$dir/$asset"
    [[ -f "$file" ]] || die "$tag: $asset was not downloaded"
    listed_size="$(awk -v n="$asset" '$1 == "asset" && $2 == n { print $3 }' "$meta")"
    size="$(stat -c %s "$file")"
    [[ "$size" == "$listed_size" ]] || die "$tag: $asset is $size bytes, the Release lists $listed_size"
    sha="$(sha256sum "$file" | cut -d' ' -f1)"
    # The SHA256SUMS line: the exact name first, else the name with "~" for ".".
    line="$(awk -v n="$asset" '{ f = $2; sub(/^\*/, "", f) } f == n { print; exit }' "$dir/SHA256SUMS")"
    named="$asset"
    if [[ -z "$line" ]]; then
      line="$(awk -v n="$asset" '{ f = $2; sub(/^\*/, "", f); g = f; gsub(/~/, ".", g) } f != n && g == n { print; exit }' "$dir/SHA256SUMS")"
      named="$(awk '{ f = $2; sub(/^\*/, "", f); print f }' <<<"$line")"
    fi
    [[ -n "$line" ]] || die "$tag: SHA256SUMS has no line for $asset"
    expected="$(cut -d' ' -f1 <<<"$line")"
    [[ "$sha" == "$expected" ]] || die "$tag: $asset has SHA-256 $sha, SHA256SUMS gives $expected for $named"
    if $ATTEST; then
      gh attestation verify "$file" --repo "$REPO" >/dev/null 2>"$dir/.attest.log" \
        || { cat "$dir/.attest.log" >&2; die "$tag: $asset has no valid build provenance attestation from $REPO"; }
    fi
    note=""
    [[ "$named" == "$asset" ]] || note=" (SHA256SUMS names it $named)"
    echo "fetch-release-debs: $tag/$asset $size bytes sha256 $sha$note" >&2
    echo "$file=$tag/$asset" >> "$OUT/debs.list"
  done
done
