using System.Collections.Immutable;

namespace Orkeon.Rag.Abstractions.Options;

/// <summary>
/// Parameters passed to an <see cref="Interfaces.IChunkingStrategy"/>.
/// </summary>
public sealed record ChunkingOptions
{
    /// <summary>Maximum chunk size, in characters.</summary>
    public int MaxChunkSize { get; init; } = 1000;

    /// <summary>Overlap between consecutive chunks, in characters.</summary>
    public int Overlap { get; init; } = 200;

    /// <summary>Strategy-specific extension knobs (e.g. separators, sentence model).</summary>
    public ImmutableDictionary<string, string> Extensions { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}
