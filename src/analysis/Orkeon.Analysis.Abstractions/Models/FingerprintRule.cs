namespace Orkeon.Analysis.Abstractions.Models;

public sealed record FingerprintRule(
    string DecoratorName,
    string? Language = null,
    IReadOnlyList<string>? Tags = null,
    UniversalNodeKind? OverrideKind = null)
{
    public IReadOnlyList<string> TagsOrEmpty => Tags ?? [];
}
