#!/usr/bin/env bash
# Regression test of smoke_assert_notices (scripts/smoke-onboarding/lib/smoke-common.sh),
# the check every POSIX release smoke runs on the notices of the payload it installed. It
# is all that fails a release whose packaging stopped copying the license, the third-party
# notices or the license folder of a bundled .NET runtime, so its rules are proved here on
# every CI run, on throwaway trees: no publish, no archive, no network.
#
# The trees are the layouts the smokes meet: an archive (or what install.sh makes of it),
# with its applications under libexec/, and the Debian package, with its notices under
# usr/share/doc/orkeon and one folder per application under usr/lib. Its PowerShell twin,
# scripts/test-smoke-notices.ps1, proves Invoke-OrkeonNoticesAssertions on the same trees.
# Run: bash scripts/test-smoke-notices.sh
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# shellcheck source=smoke-onboarding/lib/smoke-common.sh
source "$here/smoke-onboarding/lib/smoke-common.sh"
smoke_init_bookkeeping

NETCORE="Microsoft.NETCore.App"
DESKTOP="Microsoft.WindowsDesktop.App"

# app <directory> <name> <rid> [<bundled framework>...] -- what `dotnet publish` leaves
# beside an apphost: with no framework named, a framework-dependent application.
app() {
  local dir="$1" name="$2" rid="$3"; shift 3
  local included="" framework
  mkdir -p "$dir"
  for framework in "$@"; do
    included="$included${included:+, }{ \"name\": \"$framework\", \"version\": \"10.0.9\" }"
  done
  if [ -n "$included" ]; then
    printf '{ "runtimeOptions": { "tfm": "net10.0", "includedFrameworks": [ %s ] } }\n' "$included" \
      > "$dir/$name.runtimeconfig.json"
  else
    printf '{ "runtimeOptions": { "tfm": "net10.0", "framework": { "name": "%s", "version": "10.0.0" } } }\n' "$NETCORE" \
      > "$dir/$name.runtimeconfig.json"
  fi
  printf '{ "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0/%s", "signature": "" }, "libraries": {} }\n' "$rid" \
    > "$dir/$name.deps.json"
}

# notices <directory> <license file> [<pack>...] -- the license, the third-party notices
# and one license file per runtime pack named.
notices() {
  local dir="$1" license="$2" pack; shift 2
  mkdir -p "$dir"
  echo "license" > "$dir/$license"
  echo "notices" > "$dir/THIRD-PARTY-NOTICES.md"
  for pack in "$@"; do
    mkdir -p "$dir/licenses/$pack"
    echo "runtime license" > "$dir/licenses/$pack/LICENSE.TXT"
  done
}

failed=0
check() { # <description> <command...>
  if "${@:2}"; then echo "ok    $1"; else echo "FAIL  $1"; failed=$((failed + 1)); fi
}
says()    { grep -qF -- "$1" <<<"$out"; }
passes()  { [ "$code" -eq 0 ] && says "PASS"; }
refuses() { [ "$code" -ne 0 ] && says "FAIL"; }

assert_notices() { # <notices directory> <license file> <application directory>...
  code=0
  out="$(smoke_assert_notices "$@" 2>&1)" || code=$?
}

echo "# an archive: one self-contained application, one framework-dependent, esbuild"
tree="$work/archive"
notices "$tree" LICENSE.md "$NETCORE.Runtime.linux-x64"
app "$tree/libexec/orkeon" orkeon linux-x64 "$NETCORE"
app "$tree/libexec/orkeon-slim" orkeon linux-x64
mkdir -p "$tree/libexec/esbuild-bin" && touch "$tree/libexec/esbuild-bin/esbuild"
assert_notices "$tree" LICENSE.md "$tree"/libexec/*/
check "passes" passes
check "names the runtime's folder" says "licenses/$NETCORE.Runtime.linux-x64/"
check "names the file it holds" says "LICENSE.TXT"
check "names the license and the notices" says "LICENSE.md, THIRD-PARTY-NOTICES.md"

rm -rf "$tree/licenses"
assert_notices "$tree" LICENSE.md "$tree"/libexec/*/
check "without licenses/: fails" refuses
check "... and names the missing folder" says "licenses/$NETCORE.Runtime.linux-x64/"
check "... and the application that bundles the runtime" says "orkeon"

