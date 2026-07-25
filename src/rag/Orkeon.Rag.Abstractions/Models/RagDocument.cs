using System.Collections.Immutable;

namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// A loaded document ready for chunking: raw content plus provenance metadata.
/// </summary>
public sealed record RagDocument
{
    /// <summary>Unique document identifier (stable across re-ingestions of the same source).</summary>
    public required string Id { get; init; }

    /// <summary>Identity of the source this document was loaded from (see <see cref="SourceDescriptor"/>).</summary>
    public required string SourceId { get; init; }

    /// <summary>Full textual content of the document.</summary>
    public required string Content { get; init; }

    /// <summary>Optional human-readable title.</summary>
    public string? Title { get; init; }

    /// <summary>Optional original locator (file path, URL…).</summary>
    public string? Location { get; init; }

    /// <summary>Free-form provenance/context metadata carried through to chunks.</summary>
    public ImmutableDictionary<string, string> Metadata { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}
