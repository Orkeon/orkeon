namespace Orkeon.Domain.FileSystem;

/// <summary>
/// Options controlling the behavior of <see cref="IFileSystemService.EnumerateFilesAsync"/>.
/// </summary>
/// <param name="Recursive">When true (default), descend into sub-directories.</param>
/// <param name="Exclude">
/// Optional list of gitignore-style glob patterns (e.g. <c>node_modules/</c>, <c>**/*.log</c>).
/// Patterns are matched against the path relative to the enumeration root, using <c>/</c> as separator.
/// </param>
/// <param name="FollowSymlinks">When false (default), symbolic links are not traversed.</param>
/// <param name="MaxDepth">Optional maximum recursion depth. <c>null</c> means unlimited.</param>
/// <param name="SearchPattern">
/// Optional simple glob pattern applied to file/directory names (e.g. <c>*.json</c>, <c>audit-*.jsonl</c>).
/// <c>null</c> means match all entries (default, backward-compatible).
/// </param>
public sealed record VirtualEnumerationOptions(
    bool Recursive = true,
    IReadOnlyList<string>? Exclude = null,
    bool FollowSymlinks = false,
    int? MaxDepth = null,
    string? SearchPattern = null);
