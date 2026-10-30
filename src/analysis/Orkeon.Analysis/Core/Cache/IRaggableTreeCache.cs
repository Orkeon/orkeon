using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Core.Cache;

/// <summary>
/// The index of a root saved on disk between sessions. An entry is returned only under the key it was saved with,
/// so a change of options, adapters or embedder never reloads an index they did not build.
/// </summary>
public interface IRaggableTreeCache
{
    /// <summary>The entry saved for <paramref name="rootPath"/> under <paramref name="cacheKey"/>, or null: absent, never saved under this key or evicted, unreadable or torn.</summary>
    Task<RaggableTreeCacheEntry?> LoadAsync(string rootPath, string cacheKey, CancellationToken ct);

    /// <summary>Saves <paramref name="entry"/>; false when the root cannot be written (a read-only mount), never an exception for it.</summary>
    Task<bool> SaveAsync(string rootPath, RaggableTreeCacheEntry entry, CancellationToken ct);

    Task InvalidateAsync(string rootPath, CancellationToken ct);
}

/// <summary>
/// One saved index: the tree, its id, the key it was built under, the stamp (SHA-256, size, last write time) of each
/// indexed file by relative path, and the SHA-256 of each package marker file (a csproj, a package.json) by relative path.
/// </summary>
public sealed record RaggableTreeCacheEntry(
    RaggableTree Tree,
    string IndexId,
    string CacheKey,
    IReadOnlyDictionary<string, FileStamp> Files,
    IReadOnlyDictionary<string, string> Markers)
{
    /// <summary>
    /// The runtime that built the entry: the cache format and the Analysis version, the part of <see cref="CacheKey"/>
    /// another runtime can never match. A save drops every entry of another runtime (#248).
    /// </summary>
    public string Runtime { get; init; } = "";
}
