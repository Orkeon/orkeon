namespace Orkeon.Analysis.Abstractions.Models;

public record RaggableEdge(
    string Id,
    string FromId,
    string ToId,
    EdgeKind Kind,
    SourceLocation? CallSite = null,
    string? Label = null);
