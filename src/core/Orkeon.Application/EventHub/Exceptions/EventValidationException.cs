namespace Orkeon.Application.EventHub.Exceptions;

/// <summary>
/// Raised by the validation stage when a message names a schema the registry does not know
/// (HUB-04, spec §12).
/// <para>
/// Thrown on the **publish** side, so the refusal reaches the author of the message rather than
/// a subscriber who can do nothing about it — the same reasoning that puts the ACL on the
/// sender side.
/// </para>
/// </summary>
#pragma warning disable S3925 // BinaryFormatter serialization is obsolete in .NET 10; ISerializable pattern not required
public sealed class EventValidationException : Exception
#pragma warning restore S3925
{
    /// <summary>The schema identifier that could not be resolved, when known.</summary>
    public string? SchemaId { get; }

    /// <summary>Creates the exception naming the unresolvable schema and the topic it came from.</summary>
    public EventValidationException(string schemaId, string topic)
        : base($"Message on topic '{topic}' declares schema '{schemaId}', which is not registered "
               + "in IEventSchemaRegistry; register it before publishing or drop the declaration.") =>
        SchemaId = schemaId;

    /// <summary>Creates the exception with an explicit message.</summary>
    public EventValidationException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with an explicit message and an inner cause.</summary>
    public EventValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception with the default message.</summary>
    public EventValidationException()
        : base("The message was refused by the EventHub validation stage.")
    {
    }
}
