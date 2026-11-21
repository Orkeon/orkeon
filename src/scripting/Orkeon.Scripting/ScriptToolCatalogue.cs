using System.Collections;
using Orkeon.Domain.Tools;

namespace Orkeon.Scripting;

/// <summary>
/// The tools a script reaches — the <c>tools</c> namespace, an agent's <c>.tools([...])</c> and the
/// catalogue <c>ctx.llm.act</c> picks from: the host's container tools, then the tools its
/// <see cref="IToolRegistry"/> holds besides them (an MCP server's, connected after the container was
/// built), each name once, the first holder keeping it. A registry tool that wraps a container tool of the same name
/// (a tool bridge proxy), or another instance of its type when the container builds it anew, stands in its place. It is read again at each enumeration, so a
/// tool registered after the catalogue was made reaches the next engine built from it.
/// </summary>
/// <remarks>
/// The container tools are taken once: tools are registered transient and disposable, and resolving
/// them again for every engine would materialise a new set each time (ANT-005).
/// </remarks>
public sealed class ScriptToolCatalogue : IEnumerable<IBaseTool>
{
    private readonly IReadOnlyList<IBaseTool> _containerTools;
    private readonly IToolRegistry? _registry;

    /// <summary>Creates the catalogue.</summary>
    /// <param name="containerTools">The tools the host's container registers, taken once here.</param>
    /// <param name="registry">The host's tool registry, read at each enumeration; null for the container tools alone.</param>
    public ScriptToolCatalogue(IEnumerable<IBaseTool> containerTools, IToolRegistry? registry)
    {
        ArgumentNullException.ThrowIfNull(containerTools);
        // A plugin tool dropped under ContinueOnError resolves to null: skipped.
        _containerTools = [.. containerTools.OfType<IBaseTool>()];
        _registry = registry;
    }

    /// <inheritdoc />
    public IEnumerator<IBaseTool> GetEnumerator()
    {
        // The registry is an in-memory map: its task is complete when it is returned.
        var registered = _registry?.GetAllToolsAsync().GetAwaiter().GetResult() ?? [];
        var byName = new Dictionary<string, IBaseTool>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in registered)
            byName.TryAdd(tool.Name, tool);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in _containerTools.Where(tool => names.Add(tool.Name)))
        {
            // A registry tool that wraps the container's instance replaced it there (a tool bridge proxy, #445):
            // the script calls what the agents call.
            yield return byName.TryGetValue(tool.Name, out var holder) && Wraps(holder, tool) ? holder : tool;
        }

        foreach (var tool in registered.Where(tool => names.Add(tool.Name)))
            yield return tool;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // Whether outer is a wrapper around inner, through at most 16 wrappers: inner itself, or, for a tool the
    // container builds anew at each resolution (firefox is transient), another instance of its type at the bottom.
    private static bool Wraps(IBaseTool outer, IBaseTool inner)
    {
        var current = outer;
        for (var depth = 0; depth < 16 && current is IToolWrapper { Inner: { } next }; depth++)
        {
            if (ReferenceEquals(next, inner) || (next is not IToolWrapper && next.GetType() == inner.GetType()))
                return true;
            current = next;
        }

        return false;
    }
}
