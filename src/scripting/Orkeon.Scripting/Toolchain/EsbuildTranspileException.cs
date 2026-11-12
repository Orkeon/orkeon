namespace Orkeon.Scripting.Toolchain;

/// <summary>
/// Thrown when esbuild rejects the supplied TypeScript source. The exception message
/// carries the formatted stderr output (file:line:col + error description).
/// </summary>
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
}
