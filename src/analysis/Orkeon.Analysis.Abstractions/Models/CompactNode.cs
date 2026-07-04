namespace Orkeon.Analysis.Abstractions.Models;

public sealed record CompactNode
{
    public required string Fqn { get; init; }
    public required UniversalNodeKind Kind { get; init; }
    public string? SummaryShort { get; init; }
    public string? Signature { get; init; }
}
