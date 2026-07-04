namespace Orkeon.Analysis.Abstractions.DTOs.Responses;

public sealed record SearchHit
{
    public required string Fqn { get; init; }
    public required double Score { get; init; }
    public string? SummaryShort { get; init; }
    public string? Signature { get; init; }
}
