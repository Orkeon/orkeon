# Orkeon Scripting DSL — examples

Each file is a self-contained `.ork.ts` (TypeScript-syntax) script runnable through
the `orkeon` CLI:

```bash
dotnet run --project src/Orkeon.Scripting.Cli -- run examples/scripting/01-hello-world.ork.ts
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

`08-rag.ork.ts` ingests a corpus shipped in `data/08-rag/` and needs two extra
flags for the full offline experience — a writable `/output` mount (persists
the ingestion manifest, proving the second ingest embeds nothing) and an
LLM-less settings file (no localhost retries):

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run examples/scripting/08-rag.ork.ts \
  --settings examples/scripting/08-rag.appsettings.json \
  --mount "$(mktemp -d)":/output:rw --allow-external-mounts
```

See [`project/features/scripting-dsl/INDEX.md`](../../project/features/scripting-dsl/INDEX.md)
for the full DSL reference.
