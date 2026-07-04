namespace Orkeon.Application.EventHub.Exceptions;

/// <summary>
/// Thrown when a string cannot be parsed into a valid <see cref="MailboxAddress"/>.
/// </summary>
#pragma warning disable S3925 // BinaryFormatter serialization is obsolete in .NET 10; ISerializable pattern not required
public sealed class InvalidMailboxAddressException : ArgumentException
#pragma warning restore S3925
{
    /// <summary>Creates a new <see cref="InvalidMailboxAddressException"/>.</summary>
    public InvalidMailboxAddressException() { }

    /// <summary>Creates a new <see cref="InvalidMailboxAddressException"/>.</summary>
    public InvalidMailboxAddressException(string message) : base(message) { }

    /// <summary>Creates a new <see cref="InvalidMailboxAddressException"/> with an inner exception.</summary>
    public InvalidMailboxAddressException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Creates a new <see cref="InvalidMailboxAddressException"/> with the offending input.</summary>
    public InvalidMailboxAddressException(string message, string paramName) : base(message, paramName) { }
}
