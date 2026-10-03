> 🇫🇷 [Version française](../fr/getting-started/default-behaviors.md)

# Default behaviors (and how to replace them)

> **See also**: [Bootstrap and execution](./bootstrap.md) · [Opt-in subsystems](../reference/opt-in-subsystems.md) · [Limits and constraints](../reference/limitations.md) · [Back to the index](../INDEX.md)

Orkeon's DI defaults follow one principle: **no implicit magic**. Nothing calls an
LLM you did not configure, nothing persists where you did not point it, and every
deliberately-minimal default **announces itself with a one-time Warning** that names
the remediation. This page is the inventory of those defaults — what each one does,
how it tells you it is active, and the exact gesture to replace it.

Most registrations below use `TryAdd`: registering your own implementation **before**
`AddOrkeonApplication()` / `AddOrkeonInfrastructure()` wins, with no other change. The
exceptions are called out in their row — a service registered unconditionally
(`AddScoped`/`AddSingleton`) is replaced by registering yours **after** the Orkeon call.

## The defaults that warn on first use

| Service | Default | What it actually does | Replace it with |
|---|---|---|---|
| `ITaskDelegator` | `NullTaskDelegator` | **Denies every delegation request**; `FindBestAgentForTaskAsync` returns the first available agent. Registered, but **consulted by no orchestration strategy today**: replacing it changes nothing in a run. Delegation that does happen goes elsewhere — the Autonomous mode hands a failed task to a peer over `IAgentChannel`, and agents delegate through the `delegate_work_to_coworker` / `ask_question_to_coworker` tools; see [Autonomous orchestration](../orchestration/autonomous.md). | Nothing to replace for a run; `services.AddSingleton<ITaskDelegator, YourDelegator>();` only serves your own code that resolves it. |
| `IKnowledgeStore` | `InMemoryKnowledgeStore` | Working in-memory store (store/retrieve/delete are real, nothing persists) — but `SearchAsync` **ignores the query** and returns the first stored entries. | Enable the RAG subsystem (`AddOrkeonRag(configuration)`) and use its ingestion/retrieval, or register your own store. |
| `ILlmCache` | `NullLlmCache` | A cache that always misses — changes cost, not correctness. | `services.AddSingleton<ILlmCache, YourCache>();` |
| `IMemorySystem` | `InMemoryMemorySystem` | Real in-memory implementation — correct, just not persistent. | Configure a persistent provider (`Memory:Provider`). |
| `IAgentSelectionService` (FirstFit) | `SimpleAgentSelectionService` | Picks the **first available agent** — the explicit safe fallback. | Set `OrkeonApplicationOptions.AgentSelectionStrategy` to `Skill` (lexical) or `Embedding` (semantic — requires a real embedding provider), via `AddOrkeonApplication(o => …)` or `services.Configure<OrkeonApplicationOptions>(…)`. |
| `IEmbeddingProvider` | resolution chain | Local BGE (when `AddOrkeonLocalEmbeddings()` is registered) → remote provider from `Orkeon:Embeddings` → **fail-fast at first use** with an actionable exception. Never a silent hash fallback. A crew with `memory: true` uses it at kickoff, so without one that crew is refused before its first task. | Register `AddOrkeonLocalEmbeddings()` or configure `Orkeon:Embeddings`. |

> **`IAgentExecutionService`** is *not* on this list on purpose: `AddOrkeonApplication()`
> registers the real `AgentExecutionService` unconditionally (`AddScoped`). The
> `NullAgentExecutionService` stub only wins in a container wired with
> `AddOrkeonInfrastructure()` **alone** (its registration is `TryAddScoped`). To
> substitute your own in a standard bootstrap, register it **after**
> `AddOrkeonApplication()`.

> **Two services have no working default at all.** No `IFileSystemService` is registered
> by either call: `AddOrkeonFileSystem(configuration)`, with at least one mount under
> `Orkeon:FileSystem:Mounts`, is required before a crew is loaded — the YAML loader, the
> file tools and the deliverables all read and write through it. And no model is
> registered: a container without one fails at its first LLM resolution, naming the missing
> service — register yours with `AddOrkeonLlmProvider(...)` (see [Bootstrap](./bootstrap.md)).
> The keyless OpenAI provider that stood in for it is gone (GAP-29). The `orkeon` CLI,
> `orkeon-host` and `orkeon-repl` behave differently: with no `Llm` section they run on the
> echo provider and say so. A crew with `planning: true` then skips its plan, with a warning:
> the echo provider replays its prompt, so there is nothing to plan with (GAP-31).

## The genuinely silent defaults

These are harmless by construction and stay at Debug or emit nothing:

| Service | Default | Why silence is fine |
|---|---|---|
| Step/task callbacks | `Null*Callback` | No-op observability hooks; Debug by design. |
| `IMemoryScope` | `NullMemoryScope` | Explicit "no memory in this context" marker — asked for, not fallen into. |
| `IMemoryProvider` | config-driven factory | Falls back to the in-memory provider when `Memory:Provider` is unset; an **unrecognized** type does warn. |

Fully-functional `TryAdd` defaults (e.g. `IPathValidator → PathValidator`) are not
listed here on purpose: they are the real implementation, not a stand-in — replace
them the same way (register yours before the Orkeon call), but nothing announces
them because nothing is missing.

## Why it works this way

A framework that silently swaps in an LLM call, a network dependency, or a
persistent store behind a default would be making decisions that belong to you.
The trade-off is that a freshly-bootstrapped host does less than a fully
configured one — and says so in the log, once per service, with the fix in the
message. If you see one of these warnings in production, it is never noise: it
means a capability you probably expect is running on its minimal stand-in.
