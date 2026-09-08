using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Memory;

/// <summary>
/// In-memory <see cref="ICategoryMemoryStore"/> for the MVP. Thread-safe,
/// auto-incrementing integer ids. A provider-backed implementation (over <c>IMemoryProvider</c>)
/// can replace this later without touching the tool or the port.
/// </summary>
public sealed class InMemoryCategoryMemoryStore : ICategoryMemoryStore
{
    private static readonly HashSet<string> KnownCategories =
        new(StringComparer.OrdinalIgnoreCase) { "user", "project", "feedback", "reference" };

    private readonly object _gate = new();
    private readonly List<MemoryEntry> _entries = new();
    private int _nextId = 1;

    /// <inheritdoc />
    public Task<IReadOnlyList<MemoryEntry>> ListAsync(string? category, CancellationToken ct)
    {
        lock (_gate)
        {
            IEnumerable<MemoryEntry> query = _entries;
            if (!string.IsNullOrWhiteSpace(category))
                query = query.Where(e => string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult<IReadOnlyList<MemoryEntry>>(query.ToArray());
        }
    }

    /// <inheritdoc />
    public Task<int> AddAsync(string category, string content, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("memory content must not be empty.", nameof(content));

        var normalized = NormalizeCategory(category);
        lock (_gate)
        {
            var id = _nextId++;
            _entries.Add(new MemoryEntry
            {
                Id = id,
                Category = normalized,
                Content = content.Trim(),
                CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            });
            return Task.FromResult(id);
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

    private static string NormalizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return "user";
        var trimmed = category.Trim();
#pragma warning disable CA1308 // lowercase is the required normalized storage/return form for the category key
        return KnownCategories.Contains(trimmed) ? trimmed.ToLowerInvariant() : trimmed;
#pragma warning restore CA1308
    }
}
