using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core.Serialization;

public interface IRaggableTreeSerializer
{
    Task SerializeAsync(RaggableTree tree, string indexId, Stream output, CancellationToken ct);
    Task<RaggableTreeSnapshot?> DeserializeAsync(Stream input, CancellationToken ct);
}

public sealed record RaggableTreeSnapshot(RaggableTree Tree, string IndexId, DateTimeOffset CreatedAt);
