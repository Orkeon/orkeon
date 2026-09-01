# Orkeon API reference

Generated reference for the public surface of every published Orkeon library —
the same surface that is frozen in each project's `PublicAPI.Shipped.txt`
(see [CONTRIBUTING — Versioning and API stability](../CONTRIBUTING.md#versioning-and-api-stability)).

Browse by namespace:

- **`Orkeon.Domain`** — entities, value objects, domain events, core interfaces.
- **`Orkeon.Application`** — use cases, ports, DTOs, orchestration services.
- **`Orkeon.Infrastructure`** — LLM providers, memory stores, orchestration strategies, VFS.
- **`Orkeon.Constants.*`** — the shared-constants satellites (LLM endpoints and wire fields, virtual mount roots, configuration keys, run-event kinds, run option names).
- **`Orkeon.Tools.*`** — the agent tool suites and their abstractions.
- **`Orkeon.Rag.*`** — the RAG subsystem (contracts, pipeline, ONNX reranker).
- **`Orkeon.Analysis.*`** — RaggableTree semantic codebase analysis.
- **`Orkeon.Scripting`** — the TypeScript DSL runtime.
- **`Orkeon.Cli.*`** — interactive CLI runner building blocks.
- **`Orkeon.Hosting`** — runner & host bootstrap.
- **`Orkeon.Plugins`** — the plugin system.

Types marked `[Experimental]` (diagnostic IDs `ORKEXP001–004`) are outside the
API stability commitment — see
[Experimental APIs](../docs/reference/experimental-apis.md).
