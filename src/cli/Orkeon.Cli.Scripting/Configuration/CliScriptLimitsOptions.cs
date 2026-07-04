using Orkeon.Scripting.Configuration;

namespace Orkeon.Cli.Scripting.Configuration;

/// <summary>
/// CLI-specific sandbox profile. WRAPS — never replaces — <see cref="ScriptingLimitsOptions"/>:
/// produces a fresh instance for the CLI's own <c>JsEngineFactory</c> so the global
/// scripting profile (agents / crews) stays untouched.
/// </summary>
/// <remarks>
/// <para>
/// Defaults match spec §8.1: 64 MB / 100 / 5 min — tighter than the global 256 MB
/// because a CLI command should not load an entire crew into memory.
/// </para>
/// <para>
/// Per-script overrides (<c>defineCommand({ limits: ... })</c>) are NOT honoured in v1 —
/// implementing them would require one JsEngineFactory per script. Tracked for Phase 5;
/// see <c>project/tasks/done/CLI-TS-IMPLEMENTATION-DONE.md</c> "deferred".
/// </para>
/// </remarks>
public sealed record CliScriptLimitsOptions
{
    /// <summary>Configuration section name (<c>Orkeon:Cli:ScriptCommands:Limits</c>).</summary>
    public const string SectionName = "Orkeon:Cli:ScriptCommands:Limits";

    /// <summary>Memory cap in bytes. Default: 64 MB (spec §8.1 CLI profile).</summary>
    public long MemoryLimitBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>Recursion depth cap. Default: 100 (inherited from spec §8.1 — same as global).</summary>
    public int RecursionLimit { get; init; } = 100;

    /// <summary>Hard timeout for a single command invocation. Default: 5 minutes.</summary>
    public TimeSpan ExecutionTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Produces a <see cref="ScriptingLimitsOptions"/> consumable by <see cref="Orkeon.Scripting.JsEngineFactory"/>.</summary>
    public ScriptingLimitsOptions ToScriptingLimits() => new()
    {
        MemoryLimitBytes = MemoryLimitBytes,
        RecursionLimit = RecursionLimit,
        ExecutionTimeout = ExecutionTimeout,
    };
}
