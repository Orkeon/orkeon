namespace Orkeon.Application.EventHub.Exceptions;

/// <summary>
/// Thrown when <c>ReplyAsync</c> is invoked with a <see cref="CorrelationId"/> that has no
/// pending request waiting for a reply (no <c>Send</c> in flight, or already answered).
/// </summary>
#pragma warning disable S3925
public sealed class UnknownCorrelationException : InvalidOperationException
#pragma warning restore S3925
{
    /// <summary>Gets the unknown correlation identifier.</summary>
    public string CorrelationId { get; }

    /// <summary>Creates a new <see cref="UnknownCorrelationException"/>.</summary>
    public UnknownCorrelationException(string correlationId)
        : base($"No pending request found for correlation id '{correlationId}'. Either it was never sent or already answered.")
    {
        CorrelationId = correlationId;
    }

    /// <summary>Creates a new <see cref="UnknownCorrelationException"/>.</summary>
    public UnknownCorrelationException()
    {
        CorrelationId = string.Empty;
    }

    /// <summary>Creates a new <see cref="UnknownCorrelationException"/> with an inner exception.</summary>
    public UnknownCorrelationException(string message, Exception innerException)
        : base(message, innerException)
    {
        CorrelationId = string.Empty;
    }
}
