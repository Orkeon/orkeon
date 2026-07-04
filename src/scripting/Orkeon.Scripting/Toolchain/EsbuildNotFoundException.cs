using System.Runtime.Serialization;

namespace Orkeon.Scripting.Toolchain;

/// <summary>
/// Thrown when the esbuild binary cannot be located through any of the configured
/// resolution strategies (config, env var, bundled binary, PATH).
/// </summary>
[Serializable]
public sealed class EsbuildNotFoundException : Exception
{
    /// <inheritdoc />
    public EsbuildNotFoundException(string message) : base(message) { }

    /// <inheritdoc />
    public EsbuildNotFoundException() { }

    /// <inheritdoc />
    public EsbuildNotFoundException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Deserialization constructor.</summary>
#pragma warning disable SYSLIB0051 // Required by S3925 ISerializable pattern
    private EsbuildNotFoundException(SerializationInfo info, StreamingContext context)
        : base(info, context) { }
#pragma warning restore SYSLIB0051
}
