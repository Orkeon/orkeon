#!/usr/bin/env bash
# The dev channel of the Orkeon apt repository: the `orkeon` package built from a green
# `main`, its assets on the single fixed-tag prerelease `apt-dev`, its signed index in the
# dev/ directory of the `apt` branch. .github/workflows/apt-dev.yml runs it; nothing here
# touches NuGet.
#
# Usage:
#   scripts/apt/publish-dev.sh version --props src/Directory.Build.props --run <CI run number>
#   scripts/apt/publish-dev.sh ensure-release [--tag apt-dev] --target <commit sha>
#   scripts/apt/publish-dev.sh index --current <dir> --out <dir> --keyring <binary cert>
#                                    [--tag apt-dev] [--keep 3] [--date-epoch <s>] <file.deb>...
#   scripts/apt/publish-dev.sh upload [--tag apt-dev] --published <Packages> <file.deb>...
#   scripts/apt/publish-dev.sh prune [--tag apt-dev] --index <Packages> [--apply]
#
# version prints the dev version of CI run <n>, numbered as publish.yml numbers the NuGet
# dev builds: `version=<prefix>-<suffix>.dev.<n>` (the upstream version and asset names) and
# `deb_version=<prefix>~<suffix>.dev.<n>` (the Debian Version: `~` sorts it below the next
# prerelease and the final). A props version without a suffix moves the dev builds to the
# next patch: 1.0.0 gives 1.0.1-dev.<n>, Debian 1.0.1~dev.<n>.
#
# ensure-release creates the prerelease when it is missing, on <commit sha>, never marked
# latest. It never moves an existing tag.
#
# index copies the published dev/ directory (--current, possibly absent: first build) to
# --out and runs build-apt-index.sh --channel dev there with the given packages, each served
# from releases/download/<tag>/<its file name>. The channel keeps the --keep newest `orkeon`
# versions: the older ones are yanked in the same signed index. It prints `status=`:
#   published  --out holds the new signed index (and `yanked=` lists the versions removed);
#   unchanged  this orkeon version is already in the index: nothing to publish;
#   older      this version would not be among the --keep newest: nothing to publish.
# APT_SIGNING_KEY / APT_SIGNING_PASSPHRASE are read by build-apt-index.sh.
#
# upload attaches each file to the prerelease under its own name, never replacing an asset
# the published index (--published, the Packages of --current) points to: such an asset is
# kept as it is. An asset of the same name that no index lists is the leftover of a run that
# failed before its push; nobody can have downloaded it through apt, so it is replaced.
#
# prune runs after the push, against the index as pushed: it deletes every `orkeon_*.deb`
# asset of the prerelease that the index no longer lists (the builds that left the three
# newest, and leftovers). A stanza leaves the index before its asset leaves the Release,
# never the reverse. Dry run unless --apply. The keyring package is never deleted.
#
# Needs gh (authenticated by GH_TOKEN) for ensure-release, upload and prune; dpkg-deb and
# dpkg for index (plus what build-apt-index.sh needs).
set -euo pipefail
export LC_ALL=C

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TAG="apt-dev"
KEEP=3

die()   { echo "publish-dev: $*" >&2; exit 1; }
usage() { echo "publish-dev: $*" >&2; exit 2; }

# Every Filename of a Packages file whose asset sits on $TAG, one asset name per line.
indexed_assets() { # <Packages>
  [[ -f "$1" ]] || return 0
  sed -n "s|^Filename: releases/download/$TAG/||p" "$1"
}
orkeon_versions() { # <Packages>: distinct versions of the orkeon package
  [[ -f "$1" ]] || return 0
  awk '/^Package:/{p=$2} /^Version:/{v=$2} /^$/{ if (p == "orkeon") print v; p = "" } END { if (p == "orkeon") print v }' "$1" | sort -u
}
release_assets() {
  gh release view "$TAG" --json assets --jq '.assets[].name'
}

