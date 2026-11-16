using System.Reflection;
using Orkeon.Domain.Tools;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// An <see cref="IBaseTool"/> whose type lives outside every Orkeon assembly, as a plugin's or a
/// host's own tool does: <see cref="DispatchProxy"/> emits it into a dynamic assembly. Only
/// <see cref="IBaseTool.Name"/> answers; any other member throws.
/// </summary>
#pragma warning disable CA1852 // DispatchProxy needs an unsealed proxy type to derive from.
public class StubForeignTool : DispatchProxy
#pragma warning restore CA1852
{
    /// <summary>The name the proxy answers.</summary>
    public string ToolName { get; set; } = "foreign_tool";

    /// <summary>A proxy named <paramref name="name"/>.</summary>
    public static IBaseTool Create(string name)
    {
        var tool = DispatchProxy.Create<IBaseTool, StubForeignTool>();
        ((StubForeignTool)(object)tool).ToolName = name;
        return tool;
    }

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        "get_Name" => ToolName,
        "get_Access" => ToolAccess.Unspecified,
        "get_UsesFileSystem" => false,
        _ => throw new NotSupportedException($"{targetMethod?.Name} is not stubbed."),
    };
}
