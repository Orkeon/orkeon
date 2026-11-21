using System.Reflection;
using Orkeon.Domain.Attributes;

namespace Orkeon.Domain.Tools;

/// <summary>
/// What a tool's <see cref="ToolContractAttribute"/> declares, read through the wrappers a host puts around it
/// (an observer, a meter, a bridge proxy): a wrapper is told from the tool it wraps.
/// </summary>
public static class ToolContracts
{
    // A chain of wrappers deeper than this is not unwrapped further.
    private const int MaxWrapperDepth = 16;

    /// <summary>
    /// Whether <paramref name="tool"/>, or a tool it wraps, declares <see cref="ToolContractAttribute.ActsOnWorkspace"/>
    /// (#445): a tool of the files, the shell or the code index.
    /// </summary>
    /// <param name="tool">The tool, wrapped or not.</param>
    /// <returns>True when the contract of the tool or of one it wraps says so.</returns>
    public static bool ActsOnWorkspace(IBaseTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var current = tool;
        for (var depth = 0; depth < MaxWrapperDepth; depth++)
        {
            if (current.GetType().GetCustomAttribute<ToolContractAttribute>(inherit: false) is { ActsOnWorkspace: true })
                return true;
            if (current is not IToolWrapper { Inner: { } inner })
                return false;
            current = inner;
        }

        return false;
    }
}
