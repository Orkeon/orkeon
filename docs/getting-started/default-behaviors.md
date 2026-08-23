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
| `IAgentPlanner` | `AgentPlannerService` | Emits the **same fixed 4-step plan** for every task (confidence 0.8). Refinement and validation are real; plan *creation* ignores the task content. | `services.AddSingleton<IAgentPlanner, YourPlanner>();` — an LLM-backed planner is a study item for V1.x. |
| `ITaskDelegator` | `NullTaskDelegator` | **Denies every delegation request**; `FindBestAgentForTaskAsync` returns the first available agent. Hierarchical/Autonomous delegation stays inert until replaced. | `services.AddSingleton<ITaskDelegator, YourDelegator>();` |
| `IKnowledgeStore` | `InMemoryKnowledgeStore` | Working in-memory store (store/retrieve/delete are real, nothing persists) — but `SearchAsync` **ignores the query** and returns the first stored entries. | Enable the RAG subsystem (`AddOrkeonRag(configuration)`) and use its ingestion/retrieval, or register your own store. |
| `ILlmCache` | `NullLlmCache` | A cache that always misses — changes cost, not correctness. | `services.AddSingleton<ILlmCache, YourCache>();` |
| `IYamlDiffService` | `NullYamlDiffService` | Diffing is an optional observability nicety. | Register your own implementation. |
| `ITemplateInstantiator` | `NullTemplateInstantiator` | Also **throws** `NotSupportedException` when actually used — louder than any log. | Register your own implementation. |
| `IToolRegistry` | `InMemoryToolRegistry` | Empty registry; the runner host replaces it with the DI-backed `ServiceProviderToolRegistry`. Crews that reference tools fail loudly under `StrictTools`. | `services.AddSingleton<IToolRegistry, ServiceProviderToolRegistry>();` |
| `IMemorySystem` | `InMemoryMemorySystem` | Real in-memory implementation — correct, just not persistent. | Configure a persistent provider (`Memory:Provider`). |
| `IAgentSelectionService` (FirstFit) | `SimpleAgentSelectionService` | Picks the **first available agent** — the explicit safe fallback. | Set `OrkeonApplicationOptions.AgentSelectionStrategy` to `Skill` (lexical) or `Embedding` (semantic — requires a real embedding provider), via `AddOrkeonApplication(o => …)` or `services.Configure<OrkeonApplicationOptions>(…)`. |
| `IEmbeddingProvider` | resolution chain | Local BGE (when `AddOrkeonLocalEmbeddings()` is registered) → remote provider from `Orkeon:Embeddings` → **fail-fast at first use** with an actionable exception. Never a silent hash fallback. | Register `AddOrkeonLocalEmbeddings()` or configure `Orkeon:Embeddings`. |

> **`IAgentExecutionService`** is *not* on this list on purpose: `AddOrkeonApplication()`
> registers the real `AgentExecutionService` unconditionally (`AddScoped`). The
> `NullAgentExecutionService` stub only wins in a container wired with
> `AddOrkeonInfrastructure()` **alone** (its registration is `TryAddScoped`). To
> substitute your own in a standard bootstrap, register it **after**
> `AddOrkeonApplication()`.

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
