namespace Orkeon.Domain.FileSystem;

/// <summary>Configuration options for watching a virtual file system path for changes.</summary>
/// <param name="SearchPattern">Glob pattern used to filter which entries are reported; <c>null</c> matches all entries.</param>
/// <param name="IncludeSubdirectories">Whether changes in subdirectories are also reported.</param>
/// <param name="DebounceWindow">Optional interval during which rapid successive changes are coalesced into a single event.</param>
public sealed record VirtualWatchOptions(
    string? SearchPattern = null,
    bool IncludeSubdirectories = true,
    TimeSpan? DebounceWindow = null);
