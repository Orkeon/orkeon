#!/usr/bin/env bash
# Regression test of scripts/install-from-source.sh, on a PATH this bench composes: no
# build, no network, nothing installed.
#
# It proves what the script owes before it compiles anything: a missing tool -- dotnet,
# git, python3, tar, curl, openssl -- is named with where to get it; several missing tools
# are named together, in one run; an SDK that does not satisfy global.json is told apart
# from no SDK at all; and --dry-run, with everything present, prints the version
# scripts/resolve-version.sh gives and the two commands, and runs neither.
#
# The checked tools are stubs, except git, which the version needs for real. The dotnet
# stub records every call: a `publish` in its log means the script built something it had
# just said it could not, or would not.
# Run: bash scripts/test-install-from-source.sh
set -euo pipefail
export LC_ALL=C

here="$(cd "$(dirname "$0")" && pwd)"
script="$here/install-from-source.sh"

failed=0
check() { # <description> <command...>
  if "${@:2}"; then echo "ok    $1"; else echo "FAIL  $1"; failed=$((failed + 1)); fi
}
exits() { [ "$code" -eq "$1" ]; }
says()  { printf '%s\n' "$out" | grep -Fq -- "$1"; }
silent_on() { ! says "$1"; }
built_nothing() { ! grep -q 'publish' "$work/dotnet.log" 2>/dev/null && silent_on 'Packaging Orkeon'; }

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# What the script itself calls before it builds, the tools it checks for left out.
BASE_TOOLS="bash sh sed grep head tr uname dirname"
CHECKED_TOOLS="dotnet git python3 tar curl openssl"

# compose <name> <tool>...: a directory holding the base tools and the checked tools named,
# to be the whole PATH of one run. Prints the directory.
compose() {
  local dir="$work/path-$1" tool; shift
  mkdir -p "$dir"
  for tool in $BASE_TOOLS; do ln -s "$(command -v "$tool")" "$dir/$tool"; done
  for tool in "$@"; do
    case "$tool" in
      git) ln -s "$(command -v git)" "$dir/git" ;;
      dotnet)
        cat > "$dir/dotnet" <<STUB
#!/bin/sh
echo "\$*" >> "$work/dotnet.log"
case "\$1" in
  --version)   [ -n "\${STUB_SDK_UNFIT:-}" ] && exit 145; echo 10.0.300 ;;
  --list-sdks) echo "9.0.100 [/usr/share/dotnet/sdk]" ;;
esac
exit 0
STUB
        chmod +x "$dir/dotnet" ;;
      *) printf '#!/bin/sh\nexit 0\n' > "$dir/$tool"; chmod +x "$dir/$tool" ;;
    esac
  done
  printf '%s' "$dir"
}

# without <tool>...: the checked tools, those named left out.
without() {
  local tool kept=()
  for tool in $CHECKED_TOOLS; do
    case " $* " in *" $tool "*) ;; *) kept+=("$tool") ;; esac
  done
  printf '%s ' "${kept[@]}"
}

# run <path> [args...]: $out is what the script printed on both streams, $code its exit.
run() {
  local path="$1"; shift
  : > "$work/dotnet.log"
  code=0; out="$(PATH="$path" "$BASH" "$script" "$@" 2>&1)" || code=$?
}

# --- 1. One tool missing: named, with where to get it, and nothing built ---------------------
echo "# one prerequisite missing"
# shellcheck disable=SC2046
run "$(compose no-dotnet $(without dotnet))" --dry-run
check "no dotnet: exits 1" exits 1
check "no dotnet: names the .NET SDK and the version global.json pins" says "the .NET SDK $(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$here/../global.json" | head -n1)"
check "no dotnet: says where to get it" says "https://dotnet.microsoft.com/download/dotnet/"
check "no dotnet: one thing missing" says "lacks 1 thing(s)"
check "no dotnet: nothing built" built_nothing

# shellcheck disable=SC2046
run "$(compose no-git $(without git))" --dry-run
check "no git: exits 1" exits 1
check "no git: names git" says "  - git,"
check "no git: says where to get it" says "https://git-scm.com/downloads"
check "no git: nothing built" built_nothing

# shellcheck disable=SC2046
run "$(compose no-python $(without python3))" --dry-run
check "no python3: exits 1" exits 1
check "no python3: names Python 3" says "  - Python 3 (python3)"
check "no python3: says where to get it" says "https://www.python.org/downloads/"
check "no python3: nothing built" built_nothing

