# Review desk — a crew written in TypeScript

Three agents, three tasks, a dependency chain and a file on the way out. This is the
worked example for authoring a crew in TypeScript: the loose scripts in
[`examples/scripting/`](../README.md) each show one primitive, this one shows them
assembled.

## The shape it uses

The last line is `globalThis.crew = crew`, not `await crew.run()`. That single difference
picks the engine:

| | this file | the numbered `0N-*.ork.ts` scripts |
|---|---|---|
| ends with | `globalThis.crew = crew` | `await crew.run()` |
| runs `.body()` | no | yes, one per agent |
| honours `withTask`, `process`, `manager` | yes | no |
| `--validate` | works | fails — nothing was handed off |

Write `.body()` and you are on the procedural shape; write `withTask` and you are on the
declarative one. Never both in one file.

## What is in it

- **`main.ork.ts`** — the crew. Agents carry built-in tools by name (`file_read`,
  `directory_read`) and TypeScript tools as instances; `withContext` chains the three
  tasks into a DAG; the last task declares a deliverable.
- **`tools/index.ts`** — three tools written here (`diff_stats`, `touched_files`,
  `risk_flags`) and a `pickTools` that throws on an unknown name. Same idiom as
  [`examples/03-finance-trading/_tools/`](../../03-finance-trading/_tools/index.ts).
- **`sample/pr-diff.txt`** — the diff the crew reviews, reachable at
  `/script/sample/pr-diff.txt` through the read-only `/script` mount.

## Run it

Check the whole definition loads — no API key, no model, about two seconds:

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- \
    run examples/scripting/crew-review-desk/main.ork.ts --validate
```

```
VALIDATION OK: …/crew-review-desk/main.ork.ts (agents=3, tasks=3, tools resolved=5)
```

A real run needs a provider, and a writable `/output` for the deliverable to land in:

```bash
mkdir -p out
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- \
    run examples/scripting/crew-review-desk/main.ork.ts \
    --settings examples/appsettings/appsettings.json \
    --mount "$PWD/out:/output:rw" --allow-external-mounts
```

## Expected output

`out/review.md` — the reporter's markdown summary — plus `out/AUTO_SUMMARY.md`, which the
runner always writes.

Without a provider the run still completes and still writes `AUTO_SUMMARY.md`, but no
`review.md`: the deliverable takes the task's final message, and the echo provider produces
none. That is the honest signal that the crew ran and the model did not.

## Approximate duration and cost

`--validate` is free and offline. A full run is three sequential LLM turns — a few thousand
tokens on the diff shipped here, under a minute on a small model.
