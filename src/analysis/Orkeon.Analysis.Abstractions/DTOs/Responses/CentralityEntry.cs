namespace Orkeon.Analysis.Abstractions.DTOs.Responses;

public sealed record CentralityEntry(string Fqn, double Value, CentralityMetric Metric);
