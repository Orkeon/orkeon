using Orkeon.Domain.FileSystem;

namespace Orkeon.Plugins;

/// <summary>
/// VFS-based implementation of <see cref="IPluginAssemblyDiscovery"/>: enumerates the
/// plugin directory through <see cref="IFileSystemService"/> (virtual paths) and resolves
/// each candidate to its physical path via
/// <see cref="IFileSystemService.ResolveAndValidate"/> — the mount-aware, rights-audited
/// resolution mechanism required by the <see cref="System.Runtime.Loader.AssemblyLoadContext"/>
/// loading APIs.
/// </summary>
public sealed class PluginAssemblyDiscovery : IPluginAssemblyDiscovery
{
    private const string AssemblyExtension = ".dll";

    private readonly IFileSystemService _fileSystem;

    /// <summary>
    /// Creates a discovery service over the given virtual file system.
    /// </summary>
    /// <param name="fileSystem">Mount-aware virtual file system service.</param>
    public PluginAssemblyDiscovery(IFileSystemService fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PluginAssemblyCandidate>> DiscoverAsync(
        OrkeonPluginsOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return DiscoverCoreAsync();

        async Task<IReadOnlyList<PluginAssemblyCandidate>> DiscoverCoreAsync()
        {
            var root = NormalizeVirtualDirectory(options.Directory);

            if (!await _fileSystem.ExistsAsync(root, ct).ConfigureAwait(false))
                return Array.Empty<PluginAssemblyCandidate>();

            var candidates = new List<PluginAssemblyCandidate>();
            var topLevelOnly = new VirtualEnumerationOptions(Recursive: false);

            await foreach (var entry in _fileSystem.EnumerateFilesAsync(root, topLevelOnly, ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                if (await ResolveEntryCandidateAsync(entry, options, ct).ConfigureAwait(false) is { } candidate)
                    candidates.Add(candidate);
            }

            candidates.Sort(static (a, b) => string.CompareOrdinal(a.VirtualPath, b.VirtualPath));
            return candidates;
        }
    }

    /// <summary>
    /// Resolves a single enumerated entry to a plugin candidate, or <c>null</c> when the entry
    /// is not a plugin assembly (or is denied by the VFS). Handles the two supported layouts:
    /// flat (<c>&lt;dir&gt;/MyPlugin.dll</c>) and folder-per-plugin
    /// (<c>&lt;dir&gt;/MyPlugin/MyPlugin.dll</c>). Symbolic links are not followed in v1.
    /// </summary>
    private async Task<PluginAssemblyCandidate?> ResolveEntryCandidateAsync(
        VirtualFileEntry entry, OrkeonPluginsOptions options, CancellationToken ct)
    {
        switch (entry.Kind)
        {
            // Layout 1 — flat: <dir>/MyPlugin.dll
            case VirtualEntryKind.File when IsCandidateFileName(GetVirtualFileName(entry.VirtualPath), options.SearchPattern):
                return TryResolveCandidate(entry.VirtualPath);

            // Layout 2 — folder-per-plugin: <dir>/MyPlugin/MyPlugin.dll
            case VirtualEntryKind.Directory:
                return await ResolveConventionalDirCandidateAsync(entry, options, ct).ConfigureAwait(false);

            default:
                return null;
        }
    }

    /// <summary>
    /// Resolves the conventional <c>&lt;dir&gt;/&lt;dir&gt;.dll</c> assembly inside a plugin folder,
    /// or <c>null</c> when the convention is not matched, the file is absent, or the VFS denies it.
    /// </summary>
    private async Task<PluginAssemblyCandidate?> ResolveConventionalDirCandidateAsync(
        VirtualFileEntry entry, OrkeonPluginsOptions options, CancellationToken ct)
    {
        var dirName = GetVirtualFileName(entry.VirtualPath);
        var conventionalFileName = dirName + AssemblyExtension;
        if (!IsCandidateFileName(conventionalFileName, options.SearchPattern))
            return null;

        var conventionalPath = $"{entry.VirtualPath}/{conventionalFileName}";
        return await _fileSystem.ExistsAsync(conventionalPath, ct).ConfigureAwait(false)
            ? TryResolveCandidate(conventionalPath)
            : null;
    }

    /// <summary>
    /// Resolves the candidate's physical path through the VFS. Returns <c>null</c> when the
    /// candidate is denied by the mount rights (the VFS policy wins). Returning the resolved
    /// candidate (rather than mutating a passed-in list) keeps the collection mutation visible
    /// at the call site so the loop's accumulation is provable by static analysis.
    /// </summary>
    private PluginAssemblyCandidate? TryResolveCandidate(string virtualPath)
    {
        var validation = _fileSystem.ResolveAndValidate(virtualPath, FileAccessRights.Read);
        return validation is { IsAllowed: true, ResolvedPath: not null }
            ? new PluginAssemblyCandidate
            {
                VirtualPath = virtualPath,
                PhysicalPath = validation.ResolvedPath,
            }
            : null;
    }

    private static bool IsCandidateFileName(string fileName, string searchPattern) =>
        fileName.EndsWith(AssemblyExtension, StringComparison.OrdinalIgnoreCase)
        && MatchesSearchPattern(fileName, searchPattern);

    private static bool MatchesSearchPattern(string fileName, string searchPattern)
    {
        if (string.IsNullOrEmpty(searchPattern) || searchPattern == "*")
            return true;

        // System.IO.Enumeration — pure name matching, no file system access.
        return System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
            searchPattern, fileName, ignoreCase: true);
    }

    private static string GetVirtualFileName(string virtualPath)
    {
        var idx = virtualPath.LastIndexOf('/');
        return idx < 0 ? virtualPath : virtualPath[(idx + 1)..];
    }

    private static string NormalizeVirtualDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!directory.StartsWith('/'))
        {
            throw new ArgumentException(
                $"Plugin directory must be a virtual path starting with '/': '{directory}'.",
                nameof(directory));
        }

        var trimmed = directory.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }
}
