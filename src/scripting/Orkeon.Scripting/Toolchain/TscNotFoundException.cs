using System.Runtime.Serialization;

namespace Orkeon.Scripting.Toolchain;

/// <summary>
/// Thrown when the TypeScript compiler (<c>tsc</c>) cannot be located or started, so a script
/// cannot be type-checked. A host decides what that costs (<see cref="Configuration.TypeCheckMode"/>).
/// </summary>
[Serializable]
public sealed class TscNotFoundException : Exception
{
    /// <inheritdoc />
    public TscNotFoundException(string message) : base(message) { }

    /// <inheritdoc />
    public TscNotFoundException() { }

    /// <inheritdoc />
    public TscNotFoundException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Deserialization constructor.</summary>
#pragma warning disable SYSLIB0051 // Required by S3925 ISerializable pattern
    private TscNotFoundException(SerializationInfo info, StreamingContext context)
        : base(info, context) { }
#pragma warning restore SYSLIB0051
}
