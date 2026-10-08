#!/usr/bin/env bash
# Regression test of scripts/resolve-version.sh and of its PowerShell twin
# scripts/resolve-version.ps1, in throwaway git repositories: no network, no token.
#
# It proves the two versions a build off a tag can carry — the dev version of a CI run
# (<props>.dev.<n>, the numbering of publish.yml and of the apt dev channel) and the local
# one (<props>.local.<commit date>) —, that a commit a v* tag points to keeps the tag's
# version, and that a shallow clone answers like a full one.
#
# The twin is compared on every case when a PowerShell is on the PATH: `pwsh` (7+) and,
# under Windows, `powershell` (Windows PowerShell 5.1). Without one the comparison is
# skipped, and said so.
# Run: bash scripts/test-resolve-version.sh
set -euo pipefail
export LC_ALL=C

here="$(cd "$(dirname "$0")" && pwd)"
script="$here/resolve-version.sh"
twin="$here/resolve-version.ps1"

failed=0
check() { # <description> <command...>
  if "${@:2}"; then echo "ok    $1"; else echo "FAIL  $1"; failed=$((failed + 1)); fi
}
exits() { [ "$code" -eq "$1" ]; }
is()    { [ "$out" = "$1" ]; }

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# A path as a Windows program reads it (Git Bash hands /tmp/... to PowerShell otherwise).
native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }

shells=()
for candidate in pwsh powershell; do
  if command -v "$candidate" >/dev/null 2>&1; then shells+=("$candidate"); fi
done
if [ "${#shells[@]}" -eq 0 ]; then
  echo "# no PowerShell on the PATH: resolve-version.ps1 is not compared here"
fi

# resolve <props> [--run <n>] [--format <f>]: runs the bash script, then every PowerShell
# found with the same arguments; $out and $code are the bash answer, $twins counts the
# PowerShell answers that differ from it.
twins=0
resolve() {
  local props="$1"; shift
  code=0; out="$(bash "$script" --props "$props" "$@" 2>/dev/null)" || code=$?
  local shell args=() tout tcode
  while [ $# -gt 0 ]; do
    case "$1" in
      --run)    args+=(-Run "$2"); shift 2 ;;
      --format) args+=(-Format "$2"); shift 2 ;;
      *) echo "test-resolve-version: resolve does not map $1" >&2; exit 2 ;;
    esac
  done
  for shell in ${shells[@]+"${shells[@]}"}; do
    tcode=0
    tout="$("$shell" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$(native "$twin")" \
      -Props "$(native "$props")" ${args[@]+"${args[@]}"} 2>/dev/null | tr -d '\r')" || tcode=$?
    if [ "$tout" != "$out" ] || [ "$tcode" -ne "$code" ]; then
      echo "      $shell answers '$tout' (exit $tcode), bash '$out' (exit $code)"
      twins=$((twins + 1))
    fi
  done
}

write_props() { # <file> <prefix> <suffix>
  mkdir -p "$(dirname "$1")"
  printf '<Project>\n  <PropertyGroup>\n    <VersionPrefix>%s</VersionPrefix>\n    <VersionSuffix>%s</VersionSuffix>\n  </PropertyGroup>\n</Project>\n' "$2" "$3" > "$1"
}

# A repository whose commits carry a known committer date, whatever the clock says.
git_in() { git -C "$1" -c user.name=test -c user.email=test@orkeon.invalid -c commit.gpgsign=false -c tag.gpgsign=false "${@:2}"; }
commit() { # <repo> <message> <committer date, ISO 8601>
  GIT_AUTHOR_DATE="$3" GIT_COMMITTER_DATE="$3" git_in "$1" commit --quiet --allow-empty -m "$2"
}

repo="$work/repo"
props="$repo/src/Directory.Build.props"
mkdir -p "$repo"
git init --quiet --initial-branch=main "$repo"
write_props "$props" 1.0.0 rc.4
git_in "$repo" add src/Directory.Build.props
commit "$repo" "the release commit" "2026-10-01T08:00:00Z"
git_in "$repo" tag v1.0.0-rc.4

# --- 1. The dev version of a CI run -------------------------------------------------------
echo "# --run: the dev version of a CI run"
resolve "$props" --run 412
check "exits 0" exits 0
check "a props version with a suffix: 1.0.0-rc.4.dev.412" is "1.0.0-rc.4.dev.412"
resolve "$props" --run 412 --format deb
check "its Debian form: 1.0.0~rc.4.dev.412" is "1.0.0~rc.4.dev.412"

