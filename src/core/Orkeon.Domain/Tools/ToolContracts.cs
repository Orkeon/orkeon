using System.Reflection;
using Orkeon.Domain.Attributes;

namespace Orkeon.Domain.Tools;

/// <summary>
/// What a tool's <see cref="ToolContractAttribute"/> declares, read through the wrappers a host puts around it
/// (an observer, a meter, a bridge proxy): a wrapper is told from the tool it wraps. The contract of a class is read on
/// the class and on its base classes, the nearest first, since the attribute itself is not inherited.
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
    public static bool ActsOnWorkspace(IBaseTool tool) => Contracts(tool).Any(c => c.ActsOnWorkspace);

    /// <summary>
    /// Whether <paramref name="tool"/>, or a tool it wraps, declares <see cref="ToolContractAttribute.StaysWithCaller"/>
    /// (#445): a tool of the caller's own run, which no tool bridge carries.
    /// </summary>
    /// <param name="tool">The tool, wrapped or not.</param>
    /// <returns>True when the contract of the tool or of one it wraps says so.</returns>
    public static bool StaysWithCaller(IBaseTool tool) => Contracts(tool).Any(c => c.StaysWithCaller);

    /// <summary>
    /// The contract <paramref name="tool"/> declares, or that the tool it wraps declares: the nearest
    /// <see cref="ToolContractAttribute"/> on its class or a base class.
    /// </summary>
    /// <param name="tool">The tool, wrapped or not.</param>
    /// <returns>The contract, or null for a tool whose classes carry none (an MCP tool, a plugin's, a script's).</returns>
    public static ToolContractAttribute? ContractOf(IBaseTool tool) => Contracts(tool).FirstOrDefault();

    private static IEnumerable<ToolContractAttribute> Contracts(IBaseTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return Walk(tool);
    }

    private static IEnumerable<ToolContractAttribute> Walk(IBaseTool tool)
    {
        var current = tool;
        for (var depth = 0; depth < MaxWrapperDepth; depth++)
        {
            for (var type = current.GetType(); type is not null && type != typeof(object); type = type.BaseType)
            {
                if (type.GetCustomAttribute<ToolContractAttribute>(inherit: false) is { } contract)
                    yield return contract;
            }

            if (current is not IToolWrapper { Inner: { } inner })
                yield break;
            current = inner;
        }
    }
}
