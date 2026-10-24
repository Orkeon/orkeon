#pragma warning disable CA1032, CA1064
using System.Runtime.Serialization;

namespace Orkeon.Analysis.Core.Editing;

[Serializable]
public sealed class StaleHashException : Exception
{
    private const string ExpectedKey = "Expected";
    private const string ActualKey = "Actual";

    public StaleHashException(string expected, string actual)
        : base($"Stale hash: expected {expected}, actual {actual}. Re-read the node before replacing it.")
    {
        Expected = expected;
        Actual = actual;
    }

    public string Expected { get; }
    public string Actual { get; }

    [Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0051")]
    private StaleHashException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        Expected = info.GetString(ExpectedKey) ?? string.Empty;
        Actual = info.GetString(ActualKey) ?? string.Empty;
    }

    /// <inheritdoc />
    [Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0051")]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(ExpectedKey, Expected);
        info.AddValue(ActualKey, Actual);
    }
}
