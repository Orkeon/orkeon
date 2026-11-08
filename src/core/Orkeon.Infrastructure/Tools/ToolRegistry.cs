using System.Collections.Concurrent;
using Orkeon.Domain.Tools;

namespace Orkeon.Infrastructure.Tools;

/// <summary>
/// The default <see cref="IToolRegistry"/>: seeded from every <see cref="IBaseTool"/> registered
/// in dependency injection, indexed by name (case-insensitively), and open to tools registered
/// at run time — the MCP client's, a plugin's.
/// <para>
/// <c>AddOrkeonInfrastructure()</c> registers it as the singleton registry, so a host that
/// references only the <c>Orkeon</c> package resolves by name — from a YAML crew's
/// <c>tools:</c> — the tools it registers in DI (GAP-11). The runners use the same class.
/// </para>
/// <para>
/// A name belongs to one tool. Two DI registrations under one name fail the construction with
/// both types named — a wiring mistake surfaces at startup, never as a coin toss at run time —
/// and <see cref="RegisterToolAsync"/> refuses a name already held by another instance rather
/// than replacing it behind the agents that name it (GAP-01). Reads and writes are safe to run
/// concurrently: a host runs several crews while a server connection registers its tools.
/// </para>
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, IBaseTool> _tools = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes the registry from the DI-provided tool collection.</summary>
    /// <param name="tools">Every <see cref="IBaseTool"/> registered in the container.</param>
    /// <exception cref="InvalidOperationException">
    /// Two tools share a name; the message names it and both tool types.
    /// </exception>
    public ToolRegistry(IEnumerable<IBaseTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        // A plugin tool dropped under ContinueOnError resolves to null in the container: skipped.
        foreach (var tool in tools.OfType<IBaseTool>())
        {
            if (HoldsReservedName(tool))
            {
                throw new InvalidOperationException(
                    $"The tool {tool.GetType().FullName} is named '{tool.Name}', a name reserved to the coworker delegation tools. Rename it.");
            }

            if (!_tools.TryAdd(tool.Name, tool))
            {
                var holder = _tools[tool.Name];
                throw new InvalidOperationException(
                    $"Two registered tools are named '{tool.Name}': {holder.GetType().FullName} and " +
                    $"{tool.GetType().FullName}. A tool name must be unique; remove or rename one of them.");
            }
        }
    }

    /// <inheritdoc />
    public Task<bool> RegisterToolAsync(IBaseTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (HoldsReservedName(tool))
            return Task.FromResult(false);
        var holder = _tools.GetOrAdd(tool.Name, tool);
        return Task.FromResult(ReferenceEquals(holder, tool));
    }

    /// <inheritdoc />
    public Task<bool> ReplaceToolAsync(IBaseTool current, IBaseTool replacement)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(replacement);
        if (!string.Equals(current.Name, replacement.Name, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A replacement carries the name of the tool it replaces.", nameof(replacement));
        if (HoldsReservedName(replacement))
            return Task.FromResult(false);
        // One compare-and-swap: a reader finds the old instance or the new one, never a gap.
        return Task.FromResult(_tools.TryUpdate(current.Name, replacement, current));
    }

    // The delegation tools' names are theirs: a tool that only carries one (an MCP server's, a plugin's) is
    // refused, so no agent is handed it in their place.
    private static bool HoldsReservedName(IBaseTool tool) =>
        CoworkerDelegation.IsReservedName(tool.Name) && !CoworkerDelegation.IsDelegationTool(tool);

    /// <inheritdoc />
    public Task<bool> UnregisterToolAsync(string toolId)
        => Task.FromResult(_tools.TryRemove(toolId, out _));

    /// <inheritdoc />
    public Task<IBaseTool?> GetToolAsync(string toolId)
        => Task.FromResult(_tools.GetValueOrDefault(toolId));

    /// <inheritdoc />
    public Task<IBaseTool?> GetToolByNameAsync(string name)
        => Task.FromResult(_tools.GetValueOrDefault(name));

    /// <inheritdoc />
    public Task<IReadOnlyList<IBaseTool>> GetAllToolsAsync()
        => Task.FromResult<IReadOnlyList<IBaseTool>>(_tools.Values.ToList().AsReadOnly());

    /// <inheritdoc />
    public Task<bool> IsRegisteredAsync(string toolId)
        => Task.FromResult(_tools.ContainsKey(toolId));

    /// <inheritdoc />
    public Task ClearAsync()
    {
        _tools.Clear();
        return Task.CompletedTask;
    }
}
