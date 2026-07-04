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

See [`project/features/scripting-dsl/INDEX.md`](../../project/features/scripting-dsl/INDEX.md)
for the full DSL reference.
