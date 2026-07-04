namespace Orkeon.Application.EventHub.Exceptions;

/// <summary>
/// Thrown when a request issued via <c>IEventHub.SendAsync</c> is not answered within the configured timeout.
/// Distinct from <see cref="System.TimeoutException"/> so callers can disambiguate from generic timeouts.
/// </summary>
#pragma warning disable S3925
public sealed class SendTimeoutException : InvalidOperationException
#pragma warning restore S3925
{
    /// <summary>Gets the timeout that was exceeded.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>Gets the correlation identifier of the pending request.</summary>
    public string CorrelationId { get; }

    /// <summary>Creates a new <see cref="SendTimeoutException"/>.</summary>
    public SendTimeoutException(string correlationId, TimeSpan timeout)
        : base($"Send request {correlationId} did not receive a reply within {timeout}.")
    {
        CorrelationId = correlationId;
        Timeout = timeout;
    }

    /// <summary>Creates a new <see cref="SendTimeoutException"/>.</summary>
    public SendTimeoutException()
    {
        CorrelationId = string.Empty;
        Timeout = TimeSpan.Zero;
    }

    /// <summary>Creates a new <see cref="SendTimeoutException"/>.</summary>
    public SendTimeoutException(string message)
        : base(message)
    {
        CorrelationId = string.Empty;
        Timeout = TimeSpan.Zero;
    }

    /// <summary>Creates a new <see cref="SendTimeoutException"/> with an inner exception.</summary>
    public SendTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
        CorrelationId = string.Empty;
        Timeout = TimeSpan.Zero;
    }
}
