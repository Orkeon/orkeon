namespace Orkeon.Analysis.Abstractions.DTOs.Responses;

public sealed record SourceSlice
{
    public required string VirtualFilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string Language { get; init; }
    public required string Source { get; init; }
    public required string Sha256 { get; init; }
    public required bool Stable { get; init; }
}
