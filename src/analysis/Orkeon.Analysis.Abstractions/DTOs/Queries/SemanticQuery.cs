namespace Orkeon.Analysis.Abstractions.DTOs.Queries;

public sealed record SemanticQuery
{
    public required string Text { get; init; }
    public int TopK { get; init; } = 10;
    public double MinScore { get; init; }
    public NodeQuery? PreFilter { get; init; }
}
