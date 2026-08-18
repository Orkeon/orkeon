# RAG showcases

Runnable demos of the RAG subsystem (`src/rag/` — see the
[RAG pipeline architecture](../../docs/architecture/rag-pipeline.md)). Each
sub-example has its own README with prerequisites and run instructions; they are
C# projects, so `run-example.sh` falls back to `dotnet run --project …` for them.

| Example | What it shows |
|---------|---------------|
| [`basic-ingestion/`](basic-ingestion/) | The smallest end-to-end demo: ingest a tiny corpus, ask a question, get a cited answer |
| [`hybrid-retrieval/`](hybrid-retrieval/) | Same corpus, same questions, two pipelines: vector-only (`fast`) vs hybrid BM25 + RRF |
| [`custom-reranker/`](custom-reranker/) | Replacing the reranking stage with a host-provided `IReranker` via DI |
| [`crew-yaml/`](crew-yaml/) | A crew declared entirely in YAML: crew-level `rag:` block + agent `knowledge:` attachments |
| [`eval/`](eval/) | The versioned golden dataset (`golden.yaml`) behind `orkeon rag eval` and the CI gate, with the measured per-profile numbers |
