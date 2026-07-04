namespace Orkeon.Application.EventHub.Exceptions;

/// <summary>
/// Thrown when an agent attempts to publish on a reserved topic (prefix <c>_system.</c>).
/// </summary>
#pragma warning disable S3925
public sealed class ReservedTopicException : InvalidOperationException
#pragma warning restore S3925
{
    /// <summary>Gets the offending topic.</summary>
    public string Topic { get; }

    /// <summary>Creates a new <see cref="ReservedTopicException"/>.</summary>
    public ReservedTopicException(string topic)
        : base($"Topic '{topic}' is reserved for system messages and cannot be published by an agent.")
    {
        Topic = topic;
    }

    /// <summary>Creates a new <see cref="ReservedTopicException"/>.</summary>
    public ReservedTopicException()
    {
        Topic = string.Empty;
    }

    /// <summary>Creates a new <see cref="ReservedTopicException"/> with an inner exception.</summary>
    public ReservedTopicException(string message, Exception innerException)
        : base(message, innerException)
    {
        Topic = string.Empty;
    }
}
