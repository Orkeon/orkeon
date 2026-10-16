using System.Collections.Immutable;
using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Cli.Commands.Scripting.Runtime;

/// <summary>
/// The REPL's <see cref="IToolPermissionRules"/>: the allow and deny rules of the session, which the
/// permission gate reads before the mode. Commands set them through <c>ctx.services.get("permissions")</c>
/// (<see cref="PermissionRulesFacade"/>); they last until the REPL exits.
/// </summary>
internal sealed class SessionToolPermissionRules : IToolPermissionRules
{
    private ImmutableList<ToolPermissionRule> _current = [];

    /// <inheritdoc />
    public ImmutableList<ToolPermissionRule> Current => Volatile.Read(ref _current);

    /// <summary>Adds <paramref name="rule"/>, unless the same rule is already in force.</summary>
    public void Add(ToolPermissionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ImmutableInterlocked.Update(ref _current, rules => rules.Contains(rule) ? rules : rules.Add(rule));
    }

    /// <summary>Removes every rule.</summary>
    public void Clear() => Volatile.Write(ref _current, []);
}
