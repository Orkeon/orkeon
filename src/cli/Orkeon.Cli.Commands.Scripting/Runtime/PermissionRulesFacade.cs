using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Cli.Commands.Scripting.Runtime;

#pragma warning disable IDE1006 // camelCase methods: this CLR type is exposed to scripts as ctx.services.get("permissions")

/// <summary>
/// The session's tool-call rules, as a command sees them: <c>ctx.services.get("permissions")</c>. The
/// permission gate reads them before the mode: a deny rule refuses a call in every mode, an allow rule lets
/// it run without asking in every mode but <c>plan</c>. A rule covers every call of a tool, or the calls
/// whose argument matches a pattern where <c>*</c> stands for any run of characters.
/// </summary>
public sealed class PermissionRulesFacade
{
    private readonly SessionToolPermissionRules _rules;

    internal PermissionRulesFacade(SessionToolPermissionRules rules, bool gateEnabled = false)
    {
        _rules = rules;
        enabled = gateEnabled;
    }

    /// <summary>
    /// Whether the host registered a permission gate (<c>Orkeon:Security:PermissionGate:Enabled = true</c>),
    /// read-only (#128). False, nothing reads these rules or the permission modes: every tool call runs
    /// unchecked.
    /// </summary>
    public bool enabled { get; }

    /// <summary>Lets the tool's calls run without asking: every call, or those whose <paramref name="argument"/> matches <paramref name="pattern"/>.</summary>
    /// <param name="tool">The tool's registered name, e.g. <c>shell_command</c>.</param>
    /// <param name="argument">The argument the pattern is read against, e.g. <c>command</c>; omitted, every call.</param>
    /// <param name="pattern">The text the argument must match whole, e.g. <c>npm test*</c>; omitted, any value.</param>
    public void allow(string tool, string? argument = null, string? pattern = null)
        => _rules.Add(Rule(ToolPermissionEffect.Allow, tool, argument, pattern));

    /// <summary>Refuses the tool's calls in every mode: every call, or those whose <paramref name="argument"/> matches <paramref name="pattern"/>.</summary>
    /// <param name="tool">The tool's registered name, e.g. <c>shell_command</c>.</param>
    /// <param name="argument">The argument the pattern is read against, e.g. <c>command</c>; omitted, every call.</param>
    /// <param name="pattern">The text the argument must match whole, e.g. <c>rm *</c>; omitted, any value.</param>
    public void deny(string tool, string? argument = null, string? pattern = null)
        => _rules.Add(Rule(ToolPermissionEffect.Deny, tool, argument, pattern));

    /// <summary>Removes every rule of the session.</summary>
    public void clear() => _rules.Clear();

    /// <summary>The rules in force, one line each: <c>allow shell_command command=npm test*</c>.</summary>
    public string[] list() => [.. _rules.Current.Select(rule => rule.ToString())];

    private static ToolPermissionRule Rule(ToolPermissionEffect effect, string tool, string? argument, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(tool))
            throw new ArgumentException("permissions: name the tool the rule covers, e.g. 'shell_command'.", nameof(tool));
        if (pattern is not null && string.IsNullOrWhiteSpace(argument))
            throw new ArgumentException("permissions: a pattern is read against an argument; name it, e.g. ('shell_command', 'command', 'npm test*').", nameof(argument));

        return new ToolPermissionRule
        {
            Effect = effect,
            ToolName = tool.Trim(),
            Argument = string.IsNullOrWhiteSpace(argument) ? null : argument.Trim(),
            Pattern = pattern,
        };
    }
}
