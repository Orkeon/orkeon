namespace Orkeon.Analysis.Abstractions.Models;

public sealed record UnresolvedRef(
    string SourceFqn,
    string RawTargetName,
    ReferenceKind Kind,
    SourceLocation CallSite);
