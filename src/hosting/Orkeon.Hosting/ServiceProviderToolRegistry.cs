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
    /// <exception cref="InvalidOperationException">
    /// Two DI-registered tools share a name; the message names it and both tool types.
    /// </exception>
    public ServiceProviderToolRegistry(IEnumerable<IBaseTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        _tools = new Dictionary<string, IBaseTool>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in tools)
        {
            if (_tools.TryGetValue(tool.Name, out var holder))
            {
                throw new InvalidOperationException(
                    $"Two registered tools are named '{tool.Name}': {holder.GetType().FullName} and " +
                    $"{tool.GetType().FullName}. A tool name must be unique; remove or rename one of them.");
            }

            _tools.Add(tool.Name, tool);
        }
    }

    /// <inheritdoc />
    public Task<bool> RegisterToolAsync(IBaseTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (_tools.TryGetValue(tool.Name, out var holder))
            return Task.FromResult(ReferenceEquals(holder, tool));

        _tools.Add(tool.Name, tool);
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
    public Task ClearAsync()
    {
        _tools.Clear();
        return Task.CompletedTask;
    }
}
