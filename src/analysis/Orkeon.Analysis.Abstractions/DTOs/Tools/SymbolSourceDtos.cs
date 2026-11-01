using Orkeon.Domain.Attributes;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record SymbolSourceRequest
{
    [FieldSchema(Description = "FQN of the symbol to cite.", IsRequired = true)]
    public string Fqn { get; init; } = "";

    [FieldSchema(Description = "Span cited: SignatureOnly, SignatureAndDoc, SignatureAndBody (default), FullSpan, StatementSpan (with statement_id).", IsRequired = false, Default = "SignatureAndBody")]
    public SourceMode Mode { get; init; } = SourceMode.SignatureAndBody;

    [FieldSchema(Description = "Most lines returned (default 40); truncated says when the span is longer.", IsRequired = false, Default = 40)]
    public int MaxLines { get; init; } = 40;

    [FieldSchema(Description = "Id of one statement (from statement_query) to cite under StatementSpan.", IsRequired = false)]
    public string? StatementId { get; init; }
}

public sealed record SymbolSourceResponse
{
    public required string VirtualFilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string Language { get; init; }

    /// <summary>True when the source was cut at the tool's bound.</summary>
    public bool Truncated { get; init; }

    public required string Source { get; init; }
    public required string Sha256 { get; init; }
    public required bool Stable { get; init; }

    public required string Fqn { get; init; }
    public required string ShortSha { get; init; }
    public required string MarkdownReadyBlock { get; init; }
}
