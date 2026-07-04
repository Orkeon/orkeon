using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Memory;
using System.Collections.Concurrent;

namespace Orkeon.Infrastructure.Memory;

/// <summary>
/// Short-term memory implementation with sliding window.
/// </summary>
public sealed class ShortTermMemory : IShortTermMemory
{
    private readonly ConcurrentQueue<MemoryItem> _memories = new();
    private readonly int _maxItems;

    /// <summary>Initializes a new instance of <see cref="ShortTermMemory"/>.</summary>
    /// <param name="maxItems">The maximum number of items to retain in the sliding window.</param>
    public ShortTermMemory(int maxItems = 100)
    {
        _maxItems = maxItems;
    }

    /// <inheritdoc />
    public Task AddAsync(MemoryItem item)
    {
        _memories.Enqueue(item);

        // Maintain sliding window
        while (_memories.Count > _maxItems)
        {
            _memories.TryDequeue(out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MemoryItem>> GetRecentAsync(int count = 10)
    {
        var recent = _memories
            .Reverse()
            .Take(count)
            .ToList();

        return Task.FromResult<IReadOnlyList<MemoryItem>>(recent);
    }

    /// <inheritdoc />
    public Task ClearAsync()
    {
        Clear();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Clear()
    {
        while (_memories.TryDequeue(out _))
        {
            // Intentionally empty - draining the queue to clear all items
        }
    }
}
