using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core.Serialization;

/// <summary>
/// Serializes RaggableTree graphs for persistence and transport.
/// </summary>
public interface IRaggableTreeSerializer
{
    Task SerializeAsync(RaggableTree tree, string indexId, Stream output, CancellationToken ct);
    Task<RaggableTreeSnapshot?> DeserializeAsync(Stream input, CancellationToken ct);
}

public sealed record RaggableTreeSnapshot(RaggableTree Tree, string IndexId, DateTimeOffset CreatedAt);
