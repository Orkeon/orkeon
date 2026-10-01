using Microsoft.Extensions.Options;
using System.Net;
using System.Text.RegularExpressions;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.Infrastructure.Security.Guards;

/// <summary>
/// Guardian of the <see cref="GuardPhase.ToolExecution"/> phase: screens the arguments of a
/// tool call for path traversal, SSRF targets and SQL injection, before the tool runs. Which
/// tools an agent may call is not its business — <c>ToolAccessPolicy</c> decides that when the
/// agent's tools are resolved. Every finding is Critical and blocks the call.
/// </summary>
public partial class ToolGuard : IGuardian
{
    private readonly bool _blockPrivateIPs;

    [GeneratedRegex(@"(?:'\s*;\s*DROP|1\s*=\s*1|UNION\s+SELECT|OR\s+1\s*=\s*1|'\s*OR\s*'|--\s*$|/\*.*\*/|;\s*DELETE|;\s*UPDATE|;\s*INSERT|'\s*;\s*EXEC|xp_cmdshell)", RegexOptions.IgnoreCase)]
    private static partial Regex SqlInjectionPattern();

    /// <summary>Initializes a new instance of <see cref="ToolGuard"/>.</summary>
    /// <param name="urlOptions">The SSRF options: with <c>Security:Url:BlockPrivateIPs = false</c>
    /// a private address is not a finding here either — the guard never contradicts the
    /// URL policy the web tools apply.</param>
    public ToolGuard(IOptions<UrlSecurityOptions>? urlOptions = null)
    {
        _blockPrivateIPs = urlOptions?.Value.BlockPrivateIPs ?? true;
    }

    /// <inheritdoc />
    public Task<GuardResult> CheckAsync(GuardContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Phase != GuardPhase.ToolExecution || string.IsNullOrEmpty(context.ToolName))
            return Task.FromResult(GuardResult.Allow());

        var argsResult = CheckToolArguments(context.ToolName, context.ToolArgs);
        return Task.FromResult(argsResult ?? GuardResult.Allow());
    }

    private GuardResult? CheckToolArguments(string toolName, IReadOnlyDictionary<string, object?>? toolArgs)
    {
        if (toolArgs is null)
            return null;

        // A *_query tool (postgres_query, mongodb_query, …) runs a query language by design: its
        // statement argument is what the model writes, not a value a query is built from.
        var runsQueries = toolName.EndsWith("_query", StringComparison.OrdinalIgnoreCase);

        foreach (var (key, value) in toolArgs)
        {
            var strValue = value?.ToString();
            if (string.IsNullOrEmpty(strValue))
                continue;

            var result = CheckArgumentForThreats(key, strValue, runsQueries);
            if (result is not null)
                return result;
        }

        return null;
    }

    private GuardResult? CheckArgumentForThreats(string key, string strValue, bool runsQueries)
    {
        if (IsPathArgument(key) && ContainsPathTraversal(strValue))
            return BlockWithViolation(
                $"Path traversal detected in argument '{key}'",
                $"Path traversal detected in tool argument '{key}'",
                GuardThreatSeverity.Critical);

        if (_blockPrivateIPs && IsUrlArgument(key) && ContainsSsrfTarget(strValue))
            return BlockWithViolation(
                $"SSRF target detected in argument '{key}'",
                $"SSRF target detected in tool argument '{key}'",
                GuardThreatSeverity.Critical);

        if (!runsQueries && IsQueryArgument(key) && ContainsSqlInjection(strValue))
            return BlockWithViolation(
                $"SQL injection pattern detected in argument '{key}'",
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
        var host = Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.Host : value;
        return IsBlockedHost(host);
    }

    /// <summary>
    /// Judges a host with the same tables <see cref="UrlValidator"/> uses, rather than with
    /// a table of its own. The regex this replaced covered IPv4 private ranges and the word
    /// "localhost" only, so [::1], [::], the IPv4-mapped and NAT64 forms of the metadata
    /// endpoint, and 100.64.0.0/10 all reached the tool.
    /// </summary>
    private static bool IsBlockedHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        // Uri.Host keeps the brackets on an IPv6 literal; IPAddress.Parse does not want them.
        var candidate = host.Length > 1 && host[0] == '[' && host[^1] == ']'
            ? host[1..^1]
            : host;

        if (IPAddress.TryParse(candidate, out var address))
            return UrlValidator.IsPrivateIP(address);

        return UrlValidator.BlockedHostnames.Contains(host);
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
