using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Abstractions.DTOs.Responses;

public sealed record SubGraph
{
    public required ImmutableArray<RaggableNode> Nodes { get; init; }
    public required ImmutableArray<RaggableEdge> Edges { get; init; }
    public bool Truncated { get; init; }
}
