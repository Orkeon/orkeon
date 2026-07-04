namespace Orkeon.Analysis.Abstractions.Models;

public sealed record IndexedRoot(
    string VirtualRoot,
    DateTimeOffset IndexedAt,
    int NodeCount,
    int EdgeCount,
    string? LanguageSummary);
