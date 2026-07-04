using System.Runtime.Serialization;

namespace Orkeon.Scripting.Exceptions;

/// <summary>
/// Thrown when a script-supplied builder configuration is invalid: missing required
/// fields, illegal value combinations, or contracts violated by the script author.
/// </summary>
[Serializable]
public sealed class InvalidScriptException : Exception
{
    /// <inheritdoc />
    public InvalidScriptException() { }

    /// <inheritdoc />
    public InvalidScriptException(string message) : base(message) { }

    /// <inheritdoc />
    public InvalidScriptException(string message, Exception inner) : base(message, inner) { }

    /// <summary>Deserialization constructor.</summary>
#pragma warning disable SYSLIB0051 // Required by S3925 ISerializable pattern
    private InvalidScriptException(SerializationInfo info, StreamingContext context)
        : base(info, context) { }
#pragma warning restore SYSLIB0051
}
