#!/usr/bin/env bash
# Runs, before a pull request is opened or updated, what the CI checks and a local build does not.
# The rules are written in .github/pre-pr-checks.md. Usage: bash scripts/pre-pr-check.sh [--full] [base]
#   --full   also runs the Release build and the test pass of ci.yml
#   base     the branch the pull request targets (default: origin/main)
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

full=0
base="origin/main"
for arg in "$@"; do
  case "$arg" in
    --full) full=1 ;;
    *) base="$arg" ;;
  esac
done

failed=0
fail() { printf 'FAIL  %s\n' "$1"; failed=1; }
pass() { printf 'ok    %s\n' "$1"; }
note() { printf 'note  %s\n' "$1"; }

merge_base="$(git merge-base "$base" HEAD 2>/dev/null || true)"
if [ -z "$merge_base" ]; then
  fail "no merge base with $base: fetch it or name the target branch"
  exit 1
fi
# The lines the branch adds to C# files, as "path:line:text".
added="$(git diff "$merge_base" -U0 --diff-filter=AM -- '*.cs' | awk '
  /^\+\+\+ / { file = substr($0, 7); next }
  /^@@/ { split($3, at, ","); line = substr(at[1], 2) + 0; next }
  /^\+/ { print file ":" line ":" substr($0, 2); line++ }')"

# refuse: a pattern the CI refuses for sure. suspect: one it refuses in some shapes; read each line.
refuse() {
  local hits; hits="$(printf '%s\n' "$added" | grep -P "$2" | cut -c1-170)"
  if [ -n "$hits" ]; then fail "$1"; printf '%s\n' "$hits" | sed 's/^/        /'; else pass "$1"; fi
}
suspect() {
  local hits; hits="$(printf '%s\n' "$added" | grep -P "$2" | cut -c1-170)"
  if [ -n "$hits" ]; then note "$1: read these lines"; printf '%s\n' "$hits" | sed 's/^/        /'; else pass "$1"; fi
}

refuse "CA2026 (.NET 11): JsonDocument.Parse(...).RootElement in one expression" \
  'JsonDocument\.Parse\(.*\)\.RootElement'
refuse "CodeQL cs/useless-upcast: (string?)null" '\(string\?\)null'
refuse "CodeQL cs/equality-on-floats: comparison with Math.Floor" '[!=]=\s*Math\.(Floor|Round|Truncate)\('
suspect "CodeQL cs/local-not-disposed: a stream created inside a call" '\(new (MemoryStream|StringReader|StreamReader)\('
suspect "CodeQL cs/dereferenced-value-may-be-null: a member read with ?. and with !. on one line" '(\w+)\?[.\[].*\b\1!'

# An empty block: a line that is "{" followed by a line that is "}", both added, under a statement.
empty="$(printf '%s\n' "$added" | awk -F: '
  { text = $0; sub(/^[^:]*:[^:]*:/, "", text) }
  text ~ /^[ \t]*\}[ \t]*$/ && open && head ~ /(foreach|for|while|if|else|catch|finally|using|lock)[ \t(]/ { print where }
  { open = (text ~ /^[ \t]*\{[ \t]*$/); if (!open) { head = text; where = $1 ":" $2 } }')"
if [ -n "$empty" ]; then fail "CodeQL cs/empty-block: an empty block without a comment"; printf '%s\n' "$empty" | sed 's/^/        /'; else pass "CodeQL cs/empty-block"; fi

if command -v gitleaks >/dev/null 2>&1; then
  if gitleaks git --config .gitleaks.toml --redact --no-banner --log-opts="$merge_base..HEAD" . >/dev/null 2>&1; then
    pass "gitleaks on the branch's commits"
  else
    fail "gitleaks on the branch's commits (run it without the redirection to read the findings)"
  fi
else
  # Without the binary: a quoted value of 12 characters or more given to a key-named setting, unless the
  # value is one .gitleaks.toml already lists.
  listed="$(grep -oE '\?[^"?]+"\?\$' .gitleaks.toml | sed -E 's/^\?//; s/"\?\$$//; s/\\//g')"
  keys="$(printf '%s\n' "$added" \
    | grep -P '(?i)(key|secret|token|password)\w*\\?"?\s*[:=,\]]+\s*\\?"[A-Za-z0-9_\-]{12,}\\?"' \
    | grep -vFf <(printf '%s\n' "$listed") | cut -c1-170)"
  if [ -n "$keys" ]; then
    note "gitleaks is not installed; a random-looking value next to a key-named setting is refused: read these lines"
    printf '%s\n' "$keys" | sed 's/^/        /'
  else
    pass "no key-shaped literal added (gitleaks is not installed: a grep, not the scan)"
  fi
fi

if git ls-files -s -- '*.sh' | grep -qv '^100755'; then fail "a .sh file is not tracked as 100755"; else pass "executable bit"; fi

run() {
  local label="$1"; shift
  if "$@" >/dev/null 2>&1; then pass "$label"; else fail "$label ($*)"; fi
}
run "doc claims" python3 scripts/check-doc-claims.py
run "docs parity" bash scripts/check-docs-parity.sh
run "comment accents" python3 scripts/check-comment-accents.py
run "scripting typings" bash scripts/check-scripting-typings.sh

if [ "$full" -eq 1 ]; then
  run "Release build, warnings as errors" dotnet build Orkeon.sln --no-restore --no-incremental -c Release -warnaserror
  run "test pass of ci.yml" dotnet test Orkeon.sln --no-build -c Release --filter "Category!=Integration&Category!=Slow"
else
  note "build and tests not run: pass --full, or run the two commands of ci.yml"
fi

note "not checked here: the .NET 11 build and CodeQL beyond the patterns above (.github/pre-pr-checks.md)"
exit "$failed"
