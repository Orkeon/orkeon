#pragma warning disable CA1032, CA1064
namespace Orkeon.Analysis.Core.Editing;

public sealed class StaleHashException : Exception
{
    public StaleHashException(string expected, string actual)
        : base($"Stale hash: expected {expected}, actual {actual}. Re-read the node before replacing it.")
    {
        Expected = expected;
        Actual = actual;
    }

    public string Expected { get; }
    public string Actual { get; }
}
