using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions.DTOs.Responses;
using Orkeon.Domain.Attributes;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record ComplexityReportRequest
{
    [FieldSchema(Description = "Keep only the symbols under this FQN.", IsRequired = false)]
    public string? RootFqn { get; init; }

    [FieldSchema(Description = "How many symbols are listed, the highest first, at least 1 (default 20).", IsRequired = false, Default = 20)]
    public int TopN { get; init; } = 20;

    [FieldSchema(Description = "Metric ranked: Cyclomatic (default), NestingDepth, FanOut, LoC, Callers.", IsRequired = false, Default = "Cyclomatic")]
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
