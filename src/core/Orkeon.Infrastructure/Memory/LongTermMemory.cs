using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Memory;

namespace Orkeon.Infrastructure.Memory;

/// <summary>
/// Simple in-memory implementation of long-term memory.
/// Used as fallback when IServiceProvider is not available.
/// </summary>
public sealed class LongTermMemory : ILongTermMemory, IDisposable
{
    private readonly List<MemoryItem> _memories = [];
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <inheritdoc />
    public async Task AddAsync(MemoryItem item)
    {
        // Allow null items to be added
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            _memories.Add(item);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryItem>> SearchAsync(string query, int maxResults = 10)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            // Return empty for null/empty queries
            if (string.IsNullOrWhiteSpace(query))
                return [];

            var keywords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return _memories
                .Where(m => keywords.Any(keyword => m.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(m => m.Timestamp)
                .Take(maxResults)
                .ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryItem>> GetAllAsync()
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            return _memories.OrderByDescending(m => m.Timestamp).ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync()
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            _memories.Clear();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>Releases the semaphore guarding the in-memory memory list.</summary>
    public void Dispose() => _semaphore.Dispose();
}
