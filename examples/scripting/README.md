# Orkeon Scripting DSL — examples

Each file is a self-contained `.ork.ts` (TypeScript-syntax) script runnable through
the `orkeon` CLI:

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run examples/scripting/01-hello-world.ork.ts
```

All examples fall back to the `UndefinedLlm` echo provider when no real provider is
configured, so they are runnable without API keys.

| File | What it shows |
|------|---------------|
| `01-hello-world.ork.ts` | Minimal crew + 1 agent + 1 task |
| `02-procedural.ork.ts` | Body without LLM — deterministic output |
| `03-mixed-llm.ork.ts` | Body delegating to `ctx.llm.complete` |
| `04-dynamic-spawn.ork.ts` | `ctx.spawn()` to add an agent at runtime |
| `05-state-with-and-lock.ork.ts` | `state.with` + `ctx.lock` critical section |
| `06-custom-tool-and-hooks.ork.ts` | `toolBuilder` + `onAgentStart` / `onAgentStop` |
| `07-fsm-and-graph.ork.ts` | `stateMachine` and `stateGraph` literal forms |
| `08-rag.ork.ts` | First-class `rag.ingest` / `rag.query` over the RAG subsystem (offline) |
| `09-tools-and-act.ork.ts` | The three tool surfaces, then `ctx.llm.act` — the tool-calling loop |
| `10-inputs-and-memory.ork.ts` | `globalThis.inputs`, agent state, crew memory, `ErrorAction.retry` |

Every file above ends with `await crew.run()` — the **procedural** shape, where each agent's
`.body()` runs and tasks are ignored. [`crew-review-desk/`](crew-review-desk/README.md) is
the other one: a three-agent crew with tasks, a dependency chain and a deliverable, handed
off with `globalThis.crew = crew`. Mixing the two endings is the mistake this catalogue is
arranged to prevent.

`08-rag.ork.ts` ingests a corpus shipped in `data/08-rag/` and needs two extra
flags for the full offline experience — a writable `/output` mount (persists
the ingestion manifest, proving the second ingest embeds nothing) and an
LLM-less settings file (no localhost retries):

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run examples/scripting/08-rag.ork.ts \
  --settings examples/scripting/08-rag.appsettings.json \
  --mount "$(mktemp -d)":/output:rw --allow-external-mounts
```

`10-inputs-and-memory.ork.ts` reads `--inputs`, and falls back to a default without it:

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run examples/scripting/10-inputs-and-memory.ork.ts \
  --inputs '{"topic":"espresso"}'
```

## Where to go next

- [Write a crew in TypeScript](../../docs/guides/write-a-crew-in-typescript.md) — the guide
  these files illustrate, starting with the two script shapes and why the choice matters.
- [Scripting DSL reference](../../docs/reference/scripting-dsl.md) — every builder and method,
  and which of the two shapes honours it.
- [Scripting DSL — architecture](../../docs/architecture/scripting.md) — where the DSL sits.
