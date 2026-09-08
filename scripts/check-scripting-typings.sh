#!/usr/bin/env bash
# Typecheck every shipped .ork.ts example against the shipped Typings/*.d.ts.
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
# The examples are copied to a scratch tree first: each becomes its own program (a shared
# one would collide on every `const crew`), and the `/// <reference orkeon-script="1.0" />`
# pragma, which tsc does not know, is stripped.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TOOLDIR="$ROOT/tools/scripting-typecheck"
TSC="$TOOLDIR/node_modules/.bin/tsc"
TYPINGS="$ROOT/src/scripting/Orkeon.Scripting/Typings"
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
mkdir -p "$WORK/typings" "$WORK/scripts"
cp "$TYPINGS"/*.d.ts "$WORK/typings/"

strip_pragma() { sed 's|^/// <reference orkeon-script.*$||' "$1" > "$2"; }

for f in "$ROOT"/examples/scripting/*.ork.ts; do
    strip_pragma "$f" "$WORK/scripts/$(basename "$f" .ork.ts).ts"
done

# The declarative crew lives in its own folder and imports a sibling module; flatten both,
# rewriting the import so the pair still resolves.
if [ -f "$ROOT/examples/scripting/crew-review-desk/main.ork.ts" ]; then
    sed 's|^/// <reference orkeon-script.*$||; s|"./tools/index.ts"|"./_crew-review-desk-tools"|' \
        "$ROOT/examples/scripting/crew-review-desk/main.ork.ts" > "$WORK/scripts/crew-review-desk.ts"
    cp "$ROOT/examples/scripting/crew-review-desk/tools/index.ts" \
       "$WORK/scripts/_crew-review-desk-tools.ts"
fi

failed=0
checked=0
for f in "$WORK"/scripts/*.ts; do
    case "$(basename "$f")" in _*) continue ;; esac   # imported modules, checked with their importer
    checked=$((checked + 1))
    # tsc refuses --project alongside file arguments, so each script gets a generated
    # project that extends the shared base. The base stays the one copyable source of the
    # recommended options.
    cfg="$WORK/$(basename "$f" .ts).tsconfig.json"
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
    if ! out=$("$TSC" --project "$cfg" 2>&1); then
        echo "FAIL $(basename "$f")"
        echo "$out" | sed "s|$WORK/scripts/|  |"
        failed=$((failed + 1))
    fi
done

if [ "$checked" -eq 0 ]; then
    echo "check-scripting-typings: no examples found -- refusing to report success." >&2
    exit 2
fi

if [ "$failed" -gt 0 ]; then
    echo ""
    echo "check-scripting-typings: $failed of $checked example(s) do not typecheck."
    echo "Either the example is wrong, or the typings describe a runtime that does not exist."
    exit 1
fi

echo "check-scripting-typings: $checked shipped examples typecheck against Typings/*.d.ts."
