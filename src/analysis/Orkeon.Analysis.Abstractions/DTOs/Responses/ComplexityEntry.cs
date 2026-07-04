namespace Orkeon.Analysis.Abstractions.DTOs.Responses;

public sealed record ComplexityEntry(string Fqn, double Value, ComplexityMetric Metric);
