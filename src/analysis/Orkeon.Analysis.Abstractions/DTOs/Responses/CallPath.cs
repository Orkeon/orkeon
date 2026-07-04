using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Abstractions.DTOs.Responses;

public sealed record CallPath
{
    public required ImmutableArray<string> Fqns { get; init; }
    public required int Length { get; init; }
    public ImmutableArray<SourceLocation> CallSites { get; init; } = [];
}
