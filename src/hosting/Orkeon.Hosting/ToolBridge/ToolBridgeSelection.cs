using System.Collections.Immutable;
using Orkeon.Domain.Tools;

namespace Orkeon.Hosting.ToolBridge;

/// <summary>The tools a side of the bridge selected, and the warnings its operator is owed.</summary>
/// <param name="Tools">The tools selected, by name, in the registry's order.</param>
/// <param name="Warnings">One sentence per tool that acts on the workspace and was named explicitly.</param>
/// <param name="Problems">One sentence per name that selects nothing; a selection with problems refuses the start.</param>
internal sealed record ToolBridgeSelectionResult(
    ImmutableList<IBaseTool> Tools,
    ImmutableList<string> Warnings,
    ImmutableList<string> Problems);

/// <summary>
/// The selection rule both sides of the bridge apply to their own registry (#445): no <c>Allow</c> selects nothing,
/// the lone <c>*</c> selects every tool, <c>Deny</c> wins. A name that is no tool of the registry is refused. The
/// lone <c>*</c> never selects a tool that acts on the workspace (<see cref="ToolContracts.ActsOnWorkspace"/>), nor
/// a coworker delegation tool, which stands for the caller's own crew; naming a workspace tool selects it with a
/// warning, naming a delegation tool is refused.
/// </summary>
internal static class ToolBridgeSelection
{
    /// <summary>The entry that selects every tool.</summary>
    public const string All = "*";

    /// <summary>The problems of the entries' syntax, judged with the options: a family prefix, an empty name.</summary>
    /// <param name="section">The section, for the messages.</param>
    /// <param name="allow">The allowed entries.</param>
    /// <param name="deny">The denied entries.</param>
    /// <returns>One sentence per entry refused.</returns>
    public static IEnumerable<string> SyntaxProblems(string section, IEnumerable<string> allow, IEnumerable<string> deny)
    {
        foreach (var (key, entries) in new[] { ("Allow", allow), ("Deny", deny) })
        {
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry))
                    yield return $"{section}:{key} holds an empty name.";
                else if (entry != All && entry.Contains('*', StringComparison.Ordinal))
                    yield return $"{section}:{key} '{entry}': only a tool's name or the lone '*' is accepted, not a pattern.";
            }
        }
    }

    /// <summary>The tools of <paramref name="tools"/> that <paramref name="allow"/> and <paramref name="deny"/> select.</summary>
    /// <param name="side">The side, for the messages (<c>Orkeon:Bridge</c>, <c>Orkeon:Tools:Bridge</c>).</param>
    /// <param name="tools">Every tool of the side's registry.</param>
    /// <param name="allow">The allowed entries.</param>
    /// <param name="deny">The denied entries.</param>
    /// <returns>The selection, its warnings and its problems.</returns>
    public static ToolBridgeSelectionResult Resolve(
        string side, IReadOnlyList<IBaseTool> tools, IReadOnlyCollection<string> allow, IReadOnlyCollection<string> deny)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(allow);
        ArgumentNullException.ThrowIfNull(deny);
        var byName = new Dictionary<string, IBaseTool>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in tools)
            byName.TryAdd(tool.Name, tool);

        var problems = ImmutableList.CreateBuilder<string>();
        foreach (var (key, entries) in new[] { ("Allow", allow), ("Deny", deny) })
        {
            foreach (var name in entries.Where(e => e != All && !byName.ContainsKey(e)))
                problems.Add($"{side}:{key} names '{name}', which is no tool of this Orkeon.");
        }

        var named = allow.Where(e => e != All && byName.TryGetValue(e, out var tool) && CoworkerDelegation.IsDelegationTool(tool)).ToList();
        foreach (var name in named)
            problems.Add($"{side}:Allow names '{name}', a coworker delegation tool: it stands for this crew and is never bridged.");

        var everything = allow.Contains(All);
        var explicitNames = new HashSet<string>(allow.Where(e => e != All), StringComparer.OrdinalIgnoreCase);
        var denied = new HashSet<string>(deny, StringComparer.OrdinalIgnoreCase);
        var selected = ImmutableList.CreateBuilder<IBaseTool>();
        var warnings = ImmutableList.CreateBuilder<string>();
        foreach (var tool in tools)
        {
            if (denied.Contains(tool.Name) || CoworkerDelegation.IsDelegationTool(tool) || selected.Any(t => string.Equals(t.Name, tool.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            var actsOnWorkspace = ToolContracts.ActsOnWorkspace(tool);
            if (explicitNames.Contains(tool.Name))
            {
                selected.Add(tool);
                if (actsOnWorkspace)
                    warnings.Add($"{side}: '{tool.Name}' acts on the workspace and is bridged because it is named: it reads and writes the bridge machine, not the caller's.");
            }
            else if (everything && !actsOnWorkspace)
            {
                selected.Add(tool);
            }
        }

        return new ToolBridgeSelectionResult(selected.ToImmutable(), warnings.ToImmutable(), problems.ToImmutable());
    }
}
