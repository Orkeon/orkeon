# Orkeon.Tools.Rag

Agent tools for the Orkeon RAG subsystem (`src/rag/`), in the `Tools.*` family
(naming per [ADR-004](../../../docs/adr/ADR-004-jumeaux-de-nommage-scripting.md) —
`Orkeon.Tools.Rag`, **not** `Orkeon.Rag.Tools`).

**Status: compilable skeleton (RAG-02 / C1).** The `rag_*` agent tools
(`rag_search`, `rag_ingest`, `rag_eval`) migrate here in later batches of
[RAG-02](../../../docs/adr/ADR-006-rag-subsystem.md), replacing the legacy
`RagTool` currently living in `Orkeon.Infrastructure.Knowledge`. Until that
migration lands, this project intentionally contains no tool classes.

Contracts live in `src/rag/Orkeon.Rag.Abstractions/`; implementations and
named-component factories live in `src/rag/Orkeon.Rag/`.
