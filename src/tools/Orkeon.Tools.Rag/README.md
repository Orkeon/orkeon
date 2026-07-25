# Orkeon.Tools.Rag

Agent tools for the Orkeon RAG subsystem (`src/rag/`), in the `Tools.*` family
(naming per [ADR-004](../../../docs/adr/ADR-004-jumeaux-de-nommage-scripting.md) —
`Orkeon.Tools.Rag`, **not** `Orkeon.Rag.Tools`).

## Tools

| Tool | Class | Description |
|------|-------|-------------|
| `rag_search` | `RagSearchTool` | Grounded retrieval over the RAG subsystem's `IRagPipeline` (question / `top_k` / `collection`); `collection = "raggable-tree"` routes to the semantic code index (`IRaggableStore`). Output: answer text + `Sources:` block with scores. |

## Registration

```csharp
services.AddOrkeonRag(configuration);   // Orkeon.Rag.DependencyInjection — the subsystem
services.AddOrkeonRagTools();           // Orkeon.Tools.Rag.DependencyInjection — the agent tools
```

Contracts live in `src/rag/Orkeon.Rag.Abstractions/`; implementations and
named-component factories live in `src/rag/Orkeon.Rag/`. See
[ADR-006](../../../docs/adr/ADR-006-rag-subsystem.md).
