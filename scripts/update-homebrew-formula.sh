#!/usr/bin/env bash
# Rewrites the five release-dependent values in installers/homebrew/orkeon.rb --
# `version`, and the `url` + `sha256` of both the on_arm and on_intel blocks --
# from a release's SHA256SUMS. Everything else in the formula is hand-authored
# and left untouched.
#
# Usage:
#   scripts/update-homebrew-formula.sh [--sums FILE] [--release TAG]
#                                      [--version X.Y.Z[-suffix]]
#                                      [--formula PATH] [--dry-run]
#
#   --sums FILE     Read the checksums from a local SHA256SUMS.
#                   Default: artifacts/installers/SHA256SUMS.
#   --release TAG   Download SHA256SUMS from that GitHub Release instead
#                   (TAG is the git tag, e.g. v0.9.2-beta).
#   --version V     Version to write. Default: derived from the osx filenames
#                   found in SHA256SUMS, which is what the release actually
#                   published.
#   --formula PATH  Formula to rewrite. Default: installers/homebrew/orkeon.rb.
#   --dry-run       Print the diff and change nothing.
#
# Idempotent: running it twice against the same SHA256SUMS is a no-op the
# second time.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPO_URL="https://github.com/Orkeon/orkeon"

SUMS=""
RELEASE_TAG=""
VERSION=""
FORMULA="$REPO_ROOT/installers/homebrew/orkeon.rb"
DRY_RUN=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --sums)    SUMS="$2"; shift 2 ;;
    --release) RELEASE_TAG="$2"; shift 2 ;;
    --version) VERSION="$2"; shift 2 ;;
    --formula) FORMULA="$2"; shift 2 ;;
    --dry-run) DRY_RUN=true; shift ;;
    -h|--help) sed -n '2,29p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

[[ -f "$FORMULA" ]] || { echo "Formula not found: $FORMULA" >&2; exit 1; }

# --- 1. Locate the checksums --------------------------------------------------
# The trap must end on a success status: under `set -e` a failing command inside
# an EXIT trap becomes the script's exit code, turning a clean run into a false
# failure.
TMPDIR_SUMS=""
TMP_FORMULA=""
cleanup() {
  [[ -n "$TMPDIR_SUMS" ]] && rm -rf "$TMPDIR_SUMS"
  [[ -n "$TMP_FORMULA" ]] && rm -f "$TMP_FORMULA"
  return 0
}
trap cleanup EXIT

if [[ -n "$RELEASE_TAG" ]]; then
  [[ -z "$SUMS" ]] || { echo "Pass either --sums or --release, not both." >&2; exit 2; }
  TMPDIR_SUMS="$(mktemp -d)"
  SUMS="$TMPDIR_SUMS/SHA256SUMS"
  echo "==> Downloading $REPO_URL/releases/download/$RELEASE_TAG/SHA256SUMS"
  curl -fsSL "$REPO_URL/releases/download/$RELEASE_TAG/SHA256SUMS" -o "$SUMS" || {
    echo "Could not fetch SHA256SUMS for release $RELEASE_TAG (a 404 means the tag has no" >&2
    echo "published Release, or the Release has no SHA256SUMS asset yet)." >&2
    exit 1
  }
fi

SUMS="${SUMS:-$REPO_ROOT/artifacts/installers/SHA256SUMS}"
[[ -f "$SUMS" ]] || {
  echo "Checksum file not found: $SUMS" >&2
  echo "Build the artefacts first (scripts/package-installers.sh --app-set cli --rids osx-arm64 osx-x64)," >&2
  echo "or point at a published release with --release <tag>." >&2
  exit 1
}

# --- 2. Version ---------------------------------------------------------------
# The filenames in SHA256SUMS are the source of truth: they carry the version
# the release actually shipped, so nothing can drift between the two.
if [[ -z "$VERSION" ]]; then
  VERSION="$(awk '{ sub(/^\*/, "", $2)
                    if ($2 ~ /^orkeon-cli-.+-osx-arm64\.tar\.gz$/) {
                      n = $2
                      sub(/^orkeon-cli-/, "", n)
                      sub(/-osx-arm64\.tar\.gz$/, "", n)
                      print n
                      exit
                    } }' "$SUMS")"
