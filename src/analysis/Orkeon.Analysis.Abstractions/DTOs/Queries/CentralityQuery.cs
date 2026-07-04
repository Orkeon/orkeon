namespace Orkeon.Analysis.Abstractions.DTOs.Queries;

public sealed record CentralityQuery
{
    public NodeLevel Scope { get; init; } = NodeLevel.L3_Symbol;
    public CentralityMetric Metric { get; init; } = CentralityMetric.InDegreeCalls;
    public int TopN { get; init; } = 20;
}
