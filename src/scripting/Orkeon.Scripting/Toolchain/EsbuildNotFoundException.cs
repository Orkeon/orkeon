namespace Orkeon.Scripting.Toolchain;

/// <summary>
/// Thrown when the esbuild binary cannot be located through any of the configured
/// resolution strategies (config, env var, bundled binary, PATH).
/// </summary>
public sealed class EsbuildNotFoundException : Exception
{
    /// <inheritdoc />
    public EsbuildNotFoundException(string message) : base(message) { }

    /// <inheritdoc />
    public EsbuildNotFoundException() { }

    /// <inheritdoc />
    public EsbuildNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}
