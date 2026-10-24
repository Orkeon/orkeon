#pragma warning disable CA1032, CA1064
namespace Orkeon.Analysis.Core.Editing;

internal sealed class StaleHashException(string expected, string actual)
    : Exception($"Stale hash: expected {expected}, actual {actual}. Re-read the node before replacing it.")
{
    public string Expected { get; } = expected;
    public string Actual { get; } = actual;
}
