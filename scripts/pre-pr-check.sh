#!/usr/bin/env bash
# Runs, before a pull request is opened or updated, what the CI checks and a local build does not.
# The rules are written in .github/pre-pr-checks.md.
#   bash scripts/pre-pr-check.sh [--full] [base]   base: the branch the pull request targets (origin/main)
#   bash scripts/pre-pr-check.sh --self-test       plants every pattern in a throw-away repository
# FAIL: the CI refuses it for sure. note: code to read against the tables of .github/pre-pr-checks.md.
# --full runs, in their order, the steps of the ci.yml job build-test that run here. It cannot run:
# "Free up runner disk space" (runner only); `dotnet restore` (run it yourself when a dependency
# changed); test-smoke-notices.ps1 without pwsh; test-build-apt-index.sh without apt-ftparchive; the
# other jobs (build-test-net11 needs the .NET 11 SDK, resolve-version-windows needs Windows).
set -uo pipefail

SCRIPTS="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
failed=0
fail() { printf 'FAIL  %s\n' "$1"; failed=1; }
pass() { printf 'ok    %s\n' "$1"; }
note() { printf 'note  %s\n' "$1"; }
show() {  # show fail|note <label> <hits, one per line>
  if [ -z "$3" ]; then pass "$2"; return; fi
  if [ "$1" = fail ]; then fail "$2"; else note "$2: read these lines"; fi
  printf '%s\n' "$3" | cut -c1-170 | sed 's/^/        /'
}

# The lines the branch adds to C# files since $1 (renamed files included), as "path:line:text".
added_lines() {
  git -c core.quotePath=false diff -M --diff-filter=AMR -U0 "$1" -- '*.cs' | awk '
    /^diff --git / { header = 1; next }
    header && /^\+\+\+ / { file = substr($0, 7); sub(/\t$/, "", file); next }
    /^@@/ { header = 0; split($3, at, ","); line = substr(at[1], 2) + 0; next }
    !header && /^\+/ { print file ":" line ":" substr($0, 2); line++ }'
}
grep_added() { printf '%s\n' "$ADDED" | grep -P -- "$1"; }

# An empty block over several lines: "{" then "}", under the head of a statement. The head is the
# added line that starts the statement: lines inside an open parenthesis (a call spread over several
# lines) continue it; a "{" starts a new context. CodeQL leaves an empty "using" body alone
# (RunnerHostSettingsValueTests, analysed on this branch, carries one), so "using" is no head here.
empty_blocks() {
  printf '%s\n' "$ADDED" | awk '
    { match($0, /:[0-9]+:/); file = substr($0, 1, RSTART - 1); n = substr($0, RSTART + 1, RLENGTH - 2) + 0
      text = substr($0, RSTART + RLENGTH)
      if (file != pf || n != pn + 1) { depth = 0; open = 0; head = "" }
      pf = file; pn = n }
    text ~ /^[ \t]*\}[ \t]*;?[ \t]*$/ && open && head ~ /^[ \t]*(\}[ \t]*)?(else[ \t]+)?(await[ \t]+)?(foreach|for|while|if|else|catch|finally|lock|try|do)([^A-Za-z0-9_]|$)/ {
      print where ":" head }
    text ~ /^[ \t]*\{[ \t]*$/ { open = 1; depth = 0; next }
    { open = 0
      if (depth <= 0) { head = text; where = file ":" n; depth = 0 }
      code = text; gsub(/"([^"\\]|\\.)*"/, "", code); sub(/\/\/.*/, "", code)
      depth += gsub(/\(/, "(", code) - gsub(/\)/, ")", code)
      if (code ~ /\{[ \t]*$/) depth = 0 }'
}
# One line: "catch { }", "else { }", "if (x) { }", "catch (E) { }"; never a method, an initializer or "{ get; }".
ONE_LINE_EMPTY='^[^:]*:\d+:(?!\s*//)(.*\b(catch|finally|else|try|do)\s*\{\s*\}|\s*(\}\s*)?(else\s+)?(await\s+)?(if|for|foreach|while|lock|catch)\s*\(.*\)\s*\{\s*\})'