cmd_version() {
  local props="" run=""
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --props) props="$2"; shift 2 ;;
      --run)   run="$2"; shift 2 ;;
      *) usage "version: unknown argument $1" ;;
    esac
  done
  [[ -f "$props" ]] || usage "version: --props names no file"
  [[ "$run" =~ ^[1-9][0-9]*$ ]] || usage "version: --run takes the CI run number, got '$run'"
  local prefix suffix
  prefix="$(sed -n 's/.*<VersionPrefix>\(.*\)<\/VersionPrefix>.*/\1/p' "$props" | head -n1)"
  suffix="$(sed -n 's/.*<VersionSuffix>\(.*\)<\/VersionSuffix>.*/\1/p' "$props" | head -n1)"
  [[ "$prefix" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)$ ]] || die "$props: VersionPrefix '$prefix' is not Major.Minor.Patch"
  # Same rule as publish.yml's publish-dev job, so a NuGet dev build and an apt dev build of
  # one CI run carry one number.
  if [[ -n "$suffix" ]]; then
    suffix="$suffix.dev.$run"
  else
    prefix="${BASH_REMATCH[1]}.${BASH_REMATCH[2]}.$((BASH_REMATCH[3] + 1))"
    suffix="dev.$run"
  fi
  [[ "$suffix" =~ ^[0-9A-Za-z.]+$ ]] || die "$props: VersionSuffix gives '$suffix', which a Debian version cannot carry"
  echo "version=$prefix-$suffix"
  echo "deb_version=$prefix~$suffix"
}

cmd_ensure_release() {
  local target=""
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --tag)    TAG="$2"; shift 2 ;;
      --target) target="$2"; shift 2 ;;
      *) usage "ensure-release: unknown argument $1" ;;
    esac
  done
  [[ "$target" =~ ^[0-9a-f]{40}$ ]] || usage "ensure-release: --target takes a full commit sha"
  if gh release view "$TAG" --json tagName >/dev/null 2>&1; then
    echo "release $TAG exists; its tag is left where it is"
    return 0
  fi
  gh release create "$TAG" --prerelease --latest=false --target "$target" \
    --title "Development builds (apt dev channel)" \
    --notes "Debian packages of the orkeon CLI built from main, served by the dev channel of the apt repository (Suites: raw/apt/dev/). Only the three latest builds are kept. Unstable and unsupported: not a release. This tag never moves." \
    >/dev/null
  echo "created release $TAG (prerelease, not latest) on $target"
}

