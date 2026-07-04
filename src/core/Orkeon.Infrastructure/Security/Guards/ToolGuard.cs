using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Services.Security;

namespace Orkeon.Infrastructure.Security.Guards;

/// <summary>
/// Guardian that checks tool execution requests for security violations.
/// Validates tool allowlists/blocklists, path traversal, SSRF, and SQL injection.
/// </summary>
public partial class ToolGuard : IGuardian
{
    private readonly GuardianPolicyEngine _policyEngine;

    [GeneratedRegex(@"(?:'\s*;\s*DROP|1\s*=\s*1|UNION\s+SELECT|OR\s+1\s*=\s*1|'\s*OR\s*'|--\s*$|/\*.*\*/|;\s*DELETE|;\s*UPDATE|;\s*INSERT|'\s*;\s*EXEC|xp_cmdshell)", RegexOptions.IgnoreCase)]
    private static partial Regex SqlInjectionPattern();

    [GeneratedRegex(@"^(10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(1[6-9]|2[0-9]|3[01])\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3}|127\.\d{1,3}\.\d{1,3}\.\d{1,3}|169\.254\.\d{1,3}\.\d{1,3}|0\.0\.0\.0|localhost)$", RegexOptions.IgnoreCase)]
    private static partial Regex PrivateIpPattern();

    /// <summary>Initializes a new instance of <see cref="ToolGuard"/>.</summary>
    /// <param name="policyEngine">The policy engine used to retrieve per-crew/agent security policies.</param>
    /// <param name="logger">The logger.</param>
    public ToolGuard(GuardianPolicyEngine policyEngine, ILogger<ToolGuard> logger)
    {
        ArgumentNullException.ThrowIfNull(policyEngine);
        _policyEngine = policyEngine;
        ArgumentNullException.ThrowIfNull(logger);
        _ = logger;
    }

    /// <inheritdoc />
    public Task<GuardResult> CheckAsync(GuardContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Phase != GuardPhase.ToolExecution || string.IsNullOrEmpty(context.ToolName))
            return Task.FromResult(GuardResult.Allow());

        var policy = _policyEngine.GetPolicy(context.CrewId, context.AgentId);

        var policyResult = CheckToolPolicy(policy, context.ToolName);
        if (policyResult is not null)
            return Task.FromResult(policyResult);

        var argsResult = CheckToolArguments(context.ToolArgs);
        if (argsResult is not null)
            return Task.FromResult(argsResult);

        return Task.FromResult(GuardResult.Allow());
    }

    private static GuardResult? CheckToolPolicy(GuardianPolicy policy, string toolName)
    {
        if (policy.BlockedTools.Count > 0 &&
            policy.BlockedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
        {
            return BlockWithViolation(
                $"Tool '{toolName}' is in the blocked tools list",
                $"Tool '{toolName}' is blocked by policy",
                GuardThreatSeverity.High);
        }

        if (policy.AllowedTools.Count > 0 &&
            !policy.AllowedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase))
        {
            return BlockWithViolation(
                $"Tool '{toolName}' is not in the allowed tools list",
                $"Tool '{toolName}' is not allowed by policy",
                GuardThreatSeverity.High);
        }

        return null;
    }

    private static GuardResult? CheckToolArguments(Dictionary<string, object>? toolArgs)
    {
        if (toolArgs is null)
            return null;

        foreach (var (key, value) in toolArgs)
        {
            var strValue = value?.ToString();
            if (string.IsNullOrEmpty(strValue))
                continue;

            var result = CheckArgumentForThreats(key, strValue);
            if (result is not null)
                return result;
        }

        return null;
    }

    private static GuardResult? CheckArgumentForThreats(string key, string strValue)
    {
        if (IsPathArgument(key) && ContainsPathTraversal(strValue))
            return BlockWithViolation(
                $"Path traversal detected in argument '{key}': {strValue}",
                $"Path traversal detected in tool argument '{key}'",
                GuardThreatSeverity.Critical);

        if (IsUrlArgument(key) && ContainsSsrfTarget(strValue))
            return BlockWithViolation(
                $"SSRF target detected in argument '{key}': {strValue}",
                $"SSRF target detected in tool argument '{key}'",
                GuardThreatSeverity.Critical);

        if (IsQueryArgument(key) && ContainsSqlInjection(strValue))
            return BlockWithViolation(
                $"SQL injection pattern detected in argument '{key}': {strValue}",
                $"SQL injection detected in tool argument '{key}'",
                GuardThreatSeverity.Critical);

        return null;
    }

    private static GuardResult BlockWithViolation(string detail, string blockMessage, GuardThreatSeverity severity)
    {
        var violation = new GuardViolation(
            nameof(ToolGuard), GuardPhase.ToolExecution, detail, severity, DateTime.UtcNow);
        return GuardResult.Block(blockMessage, [violation]);
    }

    private static bool IsPathArgument(string key) =>
        key.Contains("path", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("file", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("dir", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("folder", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsPathTraversal(string value) =>
        value.Contains("..", StringComparison.Ordinal) || value.Contains('~', StringComparison.Ordinal);

    private static bool IsUrlArgument(string key) =>
        key.Contains("url", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("uri", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("endpoint", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("host", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsSsrfTarget(string value)
    {
        // Try to extract host from URL
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return PrivateIpPattern().IsMatch(uri.Host);
        }

        // Check raw value as host
        return PrivateIpPattern().IsMatch(value);
    }

    private static bool IsQueryArgument(string key) =>
        key.Contains("query", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("sql", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("filter", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("where", StringComparison.OrdinalIgnoreCase) ||
        key.Contains("command", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsSqlInjection(string value) =>
        SqlInjectionPattern().IsMatch(value);
}