# The same receiver read with ?. or ?[ and, within 5 added lines of the same file, with !. or ![.
null_then_bang() {
  printf '%s\n' "$ADDED" | awk '
    function has(s, p,   i, c) {
      while ((i = index(s, p)) > 0) { c = i > 1 ? substr(s, i - 1, 1) : ""; if (c !~ /[A-Za-z0-9_.]/) return 1; s = substr(s, i + 1) }
      return 0 }
    { match($0, /:[0-9]+:/); file = substr($0, 1, RSTART - 1); n = substr($0, RSTART + 1, RLENGTH - 2) + 0
      text = substr($0, RSTART + RLENGTH); t = text
      while (match(t, /[A-Za-z_][A-Za-z0-9_.]*\?[.[]/)) {
        seen[file SUBSEP substr(t, RSTART, RLENGTH - 2)] = n; t = substr(t, RSTART + RLENGTH) }
      for (k in seen) {
        split(k, kr, SUBSEP)
        if (kr[1] == file && n - seen[k] <= 5 && (has(text, kr[2] "!.") || has(text, kr[2] "!["))) { print $0; delete seen[k]; break } } }'
}

# Without gitleaks: a literal of 12 characters or more, digits AND letters, given to a key-named
# setting, unless .gitleaks.toml already lists it. Words joined by dashes ("unused-in-tests") pass.
key_literals() {
  local listed=""
  [ -f .gitleaks.toml ] && listed="$(grep -oE '\?[^"?]+"\?\$' .gitleaks.toml | sed -E 's/^\?//; s/"\?\$$//; s/\\//g')"
  grep_added '(?i)(key|secret|token|password)\w*\\?"?\s*[:=,\]]+\s*\\?"(?=[\w-]*\d)(?=[\w-]*[a-z])[\w-]{12,}\\?"' \
    | if [ -n "$listed" ]; then grep -vFf <(printf '%s\n' "$listed"); else cat; fi
}

# The pattern section, on the lines added since $1. SELF_TEST=1 uses the grep instead of gitleaks.
patterns() {
  ADDED="$(added_lines "$1")"
  show fail "CA2026 (.NET 11): JsonDocument.Parse(...).RootElement in one expression" \
    "$(grep_added 'JsonDocument\.Parse\(.*\)\.RootElement')"
  show fail "CodeQL cs/useless-upcast: (string?)null" "$(grep_added '\(string\?\)null')"
  show fail "CodeQL cs/equality-on-floats: comparison with Math.Floor" \
    "$(grep_added '[!=]=\s*Math\.(Floor|Round|Truncate)\(')"
  show fail "CodeQL cs/empty-block: an empty block without a comment" \
    "$(grep_added "$ONE_LINE_EMPTY"; empty_blocks)"
  show note "CodeQL cs/local-not-disposed: a stream created inside a call" \
    "$(grep_added '\(new (MemoryStream|StringReader|StreamReader)\(')"
  show note "CodeQL cs/dereferenced-value-may-be-null: a value read with ?. then with ! within 5 lines" \
    "$(null_then_bang)"
  show fail "CodeQL cs/useless-cast-to-self: a collection expression cast to an array" \
    "$(grep_added '\((string|bool|byte|int|long|double|char|object)\[\]\)\[')"
  if [ -z "${SELF_TEST:-}" ] && command -v gitleaks >/dev/null 2>&1; then
    if gitleaks git --config .gitleaks.toml --redact --no-banner --log-opts="$1..HEAD" . >/dev/null 2>&1; then
      pass "gitleaks on the branch's commits"
    else
      fail "gitleaks on the branch's commits (run it without the redirection to read the findings)"
    fi
  else
    show note "gitleaks is not installed (a grep, not the scan): a random-looking value next to a key-named setting" \
      "$(key_literals)"
  fi
}

