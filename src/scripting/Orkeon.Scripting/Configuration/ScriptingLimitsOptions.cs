namespace Orkeon.Scripting.Configuration;

/// <summary>
/// Sandboxing limits applied to every script engine. Bound to the
/// <c>Orkeon:Scripting:Limits</c> configuration section.
/// </summary>
/// <remarks>
/// Trust model (R2.6 / SEC-009): a <c>.ork.ts</c> script is treated as
/// <strong>Untrusted by default</strong> — equivalent to an untrusted agent. The
/// default values here are deliberately strict (≈100 MB memory, 30 s timeout,
/// recursion 64) to bound local DoS. Trusted runs raise the relevant limits
/// explicitly via configuration; the framework never widens them implicitly.
/// Note: <c>IConfiguration</c> is <strong>not</strong> exposed to scripts via the
/// service whitelist (see <c>DefaultScriptServiceWhitelist</c>) to avoid leaking
/// API keys / connection strings to untrusted code.
/// </remarks>
public sealed record ScriptingLimitsOptions
{
    /// <summary>
    /// Configuration section name (<c>Orkeon:Scripting:Limits</c>).
    /// </summary>
    public const string SectionName = "Orkeon:Scripting:Limits";

    /// <summary>
    /// Maximum cumulative memory the engine is allowed to allocate, in bytes.
    /// Default: <strong>100 MB</strong> — an <c>Untrusted</c>-by-default profile
    /// for <c>.ork.ts</c> scripts of unknown provenance (see R2.6 / SEC-009).
    ///
    /// IMPORTANT: Jint's <c>LimitMemory</c> is <strong>cumulative</strong>, not
    /// peak. It uses <c>GC.GetAllocatedBytesForCurrentThread()</c> and counts
    /// every allocation since the engine was created — including short-lived
    /// intermediates from template-literal concatenation, JSON serialization
    /// of tool calls, esbuild bundle parsing, etc.
    ///
    /// This is a security ceiling that bounds local DoS by a pathological or
    /// hostile script. Trusted production runs that genuinely need more memory
    /// (e.g. RaggableTree bundling) must <strong>opt in</strong> explicitly:
    /// raise <c>Orkeon:Scripting:Limits:MemoryLimitBytes</c> in appsettings, or
    /// set <c>--memory-limit-mb 0</c> on the CLI (translates to a large ceiling)
    /// for a fully trusted run. The strict default stays tight.
    /// </summary>
    public long MemoryLimitBytes { get; init; } = 100L * 1024 * 1024;

    /// <summary>
    /// Maximum recursion depth before the engine throws. Default: <strong>64</strong>
    /// (lowered from 100 for the strict <c>Untrusted</c> profile). Catches runaway
    /// recursion well before the .NET stack overflow while leaving room for
    /// legitimately nested script logic. Trusted scripts may raise it via
    /// <c>Orkeon:Scripting:Limits:RecursionLimit</c>.
    /// </summary>
    public int RecursionLimit { get; init; } = 64;

    /// <summary>
    /// Maximum wall-clock time the engine is allowed to run, in total.
    /// Default: <strong>30 seconds</strong> — strict <c>Untrusted</c> profile
    /// (see R2.6 / SEC-009).
    ///
    /// IMPORTANT: this configures Jint's <c>TimeoutInterval</c>, which is
    /// <strong>wall-clock</strong>, not JS-bytecode CPU time. Time spent
    /// awaiting a C# Task from JS (e.g. <c>await Tools.indexCodebase(...)</c>)
    /// IS counted — the engine's internal Stopwatch keeps ticking while the
    /// host runs async work.
    ///
    /// This is a security ceiling that bounds how long a hostile script can pin
    /// a thread. Trusted long-running design-spec runs must <strong>opt in</strong>
    /// explicitly by raising <c>Orkeon:Scripting:Limits:ExecutionTimeout</c> in
    /// appsettings. The strict default stays tight.
    /// </summary>
    public TimeSpan ExecutionTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
