namespace Orkeon.Scripting.Toolchain;

/// <summary>
/// No-op transpiler that returns the source as-is. Useful for tests and when the host
/// guarantees pure-JS input (e.g. content already produced by another build step).
/// </summary>
public sealed class PassThroughTranspiler : IScriptTranspiler
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly PassThroughTranspiler Instance = new();

    /// <inheritdoc />
    public Task<string> TranspileAsync(string tsSource, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tsSource);
        return Task.FromResult(tsSource);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Pass-through cannot resolve imports — it returns the already-read source verbatim.
    /// If the script contains <c>import</c> statements they will reach Jint and throw.
    /// </remarks>
    public Task<string> BundleFromFileAsync(string physicalPath, string source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Task.FromResult(source);
    }
}
