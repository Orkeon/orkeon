using System.Collections.Immutable;

namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// The allow and deny rules a session sets for tool calls, which the permission gate reads before its
/// mode. A deny rule refuses a call in every mode. An allow rule lets a call run without asking, in every
/// mode but <c>plan</c>, which stays read-only. A host registers one when its sessions keep rules (the REPL
/// does); without one, the gate decides from the mode alone.
/// </summary>
public interface IToolPermissionRules
{
    /// <summary>The rules in force, read at each tool call.</summary>
    ImmutableList<ToolPermissionRule> Current { get; }
}

/// <summary>
/// One rule of <see cref="IToolPermissionRules"/>: a tool, and optionally the calls whose argument matches
/// a pattern, allowed or denied.
/// </summary>
public sealed record ToolPermissionRule
{
    /// <summary>Whether a matching call runs without asking or is refused.</summary>
    public required ToolPermissionEffect Effect { get; init; }

    /// <summary>The tool's registered name (e.g. <c>shell_command</c>), compared without regard to case.</summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// The argument the <see cref="Pattern"/> is read against (e.g. <c>command</c>), or null for every call of
    /// the tool. A call without that argument does not match.
    /// </summary>
    public string? Argument { get; init; }

    /// <summary>
    /// The text the argument must match whole, where <c>*</c> stands for any run of characters
    /// (<c>npm test*</c>); compared with case. Null with an <see cref="Argument"/> matches any value.
    /// </summary>
    public string? Pattern { get; init; }

    /// <summary>Whether this rule covers a call of <paramref name="toolName"/> with <paramref name="arguments"/>.</summary>
    /// <param name="toolName">The tool called.</param>
    /// <param name="arguments">The arguments the model supplied.</param>
    public bool Matches(string toolName, IReadOnlyDictionary<string, object?> arguments)
    {
        ArgumentNullException.ThrowIfNull(toolName);
        ArgumentNullException.ThrowIfNull(arguments);
        if (!string.Equals(ToolName, toolName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (Argument is null)
            return true;
        if (!arguments.TryGetValue(Argument, out var value) || value is null)
            return false;

        return Pattern is null || GlobMatches(Pattern, value.ToString() ?? "");
    }

    /// <summary>The rule as one line: <c>allow shell_command command=npm test*</c>.</summary>
    public override string ToString()
    {
        var effect = Effect == ToolPermissionEffect.Allow ? "allow" : "deny";
        return Argument is null ? $"{effect} {ToolName}" : $"{effect} {ToolName} {Argument}={Pattern ?? "*"}";
    }

    // '*' matches any run of characters, everything else itself: a two-pointer scan with backtracking to the
    // last star, linear in practice and free of the catastrophic cases of a regular expression.
    private static bool GlobMatches(string pattern, string text)
    {
        int p = 0, t = 0, star = -1, resume = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                resume = t;
            }
            else if (p < pattern.Length && pattern[p] == text[t])
            {
                p++;
                t++;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++resume;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
            p++;
        return p == pattern.Length;
    }
}

/// <summary>What a <see cref="ToolPermissionRule"/> does with the calls it covers.</summary>
public enum ToolPermissionEffect
{
    /// <summary>The call runs without asking (except in <c>plan</c> mode).</summary>
    Allow,

    /// <summary>The call is refused, in every mode; the model reads the refusal as the tool's result.</summary>
    Deny,
}
