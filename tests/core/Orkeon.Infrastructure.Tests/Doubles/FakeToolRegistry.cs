using Orkeon.Domain.Tools;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// An <see cref="IToolRegistry"/> that keeps the interface's default <c>ReplaceToolAsync</c> and records the
/// token every call received, so a test sees what the default implementation passes down.
/// </summary>
public sealed class FakeToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, IBaseTool> _tools = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The token of every call, in call order, with the method's name.</summary>
    public List<(string Method, CancellationToken Token)> ReceivedTokens { get; } = [];

    /// <summary>Runs right after <see cref="UnregisterToolAsync"/> removed a tool: a test cancels its token there.</summary>
    public Action? AfterUnregister { get; set; }

    public Task<bool> RegisterToolAsync(IBaseTool tool, CancellationToken cancellationToken = default)
    {
        ReceivedTokens.Add((nameof(RegisterToolAsync), cancellationToken));
        // A registry honours a cancelled token, as ToolRegistry does.
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_tools.TryAdd(tool.Name, tool));
    }

    public Task<bool> UnregisterToolAsync(string toolId, CancellationToken cancellationToken = default)
    {
        ReceivedTokens.Add((nameof(UnregisterToolAsync), cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        var removed = _tools.Remove(toolId);
        if (removed)
            AfterUnregister?.Invoke();
        return Task.FromResult(removed);
    }

    public Task<IBaseTool?> GetToolAsync(string toolId, CancellationToken cancellationToken = default)
    {
        ReceivedTokens.Add((nameof(GetToolAsync), cancellationToken));
        return Task.FromResult(_tools.GetValueOrDefault(toolId));
    }

    public Task<IBaseTool?> GetToolByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ReceivedTokens.Add((nameof(GetToolByNameAsync), cancellationToken));
        return Task.FromResult(_tools.GetValueOrDefault(name));
    }

    public Task<IReadOnlyList<IBaseTool>> GetAllToolsAsync(CancellationToken cancellationToken = default)
    {
        ReceivedTokens.Add((nameof(GetAllToolsAsync), cancellationToken));
        return Task.FromResult<IReadOnlyList<IBaseTool>>([.. _tools.Values]);
    }

    public Task<bool> IsRegisteredAsync(string toolId, CancellationToken cancellationToken = default)
    {
        ReceivedTokens.Add((nameof(IsRegisteredAsync), cancellationToken));
        return Task.FromResult(_tools.ContainsKey(toolId));
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ReceivedTokens.Add((nameof(ClearAsync), cancellationToken));
        _tools.Clear();
        return Task.CompletedTask;
    }
}
