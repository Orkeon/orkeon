namespace Orkeon.Application.EventHub.Exceptions;

/// <summary>
/// Thrown when a <c>Post</c> or <c>Send</c> targets a <see cref="MailboxAddress"/> that
/// has not been registered with the hub.
/// </summary>
#pragma warning disable S3925
public sealed class MailboxNotFoundException : InvalidOperationException
#pragma warning restore S3925
{
    /// <summary>Gets the missing mailbox.</summary>
    public string MailboxAddress { get; }

    /// <summary>Creates a new <see cref="MailboxNotFoundException"/>.</summary>
    public MailboxNotFoundException(string mailboxAddress)
        : base($"No mailbox registered at '{mailboxAddress}'.")
    {
        MailboxAddress = mailboxAddress;
    }

    /// <summary>Creates a new <see cref="MailboxNotFoundException"/>.</summary>
    public MailboxNotFoundException()
    {
        MailboxAddress = string.Empty;
    }

    /// <summary>Creates a new <see cref="MailboxNotFoundException"/> with an inner exception.</summary>
    public MailboxNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
        MailboxAddress = string.Empty;
    }
}
