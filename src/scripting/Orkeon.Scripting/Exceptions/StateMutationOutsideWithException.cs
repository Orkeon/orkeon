using System.Runtime.Serialization;

namespace Orkeon.Scripting.Exceptions;

/// <summary>
/// Thrown when a script tries to mutate <c>ctx.state</c> directly instead of going
/// through <c>ctx.state.with(fn)</c>. SCR-08 enforces this via a JS proxy that traps
/// <c>set</c> on the state object.
/// </summary>
[Serializable]
public sealed class StateMutationOutsideWithException : Exception
{
    /// <summary>Property the script tried to set.</summary>
    public string PropertyName { get; }

    /// <inheritdoc />
    public StateMutationOutsideWithException(string propertyName)
        : base($"Direct mutation of ctx.state.'{propertyName}' is forbidden. Use ctx.state.with(prev => ...) for atomic updates.")
    {
        PropertyName = propertyName;
    }

    /// <inheritdoc />
    public StateMutationOutsideWithException()
    {
        PropertyName = string.Empty;
    }

    /// <inheritdoc />
    public StateMutationOutsideWithException(string message, Exception innerException) : base(message, innerException)
    {
        PropertyName = string.Empty;
    }

    /// <summary>Deserialization constructor.</summary>
#pragma warning disable SYSLIB0051 // Required by S3925 ISerializable pattern
    private StateMutationOutsideWithException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        PropertyName = string.Empty;
    }
#pragma warning restore SYSLIB0051
}
