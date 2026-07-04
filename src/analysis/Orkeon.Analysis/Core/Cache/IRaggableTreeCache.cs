using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core.Cache;

public interface IRaggableTreeCache
{
    Task<RaggableTreeCacheLoadResult> LoadAsync(string rootPath, CancellationToken ct);
    Task SaveAsync(RaggableTree tree, string indexId, string rootPath, CancellationToken ct);
    Task<bool> IsValidAsync(string rootPath, CancellationToken ct);
    Task InvalidateAsync(string rootPath, CancellationToken ct);
}

public sealed record RaggableTreeCacheLoadResult(RaggableTree? Tree, string? IndexId)
{
    public static RaggableTreeCacheLoadResult Miss { get; } = new(null, null);
    public bool Hit => Tree is not null;
}