# docs.yml: docfx --warningsAsErrors refuses a link to an anchor the page does not hold (InvalidBookmark).
doc_anchors() {
  local out
  out="$(python3 "$SCRIPTS/check-doc-anchors.py" 2>&1)" && out=""
  show fail "docfx InvalidBookmark: a link to an anchor no heading of the page gives" "$out"
}

# file-modes.yml: every *.sh and every text file whose line 1 is a shebang is tracked as 100755.
exec_bits() {
  local bad
  bad="$( { git -c core.quotePath=false ls-files -- '*.sh'
            git -c core.quotePath=false grep -I -n --no-color -e '^#!' -- . | awk -F: '$2 == 1 { print $1 }'; } | sort -u \
    | awk -v ex='scripts/installer-assets/wrapper.sh.tmpl' '
        NR == FNR { split($0, t, "\t"); split(t[1], m, " "); mode[t[2]] = m[1]; next }
        $0 != ex && ($0 in mode) && mode[$0] !~ /^(100755|160000|120000)$/ { print $0 " (" mode[$0] ")" }' \
      <(git -c core.quotePath=false ls-files -s) -)"
  show fail "exec-bit: a shell or shebang script is not tracked as 100755 (git update-index --chmod=+x)" "$bad"
}

run() {
  local label="$1"; shift
  if "$@" >/dev/null 2>&1; then pass "$label"; else fail "$label ($*)"; fi
}
run_if() {  # run_if <command it needs> <label> <command...>
  local tool="$1"; shift
  if command -v "$tool" >/dev/null 2>&1; then run "$@"; else note "$1 not run: $tool is not installed"; fi
}
ci_build_test() {  # ci.yml job build-test, step by step, without the restore
  run "docs parity" bash scripts/check-docs-parity.sh
  run "doc claims" python3 scripts/check-doc-claims.py
  run "doc-claims enumeration test" python3 scripts/test-check-doc-claims.py
  run "third-party notices generator test" python3 scripts/test-third-party-notices.py
  run "comment accents" python3 scripts/check-comment-accents.py
  run "dev-channel prune test" bash scripts/test-prune-dev-packages.sh
  run "smoke notices test" bash scripts/test-smoke-notices.sh
  run_if pwsh "smoke notices test, PowerShell twin" pwsh -NoProfile -File scripts/test-smoke-notices.ps1
  run "apt signing-key guard test" bash scripts/apt/test-check-signing-key.sh
  run "apt signing-key guard" bash scripts/apt/check-signing-key.sh
  run_if apt-ftparchive "apt index generator test" bash scripts/apt/test-build-apt-index.sh --offline
  run "apt installation block guard test" bash scripts/apt/test-check-apt-snippet.sh
  run "apt installation block guard" bash scripts/apt/check-apt-snippet.sh
  run "apt branch publisher test" bash scripts/apt/test-publish-apt-channel.sh
  run "apt dev channel test" bash scripts/apt/test-publish-dev.sh
  run "off-tag version resolver test" bash scripts/test-resolve-version.sh
  run "release asset-name guard test" bash scripts/test-check-release-assets.sh
  run "source installer prerequisite test" bash scripts/test-install-from-source.sh
  run "scripting typings" bash scripts/check-scripting-typings.sh
  run "third-party notices" python3 scripts/third-party-notices.py --check
  run "Release build, warnings as errors" dotnet build Orkeon.sln -c Release --no-restore --no-incremental -warnaserror
  run "built orkeon.d.ts roll-up" env CONFIGURATION=Release bash scripts/check-scripting-typings.sh --rollup
  run "unit and fast tests" dotnet test Orkeon.sln -c Release --no-build --filter "Category!=Integration&Category!=Slow"
  run "Embeddings.Local ONNX tests" dotnet test tests/tools/Orkeon.Tools.Embeddings.Local.Tests/Orkeon.Tools.Embeddings.Local.Tests.csproj \
    -c Release --no-build --filter "Category=Integration|Category=Slow"
}

