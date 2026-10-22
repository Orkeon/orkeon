#pragma warning disable CA1032, CA1064
namespace Orkeon.Analysis.Core.Editing;

internal sealed class ParseBrokenException(IReadOnlyList<string> diagnostics)
    : Exception($"The replacement introduces parse errors: {string.Join("; ", diagnostics)}")
{
    public IReadOnlyList<string> Diagnostics { get; } = diagnostics;
}