cmd_index() {
  local current="" out="" keyring="" date_epoch="" debs=()
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --current)    current="$2"; shift 2 ;;
      --out)        out="$2"; shift 2 ;;
      --keyring)    keyring="$2"; shift 2 ;;
      --tag)        TAG="$2"; shift 2 ;;
      --keep)       KEEP="$2"; shift 2 ;;
      --date-epoch) date_epoch="$2"; shift 2 ;;
      -*) usage "index: unknown argument $1" ;;
      *) debs+=("$1"); shift ;;
    esac
  done
  [[ -n "$current" && -n "$out" && -n "$keyring" ]] || usage "index: --current, --out and --keyring are required"
  [[ "$KEEP" =~ ^[1-9][0-9]*$ ]] || usage "index: --keep takes a positive number"
  [[ ${#debs[@]} -gt 0 ]] || usage "index: no package given"
  [[ ! -e "$out" ]] || [[ -z "$(ls -A "$out")" ]] || die "index: --out $out is not empty"

  local f pkg ver new_version="" args=()
  for f in "${debs[@]}"; do
    [[ -f "$f" ]] || die "no such package: $f"
    pkg="$(dpkg-deb -f "$f" Package)"
    ver="$(dpkg-deb -f "$f" Version)"
    if [[ "$pkg" == orkeon ]]; then
      [[ -z "$new_version" || "$new_version" == "$ver" ]] || die "index: the orkeon packages carry two versions ($new_version, $ver)"
      new_version="$ver"
    fi
    args+=(--deb "$f=$TAG/$(basename "$f")")
  done
  [[ -n "$new_version" ]] || die "index: no orkeon package among the files"

  mkdir -p "$out"
  if [[ -d "$current" ]]; then
    cp -a "$current/." "$out/"
  fi

  local -a published
  mapfile -t published < <(orkeon_versions "$out/Packages")
  local v
  for v in ${published[@]+"${published[@]}"}; do
    if [[ "$v" == "$new_version" ]]; then
      echo "orkeon $new_version is already in the dev index: nothing to publish" >&2
      echo "status=unchanged"
      return 0
    fi
  done

  # Newest first, in Debian order.
  local sorted=() x i inserted
  for v in "$new_version" ${published[@]+"${published[@]}"}; do
    inserted=false
    for i in "${!sorted[@]}"; do
      x="${sorted[$i]}"
      if dpkg --compare-versions "$v" gt "$x"; then
        sorted=("${sorted[@]:0:$i}" "$v" "${sorted[@]:$i}")
        inserted=true
        break
      fi
    done
    $inserted || sorted+=("$v")
  done
  local kept=("${sorted[@]:0:$KEEP}") yanked=("${sorted[@]:$KEEP}")
  for v in ${yanked[@]+"${yanked[@]}"}; do
    if [[ "$v" == "$new_version" ]]; then
      echo "orkeon $new_version is older than the $KEEP builds the dev channel keeps (${kept[*]}): nothing to publish" >&2
      echo "status=older"
      return 0
    fi
    args+=(--yank "orkeon=$v")
  done

  bash "$here/build-apt-index.sh" --channel dev --dir "$out" --keyring "$keyring" \
    ${date_epoch:+--date-epoch "$date_epoch"} "${args[@]}" >&2
  echo "status=published"
  echo "yanked=${yanked[*]}"
}

cmd_upload() {
  local published_index="" files=()
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --tag)       TAG="$2"; shift 2 ;;
      --published) published_index="$2"; shift 2 ;;
      -*) usage "upload: unknown argument $1" ;;
      *) files+=("$1"); shift ;;
    esac
  done
  [[ -n "$published_index" ]] || usage "upload: --published is required (the Packages the index was built from; it may not exist yet)"
  [[ ${#files[@]} -gt 0 ]] || usage "upload: no file given"
  local existing indexed f name
  existing=" $(release_assets | tr '\n' ' ') "
  indexed=" $(indexed_assets "$published_index" | tr '\n' ' ') "
  for f in "${files[@]}"; do
    [[ -f "$f" ]] || die "no such file: $f"
    name="$(basename "$f")"
    if [[ "$existing" == *" $name "* ]]; then
      if [[ "$indexed" == *" $name "* ]]; then
        echo "kept       $name (the published index points to it; never replaced)"
        continue
      fi
      echo "replaced   $name (left by a run that never pushed its index)"
      gh release delete-asset "$TAG" "$name" --yes >/dev/null
    else
      echo "uploaded   $name"
    fi
    gh release upload "$TAG" "$f" >/dev/null
  done
}

cmd_prune() {
  local index="" apply=false
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --tag)   TAG="$2"; shift 2 ;;
      --index) index="$2"; shift 2 ;;
      --apply) apply=true; shift ;;
      *) usage "prune: unknown argument $1" ;;
    esac
  done
  [[ -f "$index" ]] || die "prune: --index names no file; without the pushed index nothing may be deleted"
  local indexed name
  indexed=" $(indexed_assets "$index" | tr '\n' ' ') "
  # An index that lists no orkeon asset of this Release is the wrong file, not an empty
  # channel: deleting every build on its word would empty the channel.
  indexed_assets "$index" | grep -q '^orkeon_.*\.deb$' || die "prune: $index lists no orkeon asset of $TAG; refusing to delete on its word"
  while IFS= read -r name; do
    [[ "$name" == orkeon_*.deb ]] || continue
    [[ "$indexed" == *" $name "* ]] && continue
    if $apply; then
      gh release delete-asset "$TAG" "$name" --yes >/dev/null
      echo "deleted    $name"
    else
      echo "would delete $name"
    fi
  done < <(release_assets)
}

[[ $# -gt 0 ]] || usage "a command is required: version, ensure-release, index, upload or prune"
command="$1"
shift
case "$command" in
  version)        cmd_version "$@" ;;
  ensure-release) cmd_ensure_release "$@" ;;
  index)          cmd_index "$@" ;;
  upload)         cmd_upload "$@" ;;
  prune)          cmd_prune "$@" ;;
  -h|--help)      sed -n '2,45p' "$0" | sed 's/^# \{0,1\}//' ;;
  *) usage "unknown command: $command" ;;
esac