# Plants one added line per pattern, and the shapes that must not fire, in a repository under
# ${TMPDIR:-/tmp}; fails when a pattern stays silent or a negative case fires.
self_test() {
  local out pairs c bad=0
  SELF_REPO="$(mktemp -d "${TMPDIR:-/tmp}/pre-pr-self-test.XXXXXX")" || return 2
  trap 'rm -rf "$SELF_REPO"' EXIT
  cd "$SELF_REPO" && git init -q . || return 2
  local utf8=$'caf\xc3\xa9'
  printf 'namespace Old;\n\npublic sealed class Old\n{\n    public int A { get; init; }\n    public int B { get; init; }\n    public int C { get; init; }\n}\n' > old.cs
  git add -A && git -c user.name=self-test -c user.email=self-test@invalid commit -q -m base || return 2
  mkdir -p moved "dir with space" "$utf8"
  git mv old.cs moved/must-renamed.cs
  printf '    var e = JsonDocument.Parse(json).RootElement;\n' >> moved/must-renamed.cs
  printf '    var t = (1, (string?)null);\n' > "dir with space/must-space.cs"
  printf '    var t = (1, (string?)null);\n' > "$utf8/must-utf8.cs"
  printf '    if (v != Math.Floor(v)) return;\n' > must-floats.cs
  printf '    Use(new MemoryStream(bytes));\n' > must-disposed.cs
  printf '    try { Run(); } catch { }\n' > must-catch.cs
  printf '    if (ready) { }\n' > must-if.cs
  printf 'if (a)\n{\n    Run();\n}\nelse\n{\n}\n' > must-else.cs
  printf '%s\n' '        await Assert.ThrowsAsync<X>(async () =>' '        {' \
    '            await foreach (var _ in provider.GenerateStreamingAsync(' '                "go",' \
    '                WithSchema("""{"type":"object"}"""),' '                ct))' '            {' '            }' \
    '        });' > must-foreach.cs
  printf '%s\n' '    Assert.Equal("x", e.Payload?["role"]?.GetValue<string>());' '    // the same payload' \
    '    Assert.False(e.Payload!.ContainsKey("taskId"));' > must-nullbang.cs
  printf '%s\n' '    var s = """{ "ApiKeyEnvVar": "ORKEON_TEST_451_NO_SUCH_KEY" }""";' > must-key-env.cs
  printf '%s\n' '    Settings((key, "wrkspc_01Test"));' > must-key-id.cs
  printf '%s\n' 'public sealed class Empty' '{' '}' 'public sealed record R' '{' '}' 'public class C : B' '{' \
    '    public C() : base(1)' '    {' '    }' '    public void M(' '        int a,' '        int b)' '    {' '    }' \
    '    public void N() { }' '    public int P { get; }' '    private readonly List<int> list = new List<int>' '    {' \
    '    };' '    private readonly Foo foo = new Foo() { };' '    // catch { }' '    void L()' '    {' \
    '        foreach (var x in xs)' '        {' '            // drained on purpose' '        }' '        using (Build(a,' '            b))' \
    '        {' '        }' '    }' '}' > never-shapes.cs
  printf '%s\n' '    string? apiKey = "unused-in-tests";' '    var a = first?.Name;' '    var b = other!.Name;' \
    '    F();' '    F();' '    F();' '    F();' '    F();' '    var c = first!.Name;' > never-other.cs
  printf '%s\n' '    foreach (var x in (string[])["a", "b"])' '    {' '        Run(x);' '    }' > must-cast.cs
  mkdir -p docs
  printf '%s\n' '# Ce que le canal écoute' '[ce qu'"'"'il prend](#ce-quécoute-le-canal)' > must-anchor.md
  printf '%s\n' '[x](../never-anchor.md#no-such-heading)' > docs/must-page.md
  printf '%s\n' '# L'"'"'hôte : `crew_files` & A2A' '## Twice' '## Twice' '```' '# not a heading' '[x](#no)' '```' \
    '`[x](#no)` <a id="by-hand"></a>' '<!-- [x](#no) -->' '[a](#lhôte--crew_files--a2a) [b](#twice-1) [c](#by-hand)' \
    '[d](must-anchor.md#ce-que-le-canal-écoute) [e](code.cs#L3) [f](https://example.invalid/a.md#b)' \
    '[g](#lh%C3%B4te--crew_files--a2a)' > never-anchor.md
  git add -A && git -c user.name=self-test -c user.email=self-test@invalid commit -q -m branch || return 2
  git diff -M --name-status HEAD~1 | grep -q '^R' || { echo "self-test: git did not see the rename"; return 2; }
  out="$(SELF_TEST=1 patterns HEAD~1; doc_anchors)"
  pairs="$(printf '%s\n' "$out" | awk '/^(FAIL|note)/ { h = $0; next }
    /^        / { p = $0; sub(/^ +/, "", p); match(p, /:[0-9]+:/); print h "|" substr(p, 1, RSTART - 1) }')"
  for c in "moved/must-renamed.cs|CA2026" "dir with space/must-space.cs|useless-upcast" \
           "$utf8/must-utf8.cs|useless-upcast" "must-floats.cs|equality-on-floats" \
           "must-disposed.cs|local-not-disposed" "must-catch.cs|empty-block" "must-if.cs|empty-block" \
           "must-else.cs|empty-block" "must-foreach.cs|empty-block" "must-nullbang.cs|may-be-null" \
           "must-key-env.cs|key-named" "must-key-id.cs|key-named" \
           "must-cast.cs|cast-to-self" "must-anchor.md|InvalidBookmark" "docs/must-page.md|InvalidBookmark"; do
    if printf '%s\n' "$pairs" | awk -F'|' -v p="${c%%|*}" -v l="${c#*|}" '$NF == p && index($1, l) { f = 1 } END { exit !f }'; then pass "fires: $c"; else fail "silent: $c"; bad=1; fi
  done
  if printf '%s\n' "$pairs" | grep -qF 'never-'; then
    fail "a negative case fires"; printf '%s\n' "$pairs" | grep -F 'never-' | sed 's/^/        /'; bad=1
  else
    pass "no negative case fires (empty method, constructor, class, record, using, initializers, { get; }, comments, anchors)"
  fi
  return "$bad"
}

