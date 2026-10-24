#pragma warning disable CA1032, CA1064
using System.Runtime.Serialization;

namespace Orkeon.Analysis.Core.Editing;

[Serializable]
public sealed class EditConflictException : Exception
{
    public EditConflictException(string message)
        : base(message)
    {
    }

    [Obsolete("Formatter-based serialization is obsolete.", DiagnosticId = "SYSLIB0051")]
    private EditConflictException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
    }
}
