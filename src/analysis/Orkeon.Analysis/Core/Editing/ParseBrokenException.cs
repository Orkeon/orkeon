#pragma warning disable CA1032, CA1064
using System.Runtime.Serialization;

namespace Orkeon.Analysis.Core.Editing;

[Serializable]
public sealed class ParseBrokenException : Exception
{
    private const string DiagnosticsKey = "Diagnostics";

    public ParseBrokenException(IReadOnlyList<string> diagnostics)
        : base($"The replacement introduces parse errors: {string.Join("; ", diagnostics)}")
    {
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<string> Diagnostics { get; }

    [Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0051")]
    private ParseBrokenException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        Diagnostics = (string[]?)info.GetValue(DiagnosticsKey, typeof(string[])) ?? [];
    }

    /// <inheritdoc />
    [Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0051")]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(DiagnosticsKey, Diagnostics.ToArray());
    }
}
