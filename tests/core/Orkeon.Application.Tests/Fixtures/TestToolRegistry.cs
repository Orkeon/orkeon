using Orkeon.Domain.Tools;
using Orkeon.Domain.Common;
namespace Orkeon.Application.Tests.Fixtures;

/// <summary>
/// Test implementation of IToolRegistry for unit tests.
/// </summary>
public class TestToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, IBaseTool> _tools = [];

    public int RegisterToolCallCount { get; private set; }
    public int GetToolCallCount { get; private set; }
    public int GetAllToolsCallCount { get; private set; }
    public int RemoveToolCallCount { get; private set; }

    public string? LastGetToolName { get; private set; }
    public string? LastRemoveToolName { get; private set; }
    public IBaseTool? LastRegisteredTool { get; private set; }

    public System.Threading.Tasks.Task<bool> RegisterToolAsync(IBaseTool tool)
    {
        RegisterToolCallCount++;
        LastRegisteredTool = tool;
        _tools[tool.Name] = tool;
        return System.Threading.Tasks.Task.FromResult(true);
    }

    public System.Threading.Tasks.Task<IBaseTool?> GetToolAsync(string toolId)
    {
        GetToolCallCount++;
        LastGetToolName = toolId;
        _tools.TryGetValue(toolId, out var tool);
        return System.Threading.Tasks.Task.FromResult(tool);
    }

    public System.Threading.Tasks.Task<IBaseTool?> GetToolByNameAsync(string name)
    {
        GetToolCallCount++;
        LastGetToolName = name;
        _tools.TryGetValue(name, out var tool);
        return System.Threading.Tasks.Task.FromResult(tool);
    }

    public System.Threading.Tasks.Task<IReadOnlyList<IBaseTool>> GetAllToolsAsync()
    {
        GetAllToolsCallCount++;
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<IBaseTool>>(_tools.Values.ToList());
    }

    public System.Threading.Tasks.Task<IReadOnlyList<IBaseTool>> GetToolsByTagsAsync(params string[] tags)
    {
        // For testing, return empty collection
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<IBaseTool>>([]);
    }

    public System.Threading.Tasks.Task<bool> IsRegisteredAsync(string toolId)
    {
        return System.Threading.Tasks.Task.FromResult(_tools.ContainsKey(toolId));
    }

    public System.Threading.Tasks.Task<IReadOnlyList<IBaseTool>> GetToolsByCapabilityAsync(string capability)
    {
        // For testing, return empty collection
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<IBaseTool>>([]);
    }

    public System.Threading.Tasks.Task<bool> UnregisterToolAsync(string toolId)
    {
        RemoveToolCallCount++;
        LastRemoveToolName = toolId;
        return System.Threading.Tasks.Task.FromResult(_tools.Remove(toolId));
    }

    public System.Threading.Tasks.Task ClearAsync()
    {
        _tools.Clear();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    public async System.Threading.Tasks.Task<ToolResult> ExecuteToolAsync(string toolName, string input, CancellationToken cancellationToken = default)
    {
        if (_tools.TryGetValue(toolName, out var tool))
        {
            return await tool.ExecuteAsync(input, cancellationToken);
        }
        return ToolResult.CreateError($"Tool '{toolName}' not found");
    }

    public System.Threading.Tasks.Task<IReadOnlyList<IBaseTool>> GetToolsAsync(IEnumerable<ITool> tools)
    {
        var result = new List<IBaseTool>();
        foreach (var tool in tools)
        {
            if (tool is IBaseTool baseTool && _tools.TryGetValue(baseTool.Name, out var registeredTool))
            {
                result.Add(registeredTool);
            }
        }
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<IBaseTool>>(result);
    }


    public void SetupToolExists(string name, IBaseTool tool)
    {
        _tools[name] = tool;
    }

    public void Reset()
    {
        _tools.Clear();
        RegisterToolCallCount = 0;
        GetToolCallCount = 0;
        GetAllToolsCallCount = 0;
        RemoveToolCallCount = 0;
        LastGetToolName = null;
        LastRemoveToolName = null;
        LastRegisteredTool = null;
    }
}
