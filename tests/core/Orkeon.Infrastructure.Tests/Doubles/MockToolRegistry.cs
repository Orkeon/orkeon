using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for IToolRegistry with call tracking and configurable results.
/// </summary>
public class MockToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, IBaseTool> _toolsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IBaseTool> _toolsByName = new(StringComparer.OrdinalIgnoreCase);

    // --- Tracking ---
    public int RegisterToolCallCount { get; private set; }
    public IBaseTool? LastRegisteredTool { get; private set; }

    public int UnregisterToolCallCount { get; private set; }
    public string? LastUnregisteredToolId { get; private set; }

    public int GetToolCallCount { get; private set; }
    public int GetToolByNameCallCount { get; private set; }
    public int GetAllToolsCallCount { get; private set; }
    public int GetToolsByTagsCallCount { get; private set; }
    public int IsRegisteredCallCount { get; private set; }
    public int GetToolsByCapabilityCallCount { get; private set; }
    public int GetToolsCallCount { get; private set; }
    public int ClearCallCount { get; private set; }

    // --- Configuration ---
    public void AddTool(string id, IBaseTool tool)
    {
        _toolsById[id] = tool;
        _toolsByName[tool.Name] = tool;
    }

    // --- IToolRegistry ---
    public Task<bool> RegisterToolAsync(IBaseTool tool)
    {
        RegisterToolCallCount++;
        LastRegisteredTool = tool;
        var id = tool.Name;
        _toolsById[id] = tool;
        _toolsByName[tool.Name] = tool;
        return Task.FromResult(true);
    }

    public Task<bool> UnregisterToolAsync(string toolId)
    {
        UnregisterToolCallCount++;
        LastUnregisteredToolId = toolId;
        var removed = _toolsById.Remove(toolId);
        return Task.FromResult(removed);
    }

    public Task<IBaseTool?> GetToolAsync(string toolId)
    {
        GetToolCallCount++;
        _toolsById.TryGetValue(toolId, out var tool);
        return Task.FromResult(tool);
    }

    public Task<IBaseTool?> GetToolByNameAsync(string name)
    {
        GetToolByNameCallCount++;
        _toolsByName.TryGetValue(name, out var tool);
        return Task.FromResult(tool);
    }

    public Task<IReadOnlyList<IBaseTool>> GetAllToolsAsync()
    {
        GetAllToolsCallCount++;
        IReadOnlyList<IBaseTool> tools = _toolsById.Values.ToList().AsReadOnly();
        return Task.FromResult(tools);
    }

    public Task<IReadOnlyList<IBaseTool>> GetToolsByTagsAsync(params string[] tags)
    {
        GetToolsByTagsCallCount++;
        IReadOnlyList<IBaseTool> tools = _toolsById.Values.ToList().AsReadOnly();
        return Task.FromResult(tools);
    }

    public Task<bool> IsRegisteredAsync(string toolId)
    {
        IsRegisteredCallCount++;
        return Task.FromResult(_toolsById.ContainsKey(toolId));
    }

    public Task<IReadOnlyList<IBaseTool>> GetToolsByCapabilityAsync(string capability)
    {
        GetToolsByCapabilityCallCount++;
        IReadOnlyList<IBaseTool> tools = _toolsById.Values.ToList().AsReadOnly();
        return Task.FromResult(tools);
    }

    public Task<IReadOnlyList<IBaseTool>> GetToolsAsync(IEnumerable<ITool> tools)
    {
        GetToolsCallCount++;
        var result = new List<IBaseTool>();
        foreach (var tool in tools)
        {
            if (_toolsByName.TryGetValue(tool.Name, out var found))
                result.Add(found);
        }
        IReadOnlyList<IBaseTool> readOnly = result.AsReadOnly();
        return Task.FromResult(readOnly);
    }

    public Task ClearAsync()
    {
        ClearCallCount++;
        _toolsById.Clear();
        _toolsByName.Clear();
        return Task.CompletedTask;
    }
}
