using System.Globalization;
using System.Runtime.Serialization;

namespace Orkeon.Scripting.Exceptions;

/// <summary>
/// Thrown when <c>ctx.receive({ timeout })</c> expires before a message arrives.
/// </summary>
[Serializable]
public sealed class ReceiveTimeoutException : Exception
{
    /// <summary>Timeout that elapsed.</summary>
    public TimeSpan Timeout { get; }

    /// <inheritdoc />
    public ReceiveTimeoutException(TimeSpan timeout)
        : base(string.Format(CultureInfo.InvariantCulture,
            "ctx.receive timed out after {0:N1}s without receiving a message.", timeout.TotalSeconds))
    {
        Timeout = timeout;
    }

    /// <inheritdoc />
    public ReceiveTimeoutException() { }

    /// <inheritdoc />
    public ReceiveTimeoutException(string message) : base(message) { }

    /// <inheritdoc />
    public ReceiveTimeoutException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Deserialization constructor.</summary>
#pragma warning disable SYSLIB0051 // Required by S3925 ISerializable pattern
    private ReceiveTimeoutException(SerializationInfo info, StreamingContext context)
        : base(info, context) { }
#pragma warning restore SYSLIB0051
}
