using Orkeon.Application.EventHub;

namespace Orkeon.Application.EventHub.Exceptions;

/// <summary>
/// Raised by the idempotency stage when a message reaches a mailbox that already consumed it
/// (HUB-04, spec §12).
/// <para>
/// The middleware contract can only pass a message on or stop it by throwing, so "drop this
/// duplicate" has to be said as an exception. The hub catches it at the delivery site and skips
/// the message — a duplicate is not an error the consumer should ever see, which is why this
/// type exists instead of reusing a general one.
/// </para>
/// </summary>
public sealed class DuplicateMessageException : Exception
{
    /// <summary>The message that had already been delivered, when known.</summary>
    public MessageId? MessageId { get; }

    /// <summary>Creates the exception for a message already consumed by its recipient.</summary>
    public DuplicateMessageException(MessageId messageId)
        : base($"Message '{messageId}' was already delivered to its recipient.") =>
        MessageId = messageId;

    /// <summary>Creates the exception with an explicit message.</summary>
    public DuplicateMessageException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with an explicit message and an inner cause.</summary>
    public DuplicateMessageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception with the default message.</summary>
    public DuplicateMessageException()
        : base("The message was already delivered to its recipient.")
    {
    }
}
