using System.Collections.Immutable;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.MCP;

namespace Orkeon.Hosting.ToolBridge;

/// <summary>The tools a side of the bridge selected, and the warnings its operator is owed.</summary>
/// <param name="Tools">The tools selected, by name, in the registry's order.</param>
/// <param name="Warnings">
/// One sentence per tool named explicitly that the lone <c>*</c> would not select: one that acts on the workspace, an MCP
/// tool, a tool whose class declares no contract.
/// </param>
/// <param name="Problems">One sentence per name that selects nothing; a selection with problems refuses the start.</param>
internal sealed record ToolBridgeSelectionResult(
    ImmutableList<IBaseTool> Tools,
    ImmutableList<string> Warnings,
    ImmutableList<string> Problems);

/// <summary>What a tool is, for the selection: the lone <c>*</c> selects only <see cref="Bridgeable"/>.</summary>
internal enum ToolBridgeKind
{
    /// <summary>A tool of Orkeon whose contract declares none of the kinds below: safe to run on another machine.</summary>
    Bridgeable,

    /// <summary>A tool that acts on the workspace (<see cref="ToolContracts.ActsOnWorkspace"/>); named, it is bridged with a warning.</summary>
    Workspace,

    /// <summary>A tool of an MCP server (<see cref="McpToolAdapter"/>); named, it is bridged with a warning.</summary>
    Mcp,

    /// <summary>A tool whose class declares no <c>[ToolContract]</c> (a plugin's, a script's); named, it is bridged with a warning.</summary>
    Unmarked,

    /// <summary>A tool of the caller's own run (<see cref="ToolContracts.StaysWithCaller"/>); naming it refuses the start.</summary>
    StaysWithCaller,

    /// <summary>A coworker delegation tool, which stands for the caller's crew; naming it refuses the start.</summary>
    Delegation,
}

/// <summary>
/// The selection rule both sides of the bridge apply to their own registry (#445): no <c>Allow</c> selects nothing,
/// <c>Deny</c> wins, a name that is no tool of the registry is refused. The lone <c>*</c> selects only the tools of
/// Orkeon that are safe to run on another machine: never a tool that acts on the workspace, an MCP tool, a tool whose
/// class declares no <c>[ToolContract]</c>, a tool that stays with its caller or a coworker delegation tool. Naming a
/// tool of the first three kinds selects it with a warning; naming one of the last two is refused.
/// </summary>
internal static class ToolBridgeSelection
{
    /// <summary>The entry that selects every tool.</summary>
    public const string All = "*";

    // A chain of wrappers deeper than this is not unwrapped further.
    private const int MaxWrapperDepth = 16;

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

    /// <summary>What <paramref name="tool"/> is, for the selection, read through the wrappers a host puts around it.</summary>
    /// <param name="tool">The tool, wrapped or not.</param>
    /// <returns>Its kind; the first that applies of delegation, stays with the caller, MCP, unmarked, workspace.</returns>
    public static ToolBridgeKind KindOf(IBaseTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (CoworkerDelegation.IsDelegationTool(tool))
            return ToolBridgeKind.Delegation;
        if (ToolContracts.StaysWithCaller(tool))
            return ToolBridgeKind.StaysWithCaller;
        if (IsMcp(tool))
            return ToolBridgeKind.Mcp;
        if (ToolContracts.ContractOf(tool) is null)
            return ToolBridgeKind.Unmarked;
        return ToolContracts.ActsOnWorkspace(tool) ? ToolBridgeKind.Workspace : ToolBridgeKind.Bridgeable;
    }

    private static bool IsMcp(IBaseTool tool)
    {
        var current = tool;
        for (var depth = 0; depth < MaxWrapperDepth; depth++)
        {
            if (current is McpToolAdapter)
                return true;
            if (current is not IToolWrapper { Inner: { } inner })
                return false;
            current = inner;
        }

        return false;
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

        var denied = new HashSet<string>(deny, StringComparer.OrdinalIgnoreCase);
        foreach (var name in allow.Where(e => e != All && !denied.Contains(e)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!byName.TryGetValue(name, out var tool))
                continue;
            switch (KindOf(tool))
            {
                case ToolBridgeKind.Delegation:
                    problems.Add($"{side}:Allow names '{name}', a coworker delegation tool: it stands for this crew and is never bridged.");
                    break;
                case ToolBridgeKind.StaysWithCaller:
                    problems.Add($"{side}:Allow names '{name}', a tool of the caller's own run (its human, memory, session, budget, sub-agents or progress): it is never bridged.");
                    break;
            }
        }

        var everything = allow.Contains(All);
        var explicitNames = new HashSet<string>(allow.Where(e => e != All), StringComparer.OrdinalIgnoreCase);
        var selected = ImmutableList.CreateBuilder<IBaseTool>();
        var warnings = ImmutableList.CreateBuilder<string>();
        foreach (var tool in tools)
        {
            if (denied.Contains(tool.Name) || selected.Any(t => string.Equals(t.Name, tool.Name, StringComparison.OrdinalIgnoreCase)))
                continue;
            var kind = KindOf(tool);
            if (kind is ToolBridgeKind.Delegation or ToolBridgeKind.StaysWithCaller)
                continue;
            if (explicitNames.Contains(tool.Name))
            {
                selected.Add(tool);
                if (Warning(side, tool.Name, kind) is { } warning)
                    warnings.Add(warning);
            }
            else if (everything && kind == ToolBridgeKind.Bridgeable)
            {
                selected.Add(tool);
            }
        }

        return new ToolBridgeSelectionResult(selected.ToImmutable(), warnings.ToImmutable(), problems.ToImmutable());
    }

    private static string? Warning(string side, string name, ToolBridgeKind kind) => kind switch
    {
        ToolBridgeKind.Workspace =>
            $"{side}: '{name}' acts on the workspace and is bridged because it is named: it reads and writes the bridge machine, not the caller's.",
        ToolBridgeKind.Mcp =>
            $"{side}: '{name}' is a tool of an MCP server and is bridged because it is named: whatever that server reaches, it reaches on the bridge machine.",
        ToolBridgeKind.Unmarked =>
            $"{side}: '{name}' declares no tool contract (a plugin's or a script's tool) and is bridged because it is named: what it reaches on the bridge machine is not known.",
        _ => null,
    };
}
