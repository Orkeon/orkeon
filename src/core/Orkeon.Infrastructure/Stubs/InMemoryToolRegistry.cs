using Microsoft.Extensions.Logging;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using System.Collections.Concurrent;

namespace Orkeon.Infrastructure.Stubs;

/// <summary>
/// In-memory implementation of <see cref="IToolRegistry"/>.
/// Stores tools in a ConcurrentDictionary keyed by tool name.
/// Logs a warning on first use to indicate no persistent registry is configured.
/// </summary>
public sealed partial class InMemoryToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, IBaseTool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<InMemoryToolRegistry> _logger;
    private int _warnedOnce;

    /// <summary>Initializes a new instance of <see cref="InMemoryToolRegistry"/>.</summary>
    public InMemoryToolRegistry(ILogger<InMemoryToolRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    private void WarnOnce()
    {
        if (Interlocked.Exchange(ref _warnedOnce, 1) == 0)
            LogInMemoryFallback();
    }

    /// <inheritdoc />
    public Task<bool> RegisterToolAsync(IBaseTool tool)
    {
        WarnOnce();
        ArgumentNullException.ThrowIfNull(tool);
        var added = _tools.TryAdd(tool.Name, tool);
        if (!added)
            _tools[tool.Name] = tool; // update existing
        if (_logger.IsEnabled(LogLevel.Debug))
            LogToolRegistered(tool.Name);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> UnregisterToolAsync(string toolId)
    {
        WarnOnce();
        var removed = _tools.TryRemove(toolId, out _);
        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task<IBaseTool?> GetToolAsync(string toolId)
    {
        WarnOnce();
        _tools.TryGetValue(toolId, out var tool);
        return Task.FromResult(tool);
    }

    /// <inheritdoc />
    public Task<IBaseTool?> GetToolByNameAsync(string name)
    {
        WarnOnce();
        _tools.TryGetValue(name, out var tool);
        return Task.FromResult(tool);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IBaseTool>> GetAllToolsAsync()
    {
        WarnOnce();
        IReadOnlyList<IBaseTool> list = _tools.Values.ToList().AsReadOnly();
        return Task.FromResult(list);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IBaseTool>> GetToolsByTagsAsync(params string[] tags)
    {
        WarnOnce();
        // IBaseTool does not expose Tags; return empty list.
        IReadOnlyList<IBaseTool> result = Array.Empty<IBaseTool>();
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<bool> IsRegisteredAsync(string toolId)
    {
        return Task.FromResult(_tools.ContainsKey(toolId));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IBaseTool>> GetToolsByCapabilityAsync(string capability)
    {
        WarnOnce();
        // Simple keyword match on tool description
        IReadOnlyList<IBaseTool> result = _tools.Values
            .Where(t => t.Description?.Contains(capability, StringComparison.OrdinalIgnoreCase) == true)
            .ToList()
            .AsReadOnly();
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IBaseTool>> GetToolsAsync(IEnumerable<ITool> tools)
    {
        WarnOnce();
        var names = tools.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<IBaseTool> result = _tools.Values
            .Where(t => names.Contains(t.Name))
            .ToList()
            .AsReadOnly();
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task ClearAsync()
    {
        _tools.Clear();
        return Task.CompletedTask;
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Using InMemoryToolRegistry — tools are not persisted across restarts. Register a persistent IToolRegistry for production.")]
    private partial void LogInMemoryFallback();

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "Registered tool '{ToolName}'")]
    private partial void LogToolRegistered(string toolName);
}
