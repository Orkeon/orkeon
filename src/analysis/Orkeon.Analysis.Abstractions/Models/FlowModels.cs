namespace Orkeon.Analysis.Abstractions.Models;

public sealed record DataFlowLink(
    string VariableName,
    string SourceStatementId,
    string TargetStatementId);

public sealed record ControlFlowEdge(
    string FromId,
    string ToId,
    string? Condition);

public sealed record ControlFlowGraph(
    IReadOnlyList<ControlFlowEdge> Edges,
    string EntryStatementId,
    IReadOnlyList<string> ExitStatementIds);
