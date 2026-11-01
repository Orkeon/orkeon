using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Memory;

/// <summary>
/// In-memory <see cref="ICategoryMemoryStore"/>: the store of a host that does not persist its typed memory
/// (<c>Orkeon:Cli:Memory:Persist</c> off). Thread-safe, auto-incrementing integer ids; everything dies with the process.
/// <see cref="FileCategoryMemoryStore"/> is the durable one.
/// </summary>
public sealed class InMemoryCategoryMemoryStore : ICategoryMemoryStore
{
    private readonly object _gate = new();
    private readonly List<MemoryEntry> _entries = new();
    private int _nextId = 1;

    /// <inheritdoc />
    public Task<IReadOnlyList<MemoryEntry>> ListAsync(string? category, bool newestFirst, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(CategoryMemoryEntries.Select(_entries, category, newestFirst));
        }
    }

    /// <inheritdoc />
    public Task<int> AddAsync(string category, string content, CancellationToken ct)
    {
        var trimmed = CategoryMemoryEntries.RequireContent(content);
        var normalized = CategoryMemoryEntries.NormalizeCategory(category);
        lock (_gate)
        {
            var id = _nextId++;
            _entries.Add(new MemoryEntry
            {
                Id = id,
                Category = normalized,
                Content = trimmed,
                CreatedAt = CategoryMemoryEntries.Now(),
            });
            return Task.FromResult(id);
        }
    }

    /// <inheritdoc />
    public Task<bool> ReplaceAsync(int id, string content, CancellationToken ct)
    {
        var trimmed = CategoryMemoryEntries.RequireContent(content);
        lock (_gate)
        {
            var index = _entries.FindIndex(e => e.Id == id);
            if (index < 0) return Task.FromResult(false);
            _entries[index] = _entries[index] with { Content = trimmed, CreatedAt = CategoryMemoryEntries.Now() };
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        lock (_gate)
        {
            var removed = _entries.RemoveAll(e => e.Id == id);
            return Task.FromResult(removed > 0);
        }
    }

    /// <inheritdoc />
    public Task<MemoryEntry?> GetAsync(int id, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_entries.FirstOrDefault(e => e.Id == id));
        }
    }
}