# shellcheck disable=SC2046
run "$(compose no-tar $(without tar))" --dry-run
check "no tar: exits 1" exits 1
check "no tar: names tar" says "  - tar,"
check "no tar: says where to get it" says "package manager"
check "no tar: nothing built" built_nothing

# shellcheck disable=SC2046
run "$(compose no-curl $(without curl))" --dry-run
check "no curl: exits 1 and names curl" bash -c '[ "$0" -eq 1 ] && printf "%s" "$1" | grep -Fq "  - curl,"' "$code" "$out"
# shellcheck disable=SC2046
run "$(compose no-openssl $(without openssl))" --dry-run
check "no openssl: exits 1 and names openssl" bash -c '[ "$0" -eq 1 ] && printf "%s" "$1" | grep -Fq "  - openssl,"' "$code" "$out"

# --- 2. Several missing: all of them in one run -----------------------------------------------
echo "# several prerequisites missing"
# shellcheck disable=SC2046
run "$(compose no-dotnet-python $(without dotnet python3))" --dry-run
check "no dotnet, no python3: exits 1" exits 1
check "names the .NET SDK" says "  - the .NET SDK"
check "and Python 3, in the same run" says "  - Python 3 (python3)"
check "two things missing" says "lacks 2 thing(s)"
check "says it installs none of it" says "It installs none of it for you."
check "nothing built" built_nothing

# shellcheck disable=SC2046
run "$(compose nothing $(without $CHECKED_TOOLS))" --dry-run
check "none of the six: exits 1 and counts six" bash -c '[ "$0" -eq 1 ] && printf "%s" "$1" | grep -Fq "lacks 6 thing(s)"' "$code" "$out"

# --- 3. An SDK that does not satisfy global.json is not "no SDK" ------------------------------
echo "# an SDK global.json refuses"
all="$(compose all $CHECKED_TOOLS)"
STUB_SDK_UNFIT=1 run "$all" --dry-run
check "exits 1" exits 1
check "names global.json" says "a .NET SDK that satisfies global.json"
check "names what is installed" says "installed: 9.0.100"
check "nothing built" built_nothing

# --- 4. Everything present, --dry-run: the version, the two commands, neither run --------------
echo "# everything present, --dry-run"
version="$(bash "$here/resolve-version.sh")"
run "$all" --dry-run
check "exits 0" exits 0
check "prints the version resolve-version.sh gives ($version)" says "Orkeon $version, from the sources"
check "would pack the tree without an archive" says "package-installers.sh --app-set cli --rids "
check "passes --no-archive" says " --no-archive"
check "passes that version" says "--version $version "
check "would run the tree's install.sh under the default prefix" says "/_stage/orkeon-cli-$version-"
check "the default prefix is ~/.local" says "/install.sh --prefix $HOME/.local"
check "says it is a dry run" says "--dry-run: nothing is built, nothing is installed."
check "nothing built" built_nothing

run "$all" --dry-run --app-set full --prefix /opt/orkeon --modify-path
check "full set: exits 0" exits 0
check "full set: the packager is asked for it" says "--app-set full "
check "full set: the tree is the multi-app one" says "/_stage/orkeon-$version-"
check "the prefix and --modify-path reach install.sh" says "/install.sh --prefix /opt/orkeon --modify-path"

# --- 5. --uninstall builds nothing and needs nothing --------------------------------------------
echo "# --uninstall"
# shellcheck disable=SC2046
bare="$(compose bare $(without $CHECKED_TOOLS))"
run "$bare" --uninstall --prefix "$work/empty"
check "nothing installed: exits 1" exits 1
check "nothing installed: says so" says "Nothing to uninstall"

mkdir -p "$work/prefix/lib/orkeon"
printf '#!/bin/sh\necho "installed-copy $*"\n' > "$work/prefix/lib/orkeon/install.sh"
run "$bare" --uninstall --prefix "$work/prefix"
check "an installation: exits 0 with no build tool on the PATH" exits 0
check "runs the installed copy of install.sh with --uninstall" says "installed-copy --prefix $work/prefix --uninstall"

# --- 6. Usage ------------------------------------------------------------------------------------
echo "# usage"
run "$all" --bogus
check "an unknown argument exits 2" exits 2
run "$all" --app-set everything
check "an unknown app set exits 2" exits 2
run "$all" --help
check "--help exits 0 and shows the three lines" bash -c '[ "$0" -eq 0 ] && printf "%s" "$1" | grep -Fq "./scripts/install-from-source.sh"' "$code" "$out"

if [ "$failed" -gt 0 ]; then echo "test-install-from-source: ${failed} check(s) failed"; exit 1; fi
echo "test-install-from-source passed"
