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
    /// idempotent success. To replace a tool, unregister it first. The names of the coworker
    /// delegation tools (<see cref="CoworkerDelegation.ToolNames"/>) are reserved: a tool that only
    /// carries one is refused (<see langword="false"/>).
    /// </remarks>
    /// <param name="tool">The tool to register.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when <paramref name="tool"/> holds its name afterwards.</returns>
    System.Threading.Tasks.Task<bool> RegisterToolAsync(IBaseTool tool, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters a tool from the registry.
    /// </summary>
    /// <param name="toolId">The identifier of the tool to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task<bool> UnregisterToolAsync(string toolId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a tool by its identifier.
    /// </summary>
    /// <param name="toolId">The identifier of the tool.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task<IBaseTool?> GetToolAsync(string toolId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a tool by its name.
    /// </summary>
    /// <param name="name">The name of the tool.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task<IBaseTool?> GetToolByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all registered tools.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task<IReadOnlyList<IBaseTool>> GetAllToolsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a tool is registered.
    /// </summary>
    /// <param name="toolId">The identifier of the tool.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task<bool> IsRegisteredAsync(string toolId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all registered tools.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Swaps <paramref name="current"/> for <paramref name="replacement"/>, which carries the same name, in
    /// one step: a lookup by that name finds the one or the other, never nothing. The swap happens only when
    /// <paramref name="current"/> is the instance registered under its name; a name held by another instance,
    /// or by none, is left as it is.
    /// </summary>
    /// <param name="current">The registered instance to replace.</param>
    /// <param name="replacement">The instance to put in its place; same <c>Name</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the swap happened.</returns>
    /// <remarks>
    /// The default implementation unregisters then registers, which leaves a gap; a registry that
    /// serves concurrent readers overrides it with an atomic swap (<c>ToolRegistry</c> does). The token is
    /// honoured until the unregistration succeeds; the registration that follows is not cancelled, so a
    /// cancellation never leaves the name without a tool.
    /// </remarks>
    async System.Threading.Tasks.Task<bool> ReplaceToolAsync(IBaseTool current, IBaseTool replacement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(replacement);
        if (!string.Equals(current.Name, replacement.Name, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A replacement carries the name of the tool it replaces.", nameof(replacement));
        if (await GetToolByNameAsync(current.Name, cancellationToken).ConfigureAwait(false) is not { } holder || !ReferenceEquals(holder, current))
            return false;
        if (!await UnregisterToolAsync(current.Name, cancellationToken).ConfigureAwait(false))
            return false;
        // The first half is done: the name is free. A cancellation now would leave it held by no tool for good,
        // so the second half runs to its end whatever the caller's token says.
        return await RegisterToolAsync(replacement, CancellationToken.None).ConfigureAwait(false);
    }
}
