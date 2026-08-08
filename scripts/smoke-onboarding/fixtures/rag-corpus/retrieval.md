# Retrieval

The Orkeon RAG pipeline ingests documents, chunks them, embeds the chunks
on-device with BGE-micro-v2, fuses a lexical BM25 ranking with the vector
ranking, and reranks the candidates with an embedded ms-marco-MiniLM
cross-encoder.

Every one of those stages ships inside the installed artifact, so retrieval
works fully offline: no API key, no network call, no model download.
