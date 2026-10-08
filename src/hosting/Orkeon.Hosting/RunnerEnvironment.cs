using Orkeon.Constants.Configuration;

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
    public const string AllowExternalMountsVariable = EnvironmentVariableNames.AllowExternalMounts;

    /// <summary>Environment variable read by <see cref="DebugDiagnostics"/>.</summary>
    public const string DebugVariable = EnvironmentVariableNames.Debug;

    /// <summary>
    /// Environment variable <c>orkeon mcp serve</c> sets in its own environment before it connects
    /// the MCP servers of its settings, so every process it starts inherits it — and under which it
    /// refuses to start (GAP-35). Settings declaring <c>orkeon mcp serve</c> itself under
    /// <c>MCP:Servers</c> made each server start another before answering, until the first gave up
    /// after 30 s. Like <see cref="DebugVariable"/>, the <c>ORKEON_</c> configuration layer also
    /// reads it, as a key (<c>MCP_SERVE</c>) no setting is.
    /// </summary>
    public const string McpServeVariable = EnvironmentVariableNames.McpServe;

    /// <summary>
    /// True when an <c>orkeon mcp serve</c> started this process: <see cref="McpServeVariable"/> is
    /// set, whatever its value.
    /// </summary>
    public static bool StartedByMcpServe
        => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(McpServeVariable));

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
