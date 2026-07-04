namespace Orkeon.Analysis.Abstractions.DTOs.Queries;

public sealed record ComplexityQuery
{
    public string? RootFqn { get; init; }
    public ComplexityMetric Metric { get; init; } = ComplexityMetric.Cyclomatic;
    public int TopN { get; init; } = 20;
}
