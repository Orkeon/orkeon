using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions.DTOs.Responses;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record ComplexityReportRequest
{
    public string? RootFqn { get; init; }
    public int TopN { get; init; } = 20;
    public ComplexityMetric Metric { get; init; } = ComplexityMetric.Cyclomatic;
}

public sealed record ComplexityReportResponse
{
    public required ComplexityMetric Metric { get; init; }
    public required ImmutableArray<ComplexityEntry> Top { get; init; }
    public double? Median { get; init; }
    public double? P95 { get; init; }
    public int EvaluatedCount { get; init; }
}
