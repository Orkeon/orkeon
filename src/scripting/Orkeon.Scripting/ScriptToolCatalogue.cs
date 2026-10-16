using System.Collections;
using Orkeon.Domain.Tools;

namespace Orkeon.Scripting;

/// <summary>
/// The tools a script reaches — the <c>tools</c> namespace, an agent's <c>.tools([...])</c> and the
/// catalogue <c>ctx.llm.act</c> picks from: the host's container tools, then the tools its
/// <see cref="IToolRegistry"/> holds besides them (an MCP server's, connected after the container was
/// built), each name once, the first holder keeping it. It is read again at each enumeration, so a
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
        _containerTools = [.. containerTools];
        _registry = registry;
    }

    /// <inheritdoc />
    public IEnumerator<IBaseTool> GetEnumerator()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in _containerTools)
        {
            if (names.Add(tool.Name))
                yield return tool;
        }

        if (_registry is null)
            yield break;

        // The registry is an in-memory map: its task is complete when it is returned.
        foreach (var tool in _registry.GetAllToolsAsync().GetAwaiter().GetResult())
        {
            if (names.Add(tool.Name))
                yield return tool;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
