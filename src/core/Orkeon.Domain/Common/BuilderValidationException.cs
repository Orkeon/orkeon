namespace Orkeon.Domain.Common;

/// <summary>
/// Exception thrown when a builder's Build() method is called with invalid or incomplete configuration.
/// </summary>
#pragma warning disable S3925 // BinaryFormatter serialization is obsolete in .NET 10; ISerializable pattern not required
public sealed class BuilderValidationException : InvalidOperationException
#pragma warning restore S3925
{
    /// <summary>
    /// Gets the name of the builder that failed validation.
    /// </summary>
    public string BuilderName { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="BuilderValidationException"/>.
    /// </summary>
    /// <param name="builderName">The name of the builder that failed validation.</param>
    /// <param name="message">A description of the validation failure.</param>
    public BuilderValidationException(string builderName, string message)
        : base($"{builderName}Builder validation failed: {message}")
    {
        BuilderName = builderName;
    }

    /// <summary>Initializes a new instance of <see cref="BuilderValidationException"/>.</summary>
    public BuilderValidationException() { BuilderName = string.Empty; }

    /// <summary>Initializes a new instance of <see cref="BuilderValidationException"/>.</summary>
    /// <param name="message">A description of the validation failure.</param>
    public BuilderValidationException(string message) : base(message) { BuilderName = string.Empty; }

    /// <summary>Initializes a new instance of <see cref="BuilderValidationException"/> with an inner exception.</summary>
    /// <param name="message">A description of the validation failure.</param>
    /// <param name="innerException">The inner exception.</param>
    public BuilderValidationException(string message, Exception innerException) : base(message, innerException) { BuilderName = string.Empty; }
}