fi
[[ -n "$VERSION" ]] || {
  echo "No orkeon-cli-*-osx-arm64.tar.gz entry in $SUMS and no --version given." >&2
  exit 1
}

# --- 3. Checksums -------------------------------------------------------------
sha_for() { # $1 = artefact filename
  local sha
  sha="$(awk -v f="$1" '{ sub(/^\*/, "", $2); if ($2 == f) { print $1; exit } }' "$SUMS")"
  [[ -n "$sha" ]] || { echo "No entry for $1 in $SUMS" >&2; return 1; }
  [[ "$sha" =~ ^[0-9a-f]{64}$ ]] || { echo "Malformed sha256 for $1: $sha" >&2; return 1; }
  printf '%s\n' "$sha"
}

ARM_FILE="orkeon-cli-$VERSION-osx-arm64.tar.gz"
X64_FILE="orkeon-cli-$VERSION-osx-x64.tar.gz"
ARM_SHA="$(sha_for "$ARM_FILE")"
X64_SHA="$(sha_for "$X64_FILE")"
ARM_URL="$REPO_URL/releases/download/v$VERSION/$ARM_FILE"
X64_URL="$REPO_URL/releases/download/v$VERSION/$X64_FILE"

# --- 4. Rewrite ---------------------------------------------------------------
# Block-aware: `url` and `sha256` appear twice, once per architecture, and only
# inside on_arm / on_intel. Anything outside those blocks is copied verbatim.
TMP_FORMULA="$(mktemp)"

awk -v ver="$VERSION" \
    -v arm_url="$ARM_URL" -v arm_sha="$ARM_SHA" \
    -v x64_url="$X64_URL" -v x64_sha="$X64_SHA" '
  function replace(field, value,   pattern) {
    # sub() treats & in the replacement as "the matched text"; the values here
    # are URLs and hex digests, but escape it anyway rather than rely on that.
    gsub(/&/, "\\\\&", value)
    pattern = field " \"[^\"]*\""
    sub(pattern, field " \"" value "\"")
  }
  /^[[:space:]]*on_arm do[[:space:]]*$/   { block = "arm" }
  /^[[:space:]]*on_intel do[[:space:]]*$/ { block = "intel" }
  {
    if ($0 ~ /^[[:space:]]*version "/) {
      replace("version", ver)
    } else if (block == "arm" && $0 ~ /^[[:space:]]*url "/) {
      replace("url", arm_url)
    } else if (block == "arm" && $0 ~ /^[[:space:]]*sha256 "/) {
      replace("sha256", arm_sha)
    } else if (block == "intel" && $0 ~ /^[[:space:]]*url "/) {
      replace("url", x64_url)
    } else if (block == "intel" && $0 ~ /^[[:space:]]*sha256 "/) {
      replace("sha256", x64_sha)
    }
    print
  }
  /^[[:space:]]*end[[:space:]]*$/ { block = "" }
' "$FORMULA" > "$TMP_FORMULA"

# Every one of the five values must have landed, or the formula drifted away
# from the shape this script knows how to rewrite.
for expected in "version \"$VERSION\"" \
                "url \"$ARM_URL\"" "sha256 \"$ARM_SHA\"" \
                "url \"$X64_URL\"" "sha256 \"$X64_SHA\""; do
  grep -qF "$expected" "$TMP_FORMULA" || {
    echo "Rewrite failed: expected [$expected] in the result." >&2
    echo "The formula's on_arm / on_intel structure has changed; update this script." >&2
    exit 1
  }
done

if cmp -s "$FORMULA" "$TMP_FORMULA"; then
  echo "==> $FORMULA is already up to date for $VERSION (no change)."
  exit 0
fi

echo "==> Changes for $VERSION:"
diff -u "$FORMULA" "$TMP_FORMULA" || true

if [[ "$DRY_RUN" == true ]]; then
  echo "==> --dry-run: nothing written."
  exit 0
fi

cat "$TMP_FORMULA" > "$FORMULA"
echo "==> Updated $FORMULA"
echo "    arm64 $ARM_SHA"
echo "    x64   $X64_SHA"
echo "    Next: commit, then push the formula to the Orkeon/homebrew-tap repository."