echo "# a WPF application bundles two runtimes"
tree="$work/wpf"
notices "$tree" LICENSE.md "$NETCORE.Runtime.win-x64"
app "$tree/libexec/orkeon" orkeon win-x64 "$NETCORE"
app "$tree/libexec/orkeon-studio" Orkeon.Studio win-x64 "$NETCORE" "$DESKTOP"
assert_notices "$tree" LICENSE.md "$tree"/libexec/*/
check "the .NET folder alone: fails" refuses
check "... and names the Windows Desktop pack" says "licenses/$DESKTOP.Runtime.win-x64/"
check "... and not the folder that is there" bash -c '! grep -qF -- "$1" <<<"$2"' _ "licenses/$NETCORE.Runtime.win-x64/ is" "$out"

notices "$tree" LICENSE.md "$NETCORE.Runtime.win-x64" "$DESKTOP.Runtime.win-x64"
assert_notices "$tree" LICENSE.md "$tree"/libexec/*/
check "both folders: passes" passes
check "... and names both" says "licenses/$DESKTOP.Runtime.win-x64/"
check "... each one once" bash -c '[ "$(grep -oF -- "$1" <<<"$2" | wc -l)" -eq 1 ]' _ "licenses/$NETCORE.Runtime.win-x64/" "$out"

echo "# what a packaging script can lose"
tree="$work/losses"
notices "$tree" LICENSE.md "$NETCORE.Runtime.linux-x64"
app "$tree/libexec/orkeon" orkeon linux-x64 "$NETCORE"
rm "$tree/licenses/$NETCORE.Runtime.linux-x64/LICENSE.TXT"
assert_notices "$tree" LICENSE.md "$tree"/libexec/*/
check "an empty runtime folder: fails" refuses
check "... and says it is empty" says "licenses/$NETCORE.Runtime.linux-x64/ is empty"

notices "$tree" LICENSE.md "$NETCORE.Runtime.linux-x64"
rm "$tree/THIRD-PARTY-NOTICES.md"
assert_notices "$tree" LICENSE.md "$tree"/libexec/*/
check "no THIRD-PARTY-NOTICES.md: fails" refuses
check "... and names it" says "THIRD-PARTY-NOTICES.md is missing"

notices "$tree" LICENSE.md "$NETCORE.Runtime.linux-x64"
rm "$tree/LICENSE.md"
assert_notices "$tree" LICENSE.md "$tree"/libexec/*/
check "no license: fails" refuses
check "... and names it" says "LICENSE.md is missing"

notices "$tree" LICENSE.md "$NETCORE.Runtime.linux-x64"
rm "$tree/libexec/orkeon/orkeon.deps.json"
assert_notices "$tree" LICENSE.md "$tree"/libexec/*/
check "a bundled runtime and no runtime identifier: fails" refuses
check "... and says what it could not read" says "runtime identifier"

echo "# the Debian package"
tree="$work/deb"
notices "$tree/usr/share/doc/orkeon" copyright "$NETCORE.Runtime.linux-arm64"
app "$tree/usr/lib/orkeon" orkeon linux-arm64 "$NETCORE"
app "$tree/usr/lib/orkeon-studio-config" Orkeon.Studio.Config linux-arm64 "$NETCORE"
app "$tree/usr/lib/orkeon-studio-run" Orkeon.Studio.Run linux-arm64 "$NETCORE"
assert_notices "$tree/usr/share/doc/orkeon" copyright "$tree/usr/lib/orkeon" "$tree"/usr/lib/orkeon-studio-*
check "passes on copyright" passes
check "names the license file of the layout" says "copyright, THIRD-PARTY-NOTICES.md"
check "names the runtime's folder" says "licenses/$NETCORE.Runtime.linux-arm64/"

echo "# nothing to read is never a pass"
tree="$work/empty"
notices "$tree" LICENSE.md
mkdir -p "$tree/libexec/esbuild-bin"
assert_notices "$tree" LICENSE.md "$tree"/libexec/*/
check "no *.runtimeconfig.json in the folders given: fails" refuses
check "... and says so" says "no *.runtimeconfig.json"
assert_notices "$tree" LICENSE.md "$tree"/nowhere/*/
check "a glob that matched nothing: fails" refuses
assert_notices "$tree" LICENSE.md
check "no application folder at all: fails" refuses

echo "# framework-dependent applications only"
tree="$work/slim"
notices "$tree" LICENSE.md
app "$tree/libexec/orkeon-slim" orkeon linux-x64
app "$tree/libexec/orkeon-repl" Orkeon.ConsoleApp linux-x64
assert_notices "$tree" LICENSE.md "$tree"/libexec/*/
check "no licenses/ and no bundled runtime: passes" passes
check "... and says no runtime is bundled" says "no bundled runtime"

if [ "$failed" -gt 0 ]; then
  echo "test-smoke-notices: ${failed} check(s) failed"
  exit 1
fi
echo "test-smoke-notices passed"