if ! printf 'ab\n' | grep -qP 'a(?=b)' 2>/dev/null; then
  echo "FAIL  grep -P (PCRE) does not work here: every check would print ok. Install GNU grep (macOS: brew install grep, gnubin first on PATH)." >&2
  exit 2
fi
full=0; base="origin/main"
for arg in "$@"; do
  case "$arg" in
    --full) full=1 ;;
    --self-test) self_test; exit $? ;;
    *) base="$arg" ;;
  esac
done
cd "$(git rev-parse --show-toplevel)" || exit 2
merge_base="$(git merge-base "$base" HEAD 2>/dev/null || true)"
if [ -z "$merge_base" ]; then fail "no merge base with $base: fetch it or name the target branch"; exit 1; fi

patterns "$merge_base"
doc_anchors
exec_bits
if [ "$full" -eq 1 ]; then
  ci_build_test
else
  run "doc claims" python3 scripts/check-doc-claims.py
  run "docs parity" bash scripts/check-docs-parity.sh
  run "comment accents" python3 scripts/check-comment-accents.py
  run "scripting typings" bash scripts/check-scripting-typings.sh
  note "build, tests and the other build-test steps not run: pass --full"
fi
note "not checked here: the .NET 11 build and CodeQL beyond the patterns above (.github/pre-pr-checks.md)"
exit "$failed"
