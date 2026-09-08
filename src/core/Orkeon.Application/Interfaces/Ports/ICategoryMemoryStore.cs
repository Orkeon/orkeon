namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Typed, category-scoped memory store for the coding agent. Mirrors
/// Claude Code's four memory categories (<c>user</c>, <c>project</c>, <c>feedback</c>,
/// <c>reference</c>) with integer-id CRUD, accessible to both <c>*.cmd.ts</c> handlers
/// (via <c>memory_store</c>) and crew agents.
/// </summary>
public interface ICategoryMemoryStore
{
    /// <summary>Lists entries, optionally filtered to a single category.</summary>
    Task<IReadOnlyList<MemoryEntry>> ListAsync(string? category, CancellationToken ct);

    /// <summary>Adds an entry under a category; returns its new id.</summary>
    Task<int> AddAsync(string category, string content, CancellationToken ct);

    /// <summary>Deletes an entry by id; returns whether it existed.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct);

    /// <summary>Gets an entry by id, or null.</summary>
    Task<MemoryEntry?> GetAsync(int id, CancellationToken ct);
}

/// <summary>A single typed memory entry.</summary>
public sealed record MemoryEntry
{
    /// <summary>Stable integer id within the store.</summary>
    public required int Id { get; init; }

    /// <summary>Category: <c>user</c> | <c>project</c> | <c>feedback</c> | <c>reference</c>.</summary>
    public required string Category { get; init; }

    /// <summary>Free-text content of the memory.</summary>
    public required string Content { get; init; }

    /// <summary>ISO-8601 creation timestamp.</summary>
    public string? CreatedAt { get; init; }
}
