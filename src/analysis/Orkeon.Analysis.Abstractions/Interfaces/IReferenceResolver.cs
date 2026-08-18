using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Abstractions.Interfaces;

/// <summary>
/// Resolves cross-file symbol references into graph edges (resolve phase).
/// </summary>
public interface IReferenceResolver
{
    Task<ImmutableArray<RaggableEdge>> ResolveAsync(
        IReadOnlyList<UnresolvedRef> refs,
        IReadOnlyDictionary<string, RaggableNode> symbolsByFqn,
        CancellationToken ct);
}
