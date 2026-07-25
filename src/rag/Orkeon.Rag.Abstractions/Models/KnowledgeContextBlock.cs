using System.Collections.Immutable;

namespace Orkeon.Rag.Abstractions.Models;

/// <summary>
/// Retrieved-knowledge context ready for prompt injection (RAG-03/C4): a formatted
/// text block with numbered <c>[n]</c> excerpts plus the <see cref="Citation"/>s
/// resolving each marker to its source chunk. Produced by an
/// <see cref="Interfaces.IKnowledgeContextAugmenter"/> from the collections attached
/// to an agent.
/// </summary>
public sealed record KnowledgeContextBlock
{
    /// <summary>
    /// Formatted context block: a short grounding instruction followed by the
    /// numbered excerpts (<c>[1]</c>, <c>[2]</c>, …). Never empty — augmenters
    /// return <c>null</c> instead of an empty block when nothing was retrieved.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>Citations resolving each <c>[n]</c> marker of <see cref="Text"/>, in marker order.</summary>
    public ImmutableList<Citation> Citations { get; init; } = ImmutableList<Citation>.Empty;
}
