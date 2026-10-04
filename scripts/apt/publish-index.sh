#!/usr/bin/env bash
# One publication of the stable/rc apt index, as release.yml (apt-publish) and
# apt-maintenance.yml run it: read the `apt` branch, update and sign the channels, prove
# them with a real apt client, then push them as one commit.
#
# Usage:
#   scripts/apt/publish-index.sh --message <what> --keyring <binary certificate>
#                                [--channel <name>]... [--ensure <name>]...
#                                [--debs <debs.list>] [--yank <pkg>=<ver>[/<arch>]]...
#                                [--check-version <upstream version>] [--check-image <image>]
#                                [--check-server-container <name>] [--repo-dir <checkout>]
#
#   --channel, --ensure, --debs, --yank   passed to update-apt-channels.sh.
#   --keyring          the certificate the index must verify with; it also becomes the
#                      branch's root orkeon-archive-keyring.gpg.
#   --check-version    before pushing, every updated channel that lists orkeon at this
#                      version goes through check-apt-branch.sh (with --debs as the assets);
#                      --check-image and --check-server-container are passed to it.
#   --repo-dir         the checkout that pushes (default: this repository).
#
# Steps: publish-apt-channel.sh --read (the branch and its commit), update-apt-channels.sh
# on that copy, the checks, then publish-apt-channel.sh with every channel directory that
# exists and --base set to the commit read: if a channel changed on the remote meanwhile,
# nothing is pushed. Environment: APT_SIGNING_KEY, APT_SIGNING_PASSPHRASE.
set -euo pipefail
export LC_ALL=C

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MESSAGE=""
KEYRING=""
REPO_DIR="$(cd "$here/../.." && pwd)"
CHECK_VERSION=""
CHECK_ARGS=()
CHANNELS=()
UPDATE_ARGS=()
DEBS_FILE=""

usage() { echo "publish-index: $*" >&2; exit 2; }

while [[ $# -gt 0 ]]; do
  [[ $# -ge 2 ]] || usage "$1 needs a value"
  case "$1" in
    --message)       MESSAGE="$2" ;;
    --keyring)       KEYRING="$2" ;;
    --repo-dir)      REPO_DIR="$2" ;;
    --channel|--ensure) CHANNELS+=("$2"); UPDATE_ARGS+=("$1" "$2") ;;
    --yank)          UPDATE_ARGS+=("$1" "$2") ;;
    --debs)          DEBS_FILE="$2"; UPDATE_ARGS+=("$1" "$2") ;;
    --check-version) CHECK_VERSION="$2" ;;
    --check-image)   CHECK_ARGS+=(--image "$2") ;;
    --check-server-container) CHECK_ARGS+=(--server-container "$2") ;;
    *) usage "unknown argument: $1" ;;
  esac
  shift 2
done
[[ -n "$MESSAGE" ]] || usage "--message is required"
[[ -f "$KEYRING" ]] || usage "--keyring must name the binary certificate"
[[ ${#CHANNELS[@]} -ge 1 ]] || usage "give --channel or --ensure"
[[ -z "$CHECK_VERSION" || -n "$DEBS_FILE" ]] || usage "--check-version needs --debs"
KEYRING="$(cd "$(dirname "$KEYRING")" && pwd)/$(basename "$KEYRING")"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
branch="$WORK/branch"
base="$(bash "$here/publish-apt-channel.sh" --repo-dir "$REPO_DIR" --read "$branch")"
echo "publish-index: apt branch read at ${base:-<none yet>}" >&2

bash "$here/update-apt-channels.sh" --branch-dir "$branch" --keyring "$KEYRING" "${UPDATE_ARGS[@]}"

if [[ -n "$CHECK_VERSION" ]]; then
  cp "$KEYRING" "$branch/orkeon-archive-keyring.gpg"
  deb_version="${CHECK_VERSION//-/\~}"
  for c in $(printf '%s\n' "${CHANNELS[@]}" | awk '!seen[$0]++'); do
    if grep -qx "Version: $deb_version" "$branch/$c/Packages" 2>/dev/null; then
      bash "$here/check-apt-branch.sh" --branch-dir "$branch" --debs "$DEBS_FILE" --channel "$c" \
        --version "$CHECK_VERSION" ${CHECK_ARGS[@]+"${CHECK_ARGS[@]}"}
    fi
  done
fi

pairs=()
for c in $(printf '%s\n' "${CHANNELS[@]}" | awk '!seen[$0]++'); do
  [[ -d "$branch/$c" ]] && pairs+=(--channel "$c" --dir "$branch/$c")
done
bash "$here/publish-apt-channel.sh" --repo-dir "$REPO_DIR" "${pairs[@]}" --message "$MESSAGE" \
  --root-keyring "$KEYRING" ${base:+--base "$base"}
