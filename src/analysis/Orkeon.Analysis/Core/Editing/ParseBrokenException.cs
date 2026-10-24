#pragma warning disable CA1032, CA1064
namespace Orkeon.Analysis.Core.Editing;

public sealed class ParseBrokenException : Exception
{
    public ParseBrokenException(IReadOnlyList<string> diagnostics)
        : base($"The replacement introduces parse errors: {string.Join("; ", diagnostics)}")
    {
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<string> Diagnostics { get; }
}
