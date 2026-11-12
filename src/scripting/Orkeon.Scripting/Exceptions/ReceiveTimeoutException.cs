using System.Globalization;

namespace Orkeon.Scripting.Exceptions;

/// <summary>
/// Thrown when <c>ctx.receive({ timeout })</c> expires before a message arrives.
/// </summary>
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
}
