using System.Collections.Immutable;

namespace Orkeon.Analysis.Abstractions.Models;

public sealed record VectorMetadata
{
    public required string Kind { get; init; }
    public required string Language { get; init; }
    public required string VirtualFilePath { get; init; }
    public required string Fqn { get; init; }
    public ImmutableArray<string> Tags { get; init; } = [];
    public ImmutableArray<string> Decorators { get; init; } = [];
    public bool HasParent { get; init; }
    public int ChildCount { get; init; }
    public int InDegree { get; init; }
    public int OutDegree { get; init; }
}
