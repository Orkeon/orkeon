#!/usr/bin/env bash
# Checks that a set of release assets and its checksum manifests agree, name for
# name, before (or after) they reach a GitHub Release.
#
# Usage:
#   scripts/check-release-assets.sh [--assets FILE] [--no-hash] DIR MANIFEST...
#
#   DIR           the directory holding the assets and the manifests.
#   MANIFEST...   the checksum files (sha256sum format) found in DIR, e.g.
#                 SHA256SUMS SHA256SUMS.msi. Together they must cover every asset.
#   --assets FILE the asset names, one per line (e.g. the names a published
#                 Release lists). Without it, the assets are the files of DIR that
#                 release.yml uploads: the globs below mirror its `files:` input.
#   --no-hash     check the names only, not the bytes.
#
# Fails (exit 1, every problem listed) when:
#   - an asset or manifest name holds a character outside [A-Za-z0-9._-]: GitHub
#     rewrites such characters on upload (`~` becomes `.`), so the published name
#     would no longer be the one the manifest and any apt index point at;
#   - an asset is listed by no manifest, or by more than one;
#   - a manifest line is malformed, or names a file that is not an asset;
#   - a manifest does not verify (`sha256sum --check`, never --ignore-missing: a
#     missing file is an error, not a skipped line).
set -euo pipefail

ASSETS_FILE=""
HASH=true
while [[ $# -gt 0 ]]; do
  case "$1" in
    --assets)  ASSETS_FILE="$2"; shift 2 ;;
    --no-hash) HASH=false; shift ;;
    -h|--help) sed -n '2,25p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    --) shift; break ;;
    -*) echo "Unknown option: $1" >&2; exit 2 ;;
    *) break ;;
  esac
done
[[ $# -ge 2 ]] || { echo "usage: check-release-assets.sh [--assets FILE] [--no-hash] DIR MANIFEST..." >&2; exit 2; }
DIR="$1"; shift
MANIFESTS=("$@")
[[ -d "$DIR" ]] || { echo "Not a directory: $DIR" >&2; exit 2; }

# The asset globs of release.yml's publication step, manifests excluded. Keep the
# two lists in step: an asset uploaded but not matched here would escape the check.
PUBLISH_GLOBS=('*.tar.gz' '*.zip' '*.deb' 'orkeon*.msi' '*.sbom.cdx.json')

problems=()
problem() { problems+=("$1"); }
valid_name() { [[ "$1" =~ ^[A-Za-z0-9._-]+$ ]]; }
is_manifest() { local m; for m in "${MANIFESTS[@]}"; do [[ "$1" == "$m" ]] && return 0; done; return 1; }

declare -A asset=()
if [[ -n "$ASSETS_FILE" ]]; then
  [[ -f "$ASSETS_FILE" ]] || { echo "No such asset list: $ASSETS_FILE" >&2; exit 2; }
  while IFS= read -r name || [[ -n "$name" ]]; do
    [[ -n "$name" ]] || continue
    is_manifest "$name" && continue
    asset["$name"]=0
  done < "$ASSETS_FILE"
else
  shopt -s nullglob
  for glob in "${PUBLISH_GLOBS[@]}"; do
    for path in "$DIR"/$glob; do
      [[ -f "$path" ]] || continue
      name="${path##*/}"
      is_manifest "$name" && continue
      asset["$name"]=0
    done
  done
  shopt -u nullglob
fi
[[ ${#asset[@]} -gt 0 ]] || problem "no asset to check in $DIR"

for name in "${!asset[@]}"; do
  valid_name "$name" || problem "asset name '$name' holds a character GitHub rewrites on upload (allowed: A-Z a-z 0-9 . _ -)"
done

for manifest in "${MANIFESTS[@]}"; do
  valid_name "$manifest" || problem "manifest name '$manifest' holds a character GitHub rewrites on upload"
  if [[ ! -f "$DIR/$manifest" ]]; then
    problem "manifest $manifest is missing from $DIR"
    continue
  fi
  lineno=0
  while IFS= read -r line || [[ -n "$line" ]]; do
    lineno=$((lineno + 1))
    if [[ ! "$line" =~ ^[0-9a-f]{64}\ [\ *](.+)$ ]]; then
      problem "$manifest:$lineno: not a sha256sum line: $line"
      continue
    fi
    name="${BASH_REMATCH[1]}"
    valid_name "$name" || problem "$manifest:$lineno: '$name' holds a character GitHub rewrites on upload, no published asset can carry that name"
    if [[ -z "${asset[$name]+set}" ]]; then
      problem "$manifest:$lineno: '$name' is listed but is no asset"
    else
      asset["$name"]=$((asset["$name"] + 1))
    fi
  done < "$DIR/$manifest"
done

for name in "${!asset[@]}"; do
  case "${asset[$name]}" in
    0) problem "asset '$name' is listed by no manifest (${MANIFESTS[*]})" ;;
    1) ;;
    *) problem "asset '$name' is listed ${asset[$name]} times across ${MANIFESTS[*]}" ;;
  esac
done

if [[ "$HASH" == true ]]; then
  for manifest in "${MANIFESTS[@]}"; do
    [[ -f "$DIR/$manifest" ]] || continue
    if ! out="$(cd "$DIR" && sha256sum --check --strict --quiet "$manifest" 2>&1)"; then
      problem "$manifest does not verify: $(tr '\n' ' ' <<<"$out")"
    fi
  done
fi

if [[ ${#problems[@]} -gt 0 ]]; then
  printf '%s\n' "${problems[@]}" | sort | sed 's/^/::error::/' >&2
  echo "Release assets and manifests disagree: ${#problems[@]} problem(s)." >&2
  exit 1
fi
echo "OK: ${#asset[@]} asset(s), each listed once by ${MANIFESTS[*]}$([[ "$HASH" == true ]] && echo ', every checksum verified')."
