using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;

namespace Orkeon.Hosting;

/// <summary>
/// Resolves YAML tool names to IBaseTool instances registered in DI.
/// Used by CrewFactory to create agents with their tools.
/// </summary>
public class ServiceProviderToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, IBaseTool> _tools;

    /// <summary>Initializes the registry from the DI-provided tool collection.</summary>
    public ServiceProviderToolRegistry(IEnumerable<IBaseTool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public Task<bool> RegisterToolAsync(IBaseTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tools[tool.Name] = tool;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> UnregisterToolAsync(string toolId)
        => Task.FromResult(_tools.Remove(toolId));

    /// <inheritdoc />
    public Task<IBaseTool?> GetToolAsync(string toolId)
        => Task.FromResult(_tools.GetValueOrDefault(toolId));

    /// <inheritdoc />
    public Task<IBaseTool?> GetToolByNameAsync(string name)
        => Task.FromResult(_tools.GetValueOrDefault(name));

    /// <inheritdoc />
    public Task<IReadOnlyList<IBaseTool>> GetAllToolsAsync()
        => Task.FromResult<IReadOnlyList<IBaseTool>>(_tools.Values.ToList());

    /// <inheritdoc />
    public Task<IReadOnlyList<IBaseTool>> GetToolsByTagsAsync(params string[] tags)
        => Task.FromResult<IReadOnlyList<IBaseTool>>(Array.Empty<IBaseTool>());

    /// <inheritdoc />
    public Task<bool> IsRegisteredAsync(string toolId)
        => Task.FromResult(_tools.ContainsKey(toolId));

    /// <inheritdoc />
    public Task<IReadOnlyList<IBaseTool>> GetToolsByCapabilityAsync(string capability)
        => Task.FromResult<IReadOnlyList<IBaseTool>>(Array.Empty<IBaseTool>());

    /// <inheritdoc />
    public Task<IReadOnlyList<IBaseTool>> GetToolsAsync(IEnumerable<ITool> tools)
    {
        var names = tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Task.FromResult<IReadOnlyList<IBaseTool>>(
            _tools.Values.Where(t => names.Contains(t.Name)).ToList());
    }

    /// <inheritdoc />
    public Task ClearAsync()
    {
        _tools.Clear();
        return Task.CompletedTask;
    }
}
