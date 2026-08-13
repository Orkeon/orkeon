namespace Orkeon.Hosting;

/// <summary>
/// Process-environment defaults shared by every runner. Environment variables provide
/// deployment-level defaults for CLI flags: a sandboxed deployment (e.g. the
/// orkeon-runners container image) bakes <c>ORKEON_ALLOW_EXTERNAL_MOUNTS=1</c> because
/// its own boundary already provides the isolation the flag's cwd guard approximates,
/// so every mount "outside the cwd" is still inside the sandbox.
/// </summary>
public static class RunnerEnvironment
{
    /// <summary>Environment variable read by <see cref="AllowExternalMounts"/>.</summary>
    public const string AllowExternalMountsVariable = "ORKEON_ALLOW_EXTERNAL_MOUNTS";

    /// <summary>Environment variable read by <see cref="DebugDiagnostics"/>.</summary>
    public const string DebugVariable = "ORKEON_DEBUG";

    /// <summary>
    /// True when <c>ORKEON_DEBUG</c> is set to <c>1</c>, <c>true</c> or <c>yes</c>
    /// (case-insensitive) — the opt-in that turns the runner's user-facing one-line
    /// diagnostics back into full exception dumps (type chain + stack).
    /// </summary>
    public static bool DebugDiagnostics
        => IsTruthy(Environment.GetEnvironmentVariable(DebugVariable));

    /// <summary>
    /// True when <c>ORKEON_ALLOW_EXTERNAL_MOUNTS</c> is set to <c>1</c>, <c>true</c> or
    /// <c>yes</c> (case-insensitive) — the environment-level equivalent of passing
    /// <c>--allow-external-mounts</c> on every invocation.
    /// </summary>
    public static bool AllowExternalMounts
        => IsTruthy(Environment.GetEnvironmentVariable(AllowExternalMountsVariable));

    private static bool IsTruthy(string? value)
        => value is not null
           && (value == "1"
               || value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
}
