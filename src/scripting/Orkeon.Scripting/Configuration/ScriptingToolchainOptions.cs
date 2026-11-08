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
    /// next to the application (<c>esbuild-bin/esbuild</c>), then to the copy <c>npm install</c> places
    /// in a checkout of the repository (<c>tools/scripting-esbuild/node_modules/</c>, searched from the
    /// working directory and the application directory upwards), then to a PATH lookup.
    /// </summary>
    public string? EsbuildPath { get; init; }

    /// <summary>
    /// Maximum time esbuild has to transpile a single source. Default: 30 s.
    /// </summary>
    public TimeSpan EsbuildTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When the TypeScript compiler (<c>tsc</c>) checks the types of a <c>.ork.ts</c> script
    /// against the typings this build ships: <see cref="TypeCheckMode.Off"/> never,
    /// <see cref="TypeCheckMode.Validate"/> (default) in <c>orkeon run --validate</c>,
    /// <see cref="TypeCheckMode.Always"/> in <c>--validate</c> and before every run. Under
    /// <c>Validate</c> a missing compiler is a warning; under <c>Always</c> it is a failure.
    /// </summary>
    public TypeCheckMode TypeCheck { get; init; } = TypeCheckMode.Validate;

    /// <summary>
    /// Absolute path to the <c>tsc</c> binary. When set it is the only candidate: a path that does
    /// not exist is reported as a missing compiler, never silently replaced. When null, the type
    /// checker looks in <c>tools/scripting-typecheck/node_modules/.bin/</c> of a checkout
    /// (searched from the working directory and the application directory upwards), then in <c>PATH</c>.
    /// </summary>
    public string? TscPath { get; init; }

    /// <summary>
    /// Maximum time <c>tsc</c> has to check one script. Default: 60 s.
    /// </summary>
    public TimeSpan TypeCheckTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Binds the <c>Orkeon:Scripting:Toolchain</c> section of <paramref name="configuration"/>.
    /// A missing section, or a <see langword="null"/> configuration, yields the defaults.
    /// </summary>
    public static ScriptingToolchainOptions FromConfiguration(IConfiguration? configuration)
        => configuration?.GetSection(SectionName).Get<ScriptingToolchainOptions>() ?? new ScriptingToolchainOptions();
}

/// <summary>When <c>tsc</c> type-checks a <c>.ork.ts</c> script (<see cref="ScriptingToolchainOptions.TypeCheck"/>).</summary>
public enum TypeCheckMode
{
    /// <summary>Never.</summary>
    Off,

    /// <summary>In <c>orkeon run --validate</c> only (default); a missing <c>tsc</c> is a warning.</summary>
    Validate,

    /// <summary>In <c>--validate</c> and before every run; a missing <c>tsc</c> is a failure.</summary>
    Always,
}
