namespace Orkeon.Analysis.Abstractions.DTOs.Responses;

public sealed record NotFoundResponse
{
    public required string Fqn { get; init; }
    public required string Reason { get; init; }
}