write_props "$work/stable.props" 1.0.0 ""
resolve "$work/stable.props" --run 412
check "a props version without a suffix moves to the next patch: 1.0.1-dev.412" is "1.0.1-dev.412"
resolve "$work/stable.props" --run 412 --format deb
check "its Debian form: 1.0.1~dev.412" is "1.0.1~dev.412"

# --- 2. A build made from a checkout ------------------------------------------------------
echo "# no --run: HEAD on a v* tag"
resolve "$props"
check "exits 0" exits 0
check "the tag's version: 1.0.0-rc.4" is "1.0.0-rc.4"
resolve "$props" --format deb
check "its Debian form: 1.0.0~rc.4" is "1.0.0~rc.4"

echo "# no --run: HEAD one commit after the tag"
# 23:30 at UTC-05:00 is 04:30 the next day in UTC: the stamp is the UTC one.
commit "$repo" "one commit later" "2026-10-07T23:30:00-05:00"
resolve "$props"
check "exits 0" exits 0
check "the local form, dated in UTC: 1.0.0-rc.4.local.202610080430" is "1.0.0-rc.4.local.202610080430"
check "which is not the release's version" test "$out" != "1.0.0-rc.4"
resolve "$props" --format deb
check "its Debian form: 1.0.0~rc.4.local.202610080430" is "1.0.0~rc.4.local.202610080430"
resolve "$props" --run 413
check "--run still gives the dev version there" is "1.0.0-rc.4.dev.413"

echo "# no --run: a tag that is no version tag does not count"
git_in "$repo" tag apt-dev
resolve "$props"
check "apt-dev on HEAD leaves the local form" is "1.0.0-rc.4.local.202610080430"

echo "# no --run: a shallow clone answers like the full one"
full="$out"
git clone --quiet --depth 1 "file://$repo" "$work/shallow"
check "the clone is shallow" test "$(git -C "$work/shallow" rev-list --count HEAD)" = 1
resolve "$work/shallow/src/Directory.Build.props"
check "the same version" is "$full"

echo "# no --run: a props version without a suffix"
write_props "$props" 1.0.0 ""
git_in "$repo" add src/Directory.Build.props
commit "$repo" "the stable version" "2026-11-02T10:15:00Z"
resolve "$props"
check "the next patch, as for a dev build: 1.0.1-local.202611021015" is "1.0.1-local.202611021015"

echo "# no --run: a tree that is no git checkout"
write_props "$work/tarball/src/Directory.Build.props" 1.0.0 rc.4
# GIT_CEILING_DIRECTORIES keeps git from finding a repository above the temporary directory.
export GIT_CEILING_DIRECTORIES="$work"
resolve "$work/tarball/src/Directory.Build.props"
check "exits 0" exits 0
check "the props version, the only one there is to read: 1.0.0-rc.4" is "1.0.0-rc.4"
unset GIT_CEILING_DIRECTORIES

# --- 3. Refusals ----------------------------------------------------------------------------
echo "# refusals"
resolve "$props" --run 0
check "run number 0 is a usage error" exits 2
resolve "$props" --run abc
check "a run number that is no number is a usage error" exits 2
resolve "$props" --format rpm
check "an unknown format is a usage error" exits 2
resolve "$work/absent.props" --run 5
check "a props file that does not exist is a usage error" exits 2
write_props "$work/short.props" 1.0 rc.4
resolve "$work/short.props" --run 5
check "a prefix that is not Major.Minor.Patch fails" exits 1
write_props "$work/odd.props" 1.0.0 rc_4
resolve "$work/odd.props" --run 5
check "a suffix neither SemVer nor Debian can carry fails" exits 1

# --- 4. The repository's own props ----------------------------------------------------------
echo "# the repository's own props"
resolve "$here/../src/Directory.Build.props" --run 7
check "exits 0" exits 0
check "a dev version of run 7" bash -c '[[ "$0" =~ ^[0-9]+\.[0-9]+\.[0-9]+-([0-9A-Za-z.]+\.)?dev\.7$ ]]' "$out"

if [ "${#shells[@]}" -gt 0 ]; then
  echo "# resolve-version.ps1 (${shells[*]})"
  check "every answer above is the bash one" test "$twins" -eq 0
fi

if [ "$failed" -gt 0 ]; then echo "test-resolve-version: ${failed} check(s) failed"; exit 1; fi
echo "test-resolve-version passed"
