using Microsoft.Extensions.Configuration;

namespace Orkeon.Scripting.Configuration;

/// <summary>
/// Toolchain settings for the scripting runtime. Bound to the
/// <c>Orkeon:Scripting:Toolchain</c> configuration section by
/// <see cref="FromConfiguration"/>, which every host that builds an
/// <see cref="Toolchain.EsbuildTranspiler"/> goes through (<see cref="Toolchain.EsbuildTranspiler.Create"/>).
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

    /// <summary>
    /// Binds the <c>Orkeon:Scripting:Toolchain</c> section of <paramref name="configuration"/>.
    /// A missing section, or a <see langword="null"/> configuration, yields the defaults.
    /// </summary>
    public static ScriptingToolchainOptions FromConfiguration(IConfiguration? configuration)
        => configuration?.GetSection(SectionName).Get<ScriptingToolchainOptions>() ?? new ScriptingToolchainOptions();
}
