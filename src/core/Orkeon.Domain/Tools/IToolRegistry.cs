using Orkeon.Domain.Common;

namespace Orkeon.Domain.Tools;

/// <summary>
/// Interface for managing tool registration and discovery.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Registers a tool with the registry.
    /// </summary>
    System.Threading.Tasks.Task<bool> RegisterToolAsync(IBaseTool tool);

    /// <summary>
    /// Unregisters a tool from the registry.
    /// </summary>
    System.Threading.Tasks.Task<bool> UnregisterToolAsync(string toolId);

    /// <summary>
    /// Gets a tool by its identifier.
    /// </summary>
    System.Threading.Tasks.Task<IBaseTool?> GetToolAsync(string toolId);

    /// <summary>
    /// Gets a tool by its name.
    /// </summary>
    System.Threading.Tasks.Task<IBaseTool?> GetToolByNameAsync(string name);

    /// <summary>
    /// Gets all registered tools.
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<IBaseTool>> GetAllToolsAsync();

    /// <summary>
    /// Gets tools filtered by tags.
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<IBaseTool>> GetToolsByTagsAsync(params string[] tags);

    /// <summary>
    /// Checks if a tool is registered.
    /// </summary>
    System.Threading.Tasks.Task<bool> IsRegisteredAsync(string toolId);

    /// <summary>
    /// Gets tools that match a specific capability.
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<IBaseTool>> GetToolsByCapabilityAsync(string capability);

    /// <summary>
    /// Gets multiple tools by their interfaces.
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<IBaseTool>> GetToolsAsync(IEnumerable<ITool> tools);

    /// <summary>
    /// Clears all registered tools.
    /// </summary>
    System.Threading.Tasks.Task ClearAsync();
}
