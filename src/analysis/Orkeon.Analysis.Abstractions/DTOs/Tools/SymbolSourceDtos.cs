namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record SymbolSourceRequest
{
    public string Fqn { get; init; } = "";
    public SourceMode Mode { get; init; } = SourceMode.SignatureAndBody;
    public int MaxLines { get; init; } = 40;
    public string? StatementId { get; init; }
}

public sealed record SymbolSourceResponse
{
    public required string VirtualFilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string Language { get; init; }
    public required string Source { get; init; }
    public required string Sha256 { get; init; }
    public required bool Stable { get; init; }
    public bool Truncated { get; init; }

    public required string Fqn { get; init; }
    public required string ShortSha { get; init; }
    public required string MarkdownReadyBlock { get; init; }
}
