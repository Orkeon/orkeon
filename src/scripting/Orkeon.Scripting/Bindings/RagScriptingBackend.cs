using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Interfaces;

namespace Orkeon.Scripting.Bindings;

/// <summary>
/// Everything the first-class <c>rag.*</c> scripting namespace needs, bundled
/// all-or-nothing: the two RAG pipelines plus the virtual file system used to
/// expand glob patterns in <c>rag.ingest</c> sources. A host that wired
/// <c>AddOrkeonRag</c> builds one; without it the namespace is still registered
/// but fails loudly on use. Bundling keeps <see cref="IFileSystemService"/> a
/// required, non-nullable dependency (VFS rule ORKVFS007) while the backend as
/// a whole stays optional.
/// </summary>
public sealed record RagScriptingBackend
{
    /// <summary>Ingestion pipeline backing <c>rag.ingest</c>.</summary>
    public required IIngestionPipeline IngestionPipeline { get; init; }

    /// <summary>Query pipeline backing <c>rag.query</c>.</summary>
    public required IRagPipeline RagPipeline { get; init; }

    /// <summary>
    /// Resolves a per-call retrieval profile (<c>fast</c> | <c>balanced</c> |
    /// <c>quality</c> | <c>corrective</c> | <c>adaptive</c>). Null when the host
    /// registered no resolver, in which case <c>rag.query</c> refuses a profile
    /// request loudly instead of silently ignoring it.
    /// </summary>
    public IRagProfileResolver? ProfileResolver { get; init; }

    /// <summary>Virtual file system used to expand glob patterns in ingest sources.</summary>
    public required IFileSystemService FileSystem { get; init; }
}
