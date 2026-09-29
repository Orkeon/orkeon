#!/usr/bin/env bash
# Regression test of scripts/prune-dev-packages.sh against a fake `gh`. That script
# deletes package versions from the GitHub feed, and its retention rules are all that
# stands between the dev channel and a deleted release, so they are proved here on
# every CI run: no network, no token.
#
# The fake answers the three endpoints the script reads (the tags, the organization's
# packages, a package's versions) from the tables below, logs every DELETE, and fails
# with HTTP 404 / 403 on the version ids named so. Run: bash scripts/test-prune-dev-packages.sh
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin" "$work/artifacts"

cat > "$work/bin/gh" <<'FAKE'
#!/usr/bin/env bash
if [ "$1 $2" = "api -X" ]; then
  echo "DELETE $4" >> "$FAKE_LOG"
  case "$4" in
    */versions/404) echo "gh: Not Found (HTTP 404)" >&2; exit 1 ;;
    */versions/403) echo "gh: Forbidden (HTTP 403)" >&2; exit 1 ;;
  esac
  exit 0
fi
case "$2" in
  repos/*/tags) [ -n "${FAKE_NO_TAGS:-}" ] || printf '%s\n' 0.9.2-beta 1.0.0-rc.3 1.0.0-rc.4 ;;
  "orgs/Orkeon/packages?"*) printf '%s\n' Orkeon Orkeon.Tools Orkeon.Old ;;
  orgs/Orkeon/packages/nuget/Orkeon/versions*)
    printf '%s\n' "1 1.0.0-rc.3" "2 1.0.0-rc.4" "3 1.0.0-rc.4.dev.110" "404 1.0.0-rc.4.dev.115" \
                  "5 1.0.0-rc.4.dev.120" "6 1.0.0-rc.4.dev.125" "7 0.9.1-beta" ;;
  orgs/Orkeon/packages/nuget/Orkeon.Tools/versions*)
    printf '%s\n' "11 1.0.0-rc.4" "403 1.0.0-rc.4.dev.110" "13 1.0.0-rc.4.dev.120" "14 1.0.1-dev.130" ;;
  orgs/Orkeon/packages/nuget/Orkeon.Old/versions*)
    printf '%s\n' "21 1.0.0-rc.4.dev.90" ;;
  *) echo "fake gh: unexpected call: $*" >&2; exit 1 ;;
esac
FAKE
chmod +x "$work/bin/gh"

# What the CI job passes: the packages it has just pushed, at the version it pushed.
touch "$work/artifacts/Orkeon.1.0.0-rc.4.dev.120.nupkg" \
      "$work/artifacts/Orkeon.Tools.1.0.0-rc.4.dev.120.nupkg" \
      "$work/artifacts/Orkeon.1.0.0-rc.4.dev.120.snupkg"

failed=0
check() { # <description> <command...>
  if "${@:2}"; then echo "ok    $1"; else echo "FAIL  $1"; failed=$((failed + 1)); fi
}
line()    { grep -qxF -- "$1" <<<"$out"; }   # a whole output line
no_line() { ! line "$1"; }
says()    { grep -qF -- "$1" <<<"$out"; }    # a fragment of the output
deleted() { grep -qxF -- "DELETE orgs/Orkeon/packages/nuget/$1/versions/$2" "$work/deletes"; }
deletes() { [ "$(grep -c . "$work/deletes" || true)" -eq "$1" ]; }
exits()   { [ "$code" -eq "$1" ]; }

prune() { # <env assignments...> -- <script arguments...>
  local envs=()
  while [ "$1" != "--" ]; do envs+=("$1"); shift; done
  shift
  : > "$work/deletes"
  code=0
  out="$(env PATH="$work/bin:$PATH" FAKE_LOG="$work/deletes" GITHUB_REPOSITORY=Orkeon/orkeon "${envs[@]}" \
         bash "$here/prune-dev-packages.sh" "$@" 2>&1)" || code=$?
}

echo "# dry run, every package: each package keeps its newest dev build"
prune --
check "exits 0" exits 0
check "deletes nothing" deletes 0
check "older Orkeon dev builds listed" line "would delete  Orkeon 1.0.0-rc.4.dev.110"
check "... including 115" line "would delete  Orkeon 1.0.0-rc.4.dev.115"
check "... and 120" line "would delete  Orkeon 1.0.0-rc.4.dev.120"
check "the newest Orkeon dev build is kept" no_line "would delete  Orkeon 1.0.0-rc.4.dev.125"
check "a newer prefix counts by its number (1.0.1-dev.130 kept)" no_line "would delete  Orkeon.Tools 1.0.1-dev.130"
check "tagged versions are never listed" no_line "would delete  Orkeon 1.0.0-rc.4"
check "an untagged non-dev version is only reported" line "  Orkeon 0.9.1-beta"
check "a package's single version is kept" no_line "would delete  Orkeon.Old 1.0.0-rc.4.dev.90"

echo "# CI mode: --keep 120 --artifacts --apply"
prune -- --keep 1.0.0-rc.4.dev.120 --artifacts "$work/artifacts" --apply
check "exits 1 because of the 403" exits 1
check "deletes the older dev build" deleted Orkeon 3
check "tries the one an overlapping run already deleted" deleted Orkeon 404
check "reports it as already gone, not as a failure" line "already gone  Orkeon 1.0.0-rc.4.dev.115 (an overlapping run deleted it)"
check "tries the refused one" deleted Orkeon.Tools 403
check "reports the 403 as a failure" says "Orkeon.Tools 1.0.0-rc.4.dev.110: delete failed"
check "touches nothing else (kept, newer, tagged, other packages)" deletes 3
check "counts one deletion and one failure" line "1 version(s) deleted, 1 failure(s)."

echo "# --include-untagged --apply, every package: tags survive everything"
prune -- --include-untagged --apply
check "deletes the untagged non-dev version" deleted Orkeon 7
check "never a tagged version" bash -c '! grep -qE "/versions/(1|2|11)$" "$0"' "$work/deletes"
check "never a package's newest dev build" bash -c '! grep -qE "/versions/(6|14)$" "$0"' "$work/deletes"
check "never a package's last version" bash -c '! grep -q "Orkeon.Old" "$0"' "$work/deletes"

echo "# --keep 120, every package, dry run"
prune -- --keep 1.0.0-rc.4.dev.120
check "warns instead of deleting a package's last version" says "::warning::Orkeon.Old 1.0.0-rc.4.dev.90 is the package's last version"
check "keeps dev builds newer than the kept one" no_line "would delete  Orkeon 1.0.0-rc.4.dev.125"

echo "# refusals"
prune -- --keep 1.0.0-rc.4
check "--keep that is not a dev build exits 2" exits 2
prune -- --artifacts "$work/artifacts"
check "--artifacts without --keep exits 2" exits 2
prune -- --frobnicate
check "an unknown argument exits 2" exits 2
prune FAKE_NO_TAGS=1 -- --apply
check "no tag at all exits 1" exits 1
check "... and deletes nothing" deletes 0
prune -- --help
check "--help prints the header" says "Retention of the dev channel"

if [ "$failed" -gt 0 ]; then
  echo "test-prune-dev-packages: ${failed} check(s) failed"
  exit 1
fi
echo "test-prune-dev-packages passed"
