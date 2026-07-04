using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Abstractions.Interfaces;

public interface IReferenceResolver
{
    Task<ImmutableArray<RaggableEdge>> ResolveAsync(
        IReadOnlyList<UnresolvedRef> refs,
        IReadOnlyDictionary<string, RaggableNode> symbolsByFqn,
        CancellationToken ct);
}
