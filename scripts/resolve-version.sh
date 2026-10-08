#!/usr/bin/env bash
# The version of a build that is not a release, computed in one place. publish.yml (NuGet
# dev builds), scripts/apt/publish-dev.sh (apt dev builds) and package-installers.sh (the
# archives, when no --version is given) all read it here; scripts/resolve-version.ps1 is
# the same calculation for Windows, and scripts/test-resolve-version.sh holds both to it.
#
# Usage:
#   scripts/resolve-version.sh [--props <file>] [--run <CI run number>] [--format semver|deb]
#
# --props   the Directory.Build.props that carries VersionPrefix and VersionSuffix.
#           Default: src/Directory.Build.props of this checkout.
# --run     the number of the CI run that validated the commit: prints the dev version,
#           <prefix>-<suffix>.dev.<n>. Without it, prints the version of a build made from
#           the checkout that holds --props:
#             HEAD is the commit a v* tag points to   the tag's version, v stripped;
#             any other commit                        <prefix>-<suffix>.local.<stamp>;
#             no git checkout (a source archive)      the props version, as it is written.
#           <stamp> is the committer date of HEAD, YYYYMMDDHHMM in UTC. Changes that are
#           not committed do not move it: two builds of one commit carry one version.
# --format  semver (the default) or deb, the Debian Version of the same build: its `-`
#           becomes `~`, which sorts a prerelease below the final.
#
# A props version without a suffix is a stable one, released or about to be: <prefix>-dev.<n>
# would sort BELOW it, so its dev and local builds move to the next patch (1.0.0 gives
# 1.0.1-dev.<n> and 1.0.1-local.<stamp>).
#
# SemVer order, for one props version: 1.0.0-rc.4 < 1.0.0-rc.4.dev.<n> < 1.0.0-rc.4.local.<stamp>
# < 1.0.0-rc.5. A local build is never taken for an older one than a dev build of its base.
#
# Exit: 0 and the version on stdout; 1 when the props or a tag give no version; 2 on a
# usage error.
set -euo pipefail
export LC_ALL=C

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROPS="$here/../src/Directory.Build.props"
RUN=""
HAS_RUN=0
FORMAT="semver"

die()   { echo "resolve-version: $*" >&2; exit 1; }
usage() { echo "resolve-version: $*" >&2; exit 2; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --props)   [[ $# -ge 2 ]] || usage "--props takes a file"; PROPS="$2"; shift 2 ;;
    --run)     [[ $# -ge 2 ]] || usage "--run takes the CI run number"; RUN="$2"; HAS_RUN=1; shift 2 ;;
    --format)  [[ $# -ge 2 ]] || usage "--format takes semver or deb"; FORMAT="$2"; shift 2 ;;
    -h|--help) sed -n '2,31p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) usage "unknown argument $1" ;;
  esac
done

[[ -f "$PROPS" ]] || usage "--props names no file"
case "$FORMAT" in
  semver|deb) ;;
  *) usage "--format takes semver or deb, got '$FORMAT'" ;;
esac
if [[ "$HAS_RUN" -eq 1 && ! "$RUN" =~ ^[1-9][0-9]*$ ]]; then
  usage "--run takes the CI run number, got '$RUN'"
fi

emit() { # <semver version>
  if [[ "$FORMAT" == deb ]]; then echo "${1/-/\~}"; else echo "$1"; fi
}

prefix="$(sed -n 's/.*<VersionPrefix>\(.*\)<\/VersionPrefix>.*/\1/p' "$PROPS" | head -n1)"
suffix="$(sed -n 's/.*<VersionSuffix>\(.*\)<\/VersionSuffix>.*/\1/p' "$PROPS" | head -n1)"
[[ "$prefix" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)$ ]] || die "$PROPS: VersionPrefix '$prefix' is not Major.Minor.Patch"
next_patch="${BASH_REMATCH[1]}.${BASH_REMATCH[2]}.$((BASH_REMATCH[3] + 1))"
# One alphabet for both forms: a Debian version takes no `-` after the `~`, SemVer no `_`.
[[ -z "$suffix" || "$suffix" =~ ^[0-9A-Za-z.]+$ ]] \
  || die "$PROPS: VersionSuffix '$suffix' holds a character neither a SemVer prerelease nor a Debian version carries"

label=""
if [[ "$HAS_RUN" -eq 1 ]]; then
  label="dev.$RUN"
else
  tree="$(cd "$(dirname "$PROPS")" && pwd)"
  if git -C "$tree" rev-parse --verify --quiet HEAD >/dev/null 2>&1; then
    tag="$(git -C "$tree" describe --tags --match 'v*' --exact-match HEAD 2>/dev/null || true)"
    if [[ -n "$tag" ]]; then
      version="${tag#v}"
      [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$ ]] \
        || die "tag $tag on HEAD names no version: pass the version yourself"
      emit "$version"
      exit 0
    fi
    label="local.$(TZ=UTC0 git -C "$tree" log -1 --format=%cd --date=format-local:%Y%m%d%H%M)"
  else
    echo "resolve-version: $tree is no git checkout: the version is the one $PROPS states" >&2
    emit "$prefix${suffix:+-$suffix}"
    exit 0
  fi
fi

if [[ -n "$suffix" ]]; then
  emit "$prefix-$suffix.$label"
else
  emit "$next_patch-$label"
fi
