using System.Runtime.Serialization;

namespace Orkeon.Application.Validation;

/// <summary>
/// Exception thrown when command validation fails.
/// </summary>
[Serializable]
public class CommandValidationException : Exception
{
    /// <summary>
    /// Gets the validation errors.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="CommandValidationException"/>.
    /// </summary>
    public CommandValidationException(IReadOnlyList<string> errors)
        : base($"Command validation failed: {string.Join("; ", errors)}")
    {
        Errors = errors;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="CommandValidationException"/>.
    /// </summary>
    public CommandValidationException()
    {
        Errors = Array.Empty<string>();
    }

    /// <summary>
    /// Initializes a new instance of <see cref="CommandValidationException"/> with a message.
    /// </summary>
    public CommandValidationException(string message)
        : base(message)
    {
        Errors = Array.Empty<string>();
    }

    /// <summary>
    /// Initializes a new instance of <see cref="CommandValidationException"/> with a message and inner exception.
    /// </summary>
    public CommandValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = Array.Empty<string>();
    }

    /// <summary>
    /// Initializes a new instance of <see cref="CommandValidationException"/> for deserialization.
    /// </summary>
#pragma warning disable CA2229, SYSLIB0051 // Required by S3925 ISerializable pattern
    protected CommandValidationException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        Errors = Array.Empty<string>();
    }
#pragma warning restore CA2229, SYSLIB0051
}
