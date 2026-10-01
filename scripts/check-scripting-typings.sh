#!/usr/bin/env bash
# Typecheck every shipped .ork.ts example against the shipped Typings/*.d.ts, and every shipped
# .cmd.ts command against orkeon-cli.d.ts.
#
# WHY THIS EXISTS. The typings and the runtime are written in two different languages and
# nothing tied them together. Between them they had drifted six times, and every drift was
# found by a human reading C# next to TypeScript rather than by a gate:
#
#   ErrorAction   -- declared as a string union; the runtime wants objects from factories,
#                    so every `retry` silently became a failed run.
#   stateMachine  -- declared a top-level `transitions` ARRAY of {from,on,to}; the runtime
#                    reads a per-state `transitions` OBJECT keyed by event, with `target`.
#                    A script written to the declaration built a machine with NO transitions.
#   stateGraph    -- `edges` declared as an array, `circuitBreaker` instead of `graphConfig`,
#                    START/END declared as symbols where the runtime plants strings.
#   ctx.state     -- `.with()`, the ONLY legal way to mutate state, was undeclared.
#   globalThis    -- `crew`, `inputs` and `result` were undeclared, which is why every
#                    declarative crew in the repo carries an `as any` cast.
#   crew.run()    -- declared to resolve to a CrewResult it did not return.
#
# esbuild cannot catch any of these: it strips types and reports syntax only. Only a real
# typechecker, pointed at code that is known to run, can. That is what this does -- the
# examples are executed by the suite, so if they also typecheck, the two surfaces agree.
#
# The examples are copied to a scratch tree first: each .ork.ts becomes its own program (a
# shared one would collide on every `const crew`), and the `/// <reference orkeon-script="1.0" />`
# pragma, which tsc does not know, is stripped.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TOOLDIR="$ROOT/tools/scripting-typecheck"
TSC="$TOOLDIR/node_modules/.bin/tsc"
TYPINGS="$ROOT/src/scripting/Orkeon.Scripting/Typings"
CLI_TYPINGS="$ROOT/src/cli/Orkeon.Cli.Commands.Scripting/Typings/orkeon-cli.d.ts"
WORK="${TMPDIR:-/tmp}/orkeon-typecheck.$$"

if [ ! -x "$TSC" ]; then
    if ! command -v npm >/dev/null 2>&1; then
        echo "check-scripting-typings: npm is not available; cannot bootstrap tsc." >&2
        echo "  Install Node, or run: npm --prefix '$TOOLDIR' ci" >&2
        exit 2
    fi
    echo "check-scripting-typings: bootstrapping tsc in tools/scripting-typecheck ..."
    npm --prefix "$TOOLDIR" install --no-audit --no-fund --silent
fi

trap 'rm -rf "$WORK"' EXIT
mkdir -p "$WORK/typings" "$WORK/tree"
cp "$TYPINGS"/*.d.ts "$WORK/typings/"
cp "$CLI_TYPINGS" "$WORK/orkeon-cli.d.ts"

# Both pragma spellings go: the `orkeon-script` version is not a form tsc knows, and the
# `path=` one points at a per-project .orkeon/ folder that does not exist in this scratch
# tree -- the typings are supplied as explicit files instead.
strip_pragma() { mkdir -p "$(dirname "$2")"; sed -E 's|^/// <reference (orkeon-script\|path)=?.*$||' "$1" > "$2"; }

# EVERY .ork.ts under examples/, wherever it lives (GAP-12). This loop used to cover
# examples/scripting/ alone, plus two hand-picked folders; an example outside them
# (09-experimental/llm-response-format) drifted -- it handed `.llm()` a plain object the
# runtime throws away -- and no gate saw it. The tree is mirrored rather than flattened,
# so a script's relative imports (`../_tools/index.ts`) still resolve. `examples/others/`
# holds third-party checkouts and is not ours to check. `.cmd.ts` commands are mirrored too:
# they are typed by the CLI typings (orkeon-cli.d.ts), in the second loop below.
while IFS= read -r -d '' f; do
    rel="${f#"$ROOT"/}"
    strip_pragma "$f" "$WORK/tree/$rel"
done < <(find "$ROOT/examples" \( -path "$ROOT/examples/others" -o -name node_modules \) -prune \
            -o -type f -name '*.ts' ! -path '*/sample-files/*' -print0)

failed=0
checked=0
while IFS= read -r -d '' f; do
    checked=$((checked + 1))
    rel="${f#"$WORK"/tree/}"
    # tsc refuses --project alongside file arguments, so each script gets a generated
    # project that extends the shared base. The base stays the one copyable source of the
    # recommended options.
    cfg="$WORK/cfg-$checked.tsconfig.json"
    {
        printf '{ "extends": "%s", "files": [' "$TOOLDIR/tsconfig.base.json"
        first=1
        for d in "$WORK"/typings/*.d.ts; do
            [ $first -eq 1 ] || printf ', '
            printf '"%s"' "$d"
            first=0
        done
        printf ', "%s"] }' "$f"
    } > "$cfg"
    # Run from the tree root so tsc reports paths as `examples/...`.
    if ! out=$(cd "$WORK/tree" && "$TSC" --project "$cfg" 2>&1); then
        echo "FAIL $rel"
        echo "$out" | sed 's|^|  |'
        failed=$((failed + 1))
    fi
done < <(find "$WORK/tree" -type f -name '*.ork.ts' -print0 | sort -z)

if [ "$checked" -eq 0 ]; then
    echo "check-scripting-typings: no examples found -- refusing to report success." >&2
    exit 2
fi

# EVERY .cmd.ts under examples/, against orkeon-cli.d.ts alone (GAP-13). Nothing checked the
# command typings before: they declared `runCrew` as returning a Promise the runtime never
# returns, and a `"configuration"` service the whitelist refuses. A command is a script that
# calls the `defineCommand` global; the typings file is a module that declares it.
commands=0
while IFS= read -r -d '' f; do
    commands=$((commands + 1))
    rel="${f#"$WORK"/tree/}"
    cfg="$WORK/cmd-$commands.tsconfig.json"
    printf '{ "extends": "%s", "files": ["%s", "%s"] }' \
        "$TOOLDIR/tsconfig.base.json" "$WORK/orkeon-cli.d.ts" "$f" > "$cfg"
    if ! out=$(cd "$WORK/tree" && "$TSC" --project "$cfg" 2>&1); then
        echo "FAIL $rel"
        echo "$out" | sed 's|^|  |'
        failed=$((failed + 1))
    fi
done < <(find "$WORK/tree" -type f -name '*.cmd.ts' -print0 | sort -z)

if [ "$commands" -eq 0 ]; then
    echo "check-scripting-typings: no .cmd.ts examples found -- refusing to report success." >&2
    exit 2
fi
checked=$((checked + commands))

if [ "$failed" -gt 0 ]; then
    echo ""
    echo "check-scripting-typings: $failed of $checked example(s) do not typecheck."
    echo "Either the example is wrong, or the typings describe a runtime that does not exist."
    exit 1
fi

echo "check-scripting-typings: $checked shipped examples typecheck ($commands .cmd.ts against orkeon-cli.d.ts, the rest against Typings/*.d.ts)."
