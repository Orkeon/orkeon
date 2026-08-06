namespace Orkeon.Analysis.Abstractions.Models;

/// <summary>Coarse phase of a full index build, in pipeline order.</summary>
public enum IndexBuildPhase
{
    /// <summary>Walking the file system for indexable files.</summary>
    Discovery,

    /// <summary>Parsing sources and extracting nodes, file by file.</summary>
    Parse,

    /// <summary>Resolving cross-file dependency edges, per language.</summary>
    Resolve,

    /// <summary>Optional LLM summarization of nodes.</summary>
    Enrich,

    /// <summary>Producing embedding vectors for eligible nodes.</summary>
    Embed,

    /// <summary>Persisting embedded documents to the vector store.</summary>
    Persist,
}

/// <summary>
/// One progress notification from a full index build (<c>RaggableTreeBuilder</c>).
/// <see cref="Current"/>/<see cref="Total"/> are meaningful only when the phase iterates a
/// known set (Parse over files); other phases report 0/0 and read as indeterminate.
/// Consumed through the standard <see cref="IProgress{T}"/> — a host that registers no
/// sink pays nothing.
/// </summary>
public sealed record IndexBuildProgress(IndexBuildPhase Phase, int Current, int Total);
