namespace Orkeon.Analysis.Abstractions.Models;

public record RaggableTree(
    IReadOnlyList<RaggableNode> Nodes,
    IReadOnlyList<RaggableEdge> Edges,
    IReadOnlyDictionary<string, RaggableNode> Index);
