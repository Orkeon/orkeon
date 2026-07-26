/// <reference orkeon-script="1.0" />

// First-class `rag.*` namespace (RAG-03/C3): ingest a small corpus through the
// incremental ingestion pipeline, then ask a question and get citations back.
// The corpus lives next to this script (data/08-rag/), reachable through the
// implicit read-only /script mount; glob patterns are expanded via the VFS.
//
// Run it offline (no LLM, retrieval + citations only) from the repo root:
//
//   dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run examples/scripting/08-rag.ork.ts \
//     --settings examples/scripting/08-rag.appsettings.json \
//     --mount "$(mktemp -d)":/output:rw --allow-external-mounts
//
// The /output mount receives the per-collection ingestion manifest — that is
// what makes the SECOND ingest below a no-op (0 embeddings). Without it the
// run still works, just never incrementally. With an `Llm` section in the
// settings (e.g. examples/appsettings/appsettings.json + a local model
// server), rag.query also returns a generated grounded answer.

// 1. Full ingestion. `reindex: true` purges any stale manifest state so the
//    demo is deterministic even when /output points at a reused directory
//    (the document store is in-memory and empty at every process start).
const first = await rag.ingest({
    collection: "terrabrew-kb",
    sources: ["/script/data/08-rag/*.md"], // glob, expanded through the VFS
    chunkingStrategy: "recursive",
    reindex: true,
});

// 2. Same corpus again: the manifest written by step 1 short-circuits every
//    unchanged source — sourcesUnchanged = 3, chunksEmbedded = 0.
const second = await rag.ingest({
    collection: "terrabrew-kb",
    sources: ["/script/data/08-rag/*.md"],
});

// 3. Question through the query pipeline: scored [n] citations always; the
//    generated text needs an LLM (empty string in the offline run).
const answer = await rag.query("How often should I descale the machine?", {
    collection: "terrabrew-kb",
    topN: 2,
});

globalThis.result = {
    firstIngest: {
        chunksEmbedded: first.chunksEmbedded,
        sourcesAdded: first.sourcesAdded,
        sourcesReingested: first.sourcesReingested,
    },
    secondIngest: {
        chunksEmbedded: second.chunksEmbedded,   // 0 when /output is mounted
        sourcesUnchanged: second.sourcesUnchanged,
    },
    answer: answer.text || "(offline — no LLM configured; the citations below are the real value)",
    citations: answer.citations.map(c => ({
        marker: c.marker,
        source: c.sourceId,
        score: Math.round(c.score * 10000) / 10000,
    })),
};
