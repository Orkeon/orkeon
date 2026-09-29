#!/usr/bin/env bash
# Retention of the dev channel on the GitHub Packages NuGet feed: every version a
# v* tag released stays, the newest dev build stays, older dev builds go.
#
# publish.yml's `publish-dev` job pushes `<props version>.dev.<n>` (n = the CI run
# number) for every green push to main; without this the feed would grow by the
# whole package set on every merge. A version is deleted only when it is a dev
# build (it ends in `.dev.<n>` or `-dev.<n>`, the only shape the channel
# produces), no v* tag names it, and its <n> is LOWER than the kept one. A higher
# <n> belongs to a newer run that overlapped this one: deleting it would roll the
# channel back. A package's last version is never deleted (the API refuses: that
# would delete the package). A version that is neither tagged nor a dev build is
# reported, not deleted, unless --include-untagged is given.
#
# Dry run unless --apply. publish.yml runs it after a successful push, never
# before: pruning first and then failing the push would leave no dev build at all.
#
#   --keep <version>     the dev version just published; older dev builds go.
#                        Without it, each package keeps its own newest dev build.
#   --artifacts <dir>    prune only the packages packed in <dir>, named
#                        <id>.<keep>.nupkg (needs --keep). CI passes it, so the
#                        job only touches the packages it has just pushed. Without
#                        it every NuGet package of the organization is listed:
#                        use a token that can list and delete them, e.g. a classic
#                        PAT with read:packages and delete:packages.
#   --include-untagged   also delete versions that are neither tagged nor dev
#                        builds: a one-off cleanup, review the dry run first.
#   --apply              delete for real.
#   --repo <owner/name>  default $GITHUB_REPOSITORY, else Orkeon/orkeon.
#
# A deleted version can be restored from the package's settings for 30 days.
set -euo pipefail

repo="${GITHUB_REPOSITORY:-Orkeon/orkeon}"
keep=""
artifacts=""
include_untagged=false
apply=false

usage() { awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"; }

while [ $# -gt 0 ]; do
  case "$1" in
    --keep)             keep="${2:?--keep needs a version}"; shift 2 ;;
    --artifacts)        artifacts="${2:?--artifacts needs a directory}"; shift 2 ;;
    --include-untagged) include_untagged=true; shift ;;
    --apply)            apply=true; shift ;;
    --repo)             repo="${2:?--repo needs owner/name}"; shift 2 ;;
    -h|--help)          usage; exit 0 ;;
    *)                  echo "unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done
org="${repo%%/*}"

dev_re='[.-]dev\.([0-9]+)$'
threshold=""
if [ -n "$keep" ]; then
  if [[ ! "$keep" =~ $dev_re ]]; then
    echo "::error::--keep $keep is not a dev build (<version>.dev.<n>)." >&2
    exit 2
  fi
  threshold="${BASH_REMATCH[1]}"
fi

command -v gh >/dev/null || { echo "::error::gh (the GitHub CLI) is required." >&2; exit 2; }
err="$(mktemp)"
trap 'rm -f "$err"' EXIT

# The tags from the API, not `git tag`: a shallow CI checkout has none, and the
# remote is the authority on what was released.
tagged="$(gh api "repos/${repo}/tags" --paginate --jq '.[].name | select(startswith("v")) | ltrimstr("v")')"
if [ -z "$tagged" ]; then
  echo "::error::No v* tag found on ${repo} — refusing to prune against an empty release list." >&2
  exit 1
fi
# NuGet versions compare case-insensitively.
is_tagged() { grep -qixF -- "$1" <<<"$tagged"; }

packages=()
if [ -n "$artifacts" ]; then
  if [ -z "$keep" ]; then
    echo "::error::--artifacts needs --keep: the packages are read from <id>.<keep>.nupkg." >&2
    exit 2
  fi
  for f in "$artifacts"/*.nupkg; do
    [ -e "$f" ] || { echo "::error::No .nupkg in ${artifacts}." >&2; exit 1; }
    name="${f##*/}"
    id="${name%".$keep.nupkg"}"
    [ "$id" != "$name" ] || { echo "::error::${name} is not a ${keep} package." >&2; exit 1; }
    packages+=("$id")
  done
else
  list="$(gh api "orgs/${org}/packages?package_type=nuget&per_page=100" --paginate --jq '.[].name')"
  [ -z "$list" ] || mapfile -t packages <<<"$list"
fi

mode="dry run"
$apply && mode="apply"
echo "Pruning ${#packages[@]} NuGet package(s) of ${org} (${mode}): $(wc -l <<<"$tagged") tagged version(s) protected, $(
  [ -n "$keep" ] && echo "dev builds older than ${keep} go" || echo "each package's newest dev build kept")."

deleted=0
failed=0
strays=()
for pkg in "${packages[@]}"; do
  if ! versions="$(gh api "orgs/${org}/packages/nuget/${pkg}/versions?per_page=100" --paginate \
      --jq '.[] | "\(.id) \(.name)"' 2>"$err")"; then
    echo "::error::${pkg}: cannot list its versions: $(cat "$err")"
    failed=$((failed + 1))
    continue
  fi

  limit="$threshold"
  if [ -z "$limit" ]; then
    limit=0
    while read -r _ v; do
      if [[ "$v" =~ $dev_re ]] && [ "${BASH_REMATCH[1]}" -gt "$limit" ]; then
        limit="${BASH_REMATCH[1]}"
      fi
    done <<<"$versions"
  fi

  remaining="$(grep -c . <<<"$versions" || true)"
  while read -r id v; do
    [ -n "$id" ] || continue
    is_tagged "$v" && continue
    if [[ "$v" =~ $dev_re ]]; then
      # The kept build, or a newer one pushed by an overlapping run.
      [ "${BASH_REMATCH[1]}" -lt "$limit" ] || continue
    elif ! $include_untagged; then
      strays+=("${pkg} ${v}")
      continue
    fi
    if [ "$remaining" -le 1 ]; then
      echo "::warning::${pkg} ${v} is the package's last version — kept (has the package left the lineup?)."
      continue
    fi
    remaining=$((remaining - 1))
    if ! $apply; then
      echo "would delete  ${pkg} ${v}"
      deleted=$((deleted + 1))
    # </dev/null: the loop reads the version list from stdin, which gh must not eat.
    elif gh api -X DELETE "orgs/${org}/packages/nuget/${pkg}/versions/${id}" </dev/null >/dev/null 2>"$err"; then
      echo "deleted       ${pkg} ${v}"
      deleted=$((deleted + 1))
    elif grep -q "HTTP 404" "$err"; then
      echo "already gone  ${pkg} ${v} (an overlapping run deleted it)"
    else
      echo "::error::${pkg} ${v}: delete failed: $(cat "$err")"
      failed=$((failed + 1))
    fi
  done <<<"$versions"
done

if [ "${#strays[@]}" -gt 0 ]; then
  echo "${#strays[@]} version(s) neither tagged nor dev builds, left in place (--include-untagged deletes them):"
  printf '  %s\n' "${strays[@]}"
fi
echo "${deleted} version(s) $($apply && echo deleted || echo 'to delete'), ${failed} failure(s)."
if [ "$failed" -gt 0 ]; then
  echo "::error::Pruning failed ${failed} time(s). An HTTP 403 means the token may not delete: give this repository the Admin role under the package's 'Manage Actions access' settings, or run the script with a classic PAT carrying delete:packages."
  exit 1
fi
