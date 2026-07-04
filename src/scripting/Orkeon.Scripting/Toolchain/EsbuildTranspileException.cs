using System.Runtime.Serialization;

namespace Orkeon.Scripting.Toolchain;

/// <summary>
/// Thrown when esbuild rejects the supplied TypeScript source. The exception message
/// carries the formatted stderr output (file:line:col + error description).
/// </summary>
[Serializable]
public sealed class EsbuildTranspileException : Exception
{
    /// <summary>esbuild process exit code.</summary>
    public int ExitCode { get; }

    /// <inheritdoc />
    public EsbuildTranspileException(string message, int exitCode) : base(message)
    {
        ExitCode = exitCode;
    }

    /// <inheritdoc />
    public EsbuildTranspileException() { }

    /// <inheritdoc />
    public EsbuildTranspileException(string message) : base(message) { }

    /// <inheritdoc />
    public EsbuildTranspileException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Deserialization constructor.</summary>
#pragma warning disable SYSLIB0051 // Required by S3925 ISerializable pattern
    private EsbuildTranspileException(SerializationInfo info, StreamingContext context)
        : base(info, context) { }
#pragma warning restore SYSLIB0051
}
