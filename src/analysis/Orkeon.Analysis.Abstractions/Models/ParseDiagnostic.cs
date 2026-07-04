namespace Orkeon.Analysis.Abstractions.Models;

public sealed record ParseDiagnostic
{
    public required DiagnosticSeverity Severity { get; init; }
    public required int StartLine { get; init; }
    public required int StartColumn { get; init; }
    public int? EndLine { get; init; }
    public int? EndColumn { get; init; }
    public required string Message { get; init; }
    public string? TreeSitterNodeType { get; init; }
}
