using System.Collections.Immutable;

namespace Orkeon.Analysis.Core.Structural;

internal sealed record StructuralMatch
{
    public required string Path { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
    public required int StartByte { get; init; }
    public required int EndByte { get; init; }
    public required string Text { get; init; }
    public string? EnclosingSelector { get; init; }
    public ImmutableDictionary<string, string> Captures { get; init; } = ImmutableDictionary<string, string>.Empty;
}
