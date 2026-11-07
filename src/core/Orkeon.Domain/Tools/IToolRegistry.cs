namespace Orkeon.Domain.Tools;

/// <summary>
/// Interface for managing tool registration and discovery.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Registers a tool with the registry, under its <see cref="IBaseTool.Name"/>.
    /// </summary>
    /// <remarks>
    /// A name belongs to the first tool registered under it (names compare case-insensitively).
    /// Registering another instance under a name already held returns <see langword="false"/>
    /// and leaves the registered tool in place: a tool is never replaced behind the back of the
    /// agents that name it. Registering the instance that already holds the name is an
    /// idempotent success. To replace a tool, unregister it first.
    /// </remarks>
    /// <returns><see langword="true"/> when <paramref name="tool"/> holds its name afterwards.</returns>
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
    /// Checks if a tool is registered.
    /// </summary>
    System.Threading.Tasks.Task<bool> IsRegisteredAsync(string toolId);

    /// <summary>
    /// Clears all registered tools.
    /// </summary>
    System.Threading.Tasks.Task ClearAsync();

    /// <summary>
    /// Swaps <paramref name="current"/> for <paramref name="replacement"/>, which carries the same name, in
    /// one step: a lookup by that name finds the one or the other, never nothing. The swap happens only when
    /// <paramref name="current"/> is the instance registered under its name; a name held by another instance,
    /// or by none, is left as it is.
    /// </summary>
    /// <param name="current">The registered instance to replace.</param>
    /// <param name="replacement">The instance to put in its place; same <c>Name</c>.</param>
    /// <returns><see langword="true"/> when the swap happened.</returns>
    /// <remarks>
    /// The default implementation unregisters then registers, which leaves a gap; a registry that
    /// serves concurrent readers overrides it with an atomic swap (<c>ToolRegistry</c> does).
    /// </remarks>
    async System.Threading.Tasks.Task<bool> ReplaceToolAsync(IBaseTool current, IBaseTool replacement)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(replacement);
        if (!string.Equals(current.Name, replacement.Name, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A replacement carries the name of the tool it replaces.", nameof(replacement));
        if (await GetToolByNameAsync(current.Name).ConfigureAwait(false) is not { } holder || !ReferenceEquals(holder, current))
            return false;
        await UnregisterToolAsync(current.Name).ConfigureAwait(false);
        return await RegisterToolAsync(replacement).ConfigureAwait(false);
    }
}
