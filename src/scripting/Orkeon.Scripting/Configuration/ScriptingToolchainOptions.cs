namespace Orkeon.Scripting.Configuration;

/// <summary>
/// Toolchain settings for the scripting runtime. Bound to the
/// <c>Orkeon:Scripting:Toolchain</c> configuration section.
/// </summary>
public sealed record ScriptingToolchainOptions
{
    /// <summary>
    /// Configuration section name (<c>Orkeon:Scripting:Toolchain</c>).
    /// </summary>
    public const string SectionName = "Orkeon:Scripting:Toolchain";

    /// <summary>
    /// Absolute path to the esbuild binary. When null, the transpiler falls back to
    /// the <c>ORKEON_ESBUILD_PATH</c> environment variable, then to a binary bundled
    /// next to the application (<c>esbuild-bin/esbuild</c>), then to a PATH lookup.
    /// </summary>
    public string? EsbuildPath { get; init; }

    /// <summary>
    /// Maximum time esbuild has to transpile a single source. Default: 30 s.
    /// </summary>
    public TimeSpan EsbuildTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
