using Orkeon.Domain.EventHub;

namespace Orkeon.Application.EventHub.Exceptions;

/// <summary>
/// Thrown by the ACL stage when no <see cref="CrewLink"/> authorizes a message (spec §10.2).
/// <para>
/// It is an exception rather than a silent drop on purpose: a refusal is a decision the
/// caller has to hear. The middleware pipeline deliberately does not catch it — unlike hook
/// dispatch, where swallowing is the correct behaviour because observing must not change an
/// outcome.
/// </para>
/// </summary>
#pragma warning disable S3925 // BinaryFormatter serialization is obsolete in .NET 10; ISerializable pattern not required
public sealed class EventAclException : Exception
#pragma warning restore S3925
{
    /// <summary>Creates the exception with a message naming what was refused and why.</summary>
    public EventAclException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner cause.</summary>
    public EventAclException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception with the default message.</summary>
    public EventAclException()
        : base("The message was refused by the EventHub access control list.")
    {
    }
}
