#!/usr/bin/env bash
# Updates the signed index of one or more channels of a checked-out `apt` branch, through
# scripts/apt/build-apt-index.sh (one run per channel). It is the updater that
# publish-apt-channel.sh runs for release.yml (apt-publish) and apt-maintenance.yml.
#
# Usage:
#   scripts/apt/update-apt-channels.sh --keyring <binary certificate>
#                                      [--branch-dir <dir>] [--channel <name>]...
#                                      [--debs <debs.list>] [--yank <pkg>=<ver>[/<arch>]]...
#                                      [--ensure <name>]...
#
#   --branch-dir  the root of the apt branch (default: $APT_BRANCH_DIR, set by
#                 publish-apt-channel.sh).
#   --channel     a channel to update: its index takes every package of --debs it accepts
#                 (stable takes final versions only: a Version with "~" is left out of it)
#                 and is signed again with a newer Date, even with nothing new (resign).
#   --debs        a file of "<file.deb>=<tag>/<asset>" lines (fetch-release-debs.sh).
#   --yank        withdraw a stanza. With --yank, each --channel is updated only if a yank
#                 matches one of its stanzas, and a yank that matches no channel fails.
#   --ensure      a channel that must exist: if it has no InRelease yet, an empty signed
#                 index is written, so a source pointing at it updates without error.
#
# Environment: APT_SIGNING_KEY, APT_SIGNING_PASSPHRASE (see build-apt-index.sh).
set -euo pipefail
export LC_ALL=C

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BRANCH_DIR="${APT_BRANCH_DIR:-}"
KEYRING=""
CHANNELS=()
DEBS_FILE=""
YANKS=()
ENSURE=()

die()   { echo "update-apt-channels: $*" >&2; exit 1; }
usage() { echo "update-apt-channels: $*" >&2; exit 2; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --branch-dir) [[ $# -ge 2 ]] || usage "$1 needs a value"; BRANCH_DIR="$2"; shift 2 ;;
    --keyring)    [[ $# -ge 2 ]] || usage "$1 needs a value"; KEYRING="$2"; shift 2 ;;
    --channel)    [[ $# -ge 2 ]] || usage "$1 needs a value"; CHANNELS+=("$2"); shift 2 ;;
    --debs)       [[ $# -ge 2 ]] || usage "$1 needs a value"; DEBS_FILE="$2"; shift 2 ;;
    --yank)       [[ $# -ge 2 ]] || usage "$1 needs a value"; YANKS+=("$2"); shift 2 ;;
    --ensure)     [[ $# -ge 2 ]] || usage "$1 needs a value"; ENSURE+=("$2"); shift 2 ;;
    -h|--help) sed -n '2,25p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) usage "unknown argument: $1" ;;
  esac
done
[[ -n "$BRANCH_DIR" && -d "$BRANCH_DIR" ]] || usage "--branch-dir (or APT_BRANCH_DIR) must name the checked-out apt branch"
[[ -n "$KEYRING" ]] || usage "--keyring is required"
[[ ${#CHANNELS[@]} -ge 1 || ${#ENSURE[@]} -ge 1 ]] || usage "nothing to do: give --channel or --ensure"
[[ -z "$DEBS_FILE" || -f "$DEBS_FILE" ]] || die "no such file: $DEBS_FILE"

# has_stanza <channel dir> <pkg>=<ver>[/<arch>]
has_stanza() {
  local packages="$1/Packages" spec="$2" pkg ver arch
  [[ -f "$packages" ]] || return 1
  pkg="${spec%%=*}"; ver="${spec#*=}"; arch=""
  if [[ "$ver" == */* ]]; then arch="${ver#*/}"; ver="${ver%%/*}"; fi
  awk -v p="$pkg" -v v="$ver" -v a="$arch" '
    /^Package: / { P = $2 } /^Version: / { V = $2 } /^Architecture: / { A = $2 }
    /^$/ { if (P == p && V == v && (a == "" || A == a)) found = 1; P = V = A = "" }
    END { if (P == p && V == v && (a == "" || A == a)) found = 1; exit !found }' "$packages"
}

declare -A done_channels=()
yank_hits=()
for c in ${CHANNELS[@]+"${CHANNELS[@]}"}; do
  [[ -z "${done_channels[$c]:-}" ]] || continue
  args=()
  if [[ -n "$DEBS_FILE" ]]; then
    while IFS= read -r spec; do
      [[ -n "$spec" ]] || continue
      file="${spec%=*}"
      ver="$(dpkg-deb -f "$file" Version)" || die "cannot read $file"
      if [[ "$c" == stable && "$ver" == *"~"* ]]; then
        echo "update-apt-channels: stable leaves out $(basename "$file") ($ver is a prerelease)" >&2
        continue
      fi
      args+=(--deb "$spec")
    done < "$DEBS_FILE"
  fi
  if [[ ${#YANKS[@]} -gt 0 ]]; then
    for y in "${YANKS[@]}"; do
      if has_stanza "$BRANCH_DIR/$c" "$y"; then args+=(--yank "$y"); yank_hits+=("$y"); fi
    done
    if [[ ! " ${args[*]-} " == *" --yank "* ]]; then
      echo "update-apt-channels: $c lists none of the versions to withdraw; left as it is" >&2
      done_channels[$c]=1
      continue
    fi
  fi
  echo "update-apt-channels: updating $c" >&2
  bash "$here/build-apt-index.sh" --channel "$c" --dir "$BRANCH_DIR/$c" --keyring "$KEYRING" ${args[@]+"${args[@]}"}
  done_channels[$c]=1
done

for y in ${YANKS[@]+"${YANKS[@]}"}; do
  [[ " ${yank_hits[*]-} " == *" $y "* ]] || die "--yank $y matches no stanza of ${CHANNELS[*]}"
done

for c in ${ENSURE[@]+"${ENSURE[@]}"}; do
  [[ -z "${done_channels[$c]:-}" ]] || continue
  if [[ ! -f "$BRANCH_DIR/$c/InRelease" ]]; then
    echo "update-apt-channels: $c does not exist yet; writing its empty signed index" >&2
    bash "$here/build-apt-index.sh" --channel "$c" --dir "$BRANCH_DIR/$c" --keyring "$KEYRING"
  fi
  done_channels[$c]=1
done
