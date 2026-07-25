// Orkeon Scripting DSL — RAG namespace (RAG-03/C3)
// First-class binding over the RAG subsystem pipelines. Requires the host to
// register the subsystem (AddOrkeonRag); calls fail with an actionable error otherwise.

declare global {
    /** Incremental ingestion counters returned by `rag.ingest`. */
    interface RagIngestReport {
        readonly collection: string;
        readonly documentsLoaded: number;
        readonly chunksCreated: number;
        readonly chunksEmbedded: number;
        readonly chunksSkipped: number;
        /** Sources ingested for the first time in this run. */
        readonly sourcesAdded: number;
        /** Sources skipped because unchanged since the last ingestion (0 embedding computed). */
        readonly sourcesUnchanged: number;
        /** Sources re-ingested (modified, or forcibly rebuilt via `reindex`). */
        readonly sourcesReingested: number;
        readonly durationMs: number;
        /** Non-fatal errors encountered during the run (empty when clean). */
        readonly errors: readonly string[];
    }

    /** Provenance of a cited passage in a `rag.query` answer. */
    interface RagCitation {
        /** Citation marker number as it appears in the answer text (`[n]`). */
        readonly marker: number;
        readonly chunkId: string;
        readonly sourceId: string;
        readonly documentId?: string;
        readonly snippet?: string;
        readonly score: number;
    }

    /** Grounded answer returned by `rag.query`. */
    interface RagAnswer {
        /** Generated answer text, with `[n]` citation markers. */
        readonly text: string;
        readonly citations: readonly RagCitation[];
    }

    namespace rag {
        /**
         * Ingest sources into a collection (incremental: unchanged sources are
         * skipped, 0 embedding computed). Glob patterns (`*`, `**`, `?`) are
         * expanded through the virtual file system.
         */
        function ingest(options: {
            collection: string;
            /** Virtual paths and/or glob patterns (e.g. "/workspace/docs/**\/*.md"). */
            sources: readonly string[] | string;
            /** Chunking strategy name (recursive | sentence | structural | semantic). Pipeline default when omitted. */
            chunkingStrategy?: string;
            /** Force a full reindex (required after an embedding model change). Default false. */
            reindex?: boolean;
        }): Promise<RagIngestReport>;

        /**
         * Ask a question against a collection; returns the grounded answer with
         * citations and scores.
         */
        function query(question: string, options: {
            collection: string;
            /**
             * Retrieval profile (fast | balanced | quality | corrective | adaptive).
             * Accepted but currently a NO-OP: retrieval profiles land with RAG-04.
             */
            profile?: string;
            /** Number of chunks kept for context assembly (default 5). */
            topN?: number;
        }): Promise<RagAnswer>;
    }
}

export { };
