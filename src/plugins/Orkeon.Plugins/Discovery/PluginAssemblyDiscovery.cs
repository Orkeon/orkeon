using Orkeon.Domain.FileSystem;

namespace Orkeon.Plugins;

/// <summary>
/// VFS-based implementation of <see cref="IPluginAssemblyDiscovery"/>: enumerates the
/// plugin directory through <see cref="IFileSystemService"/> (virtual paths), reads each plugin
/// folder's <c>plugin.json</c> through it, and resolves the entry assembly the manifest names to its
/// physical path via <see cref="IFileSystemService.ResolveAndValidate"/> — the mount-aware,
/// rights-audited resolution mechanism required by the
/// <see cref="System.Runtime.Loader.AssemblyLoadContext"/> loading APIs.
/// </summary>
public sealed class PluginAssemblyDiscovery : IPluginAssemblyDiscovery
{
    /// <summary>File name of the manifest every plugin folder carries.</summary>
    public const string ManifestFileName = "plugin.json";

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

                // Flat files are not plugins: only a sub-folder can carry a manifest.
                if (entry.Kind == VirtualEntryKind.Directory
                    && await ResolvePluginFolderAsync(entry.VirtualPath, options, ct).ConfigureAwait(false) is { } candidate)
                {
                    candidates.Add(candidate);
                }
            }

            candidates.Sort(static (a, b) => string.CompareOrdinal(a.VirtualPath, b.VirtualPath));
            return candidates;
        }
    }

    /// <summary>
    /// Resolves one sub-folder of the plugin directory. A folder with a manifest yields its entry
    /// assembly and the parsed manifest; a folder whose manifest cannot be used (malformed, a required
    /// field missing, entry assembly absent, not matching <c>SearchPattern</c>, denied by the mount), or
    /// that holds a <c>.dll</c> but no manifest, yields a candidate carrying <see cref="PluginAssemblyCandidate.ManifestFailure"/>
    /// so the loader refuses it (and <c>ContinueOnError</c> records it). Any other folder is ignored.
    /// </summary>
    private async Task<PluginAssemblyCandidate?> ResolvePluginFolderAsync(
        string folder, OrkeonPluginsOptions options, CancellationToken ct)
    {
        var manifestPath = $"{folder}/{ManifestFileName}";
        var text = await _fileSystem.TryReadAllTextAsync(manifestPath, ct).ConfigureAwait(false);
        if (text is null)
            return await ResolveFolderWithoutManifestAsync(folder, ct).ConfigureAwait(false);

        PluginManifest manifest;
        try
        {
            manifest = PluginManifest.Parse(text);
            PluginManifestValidator.ValidateShape(manifest);
        }
        catch (PluginManifestException ex)
        {
            return Refused(manifestPath, $"The manifest '{manifestPath}' is not usable: {ex.Message}");
        }

        var entryName = manifest.EntryAssembly!;
        if (!IsCandidateFileName(entryName, options.SearchPattern))
        {
            return Refused(
                manifestPath,
                $"The entry assembly '{entryName}' named by '{manifestPath}' does not match the SearchPattern '{options.SearchPattern}' (and carry .dll).");
        }

        var entryPath = $"{folder}/{entryName}";
        if (!await _fileSystem.ExistsAsync(entryPath, ct).ConfigureAwait(false))
            return Refused(manifestPath, $"The entry assembly '{entryName}' named by '{manifestPath}' is not in the plugin folder.");

        return TryResolveCandidate(entryPath) is { } resolved
            ? resolved with { Manifest = manifest }
            : Refused(manifestPath, $"The entry assembly '{entryName}' named by '{manifestPath}' is denied by the mount rights.");
    }

    private async Task<PluginAssemblyCandidate?> ResolveFolderWithoutManifestAsync(
        string folder, CancellationToken ct)
    {
        var dlls = new List<string>();
        await foreach (var entry in _fileSystem
            .EnumerateFilesAsync(folder, new VirtualEnumerationOptions(Recursive: false), ct).ConfigureAwait(false))
        {
            if (entry.Kind == VirtualEntryKind.File
                && entry.VirtualPath.EndsWith(AssemblyExtension, StringComparison.OrdinalIgnoreCase))
            {
                dlls.Add(entry.VirtualPath);
            }
        }

        if (dlls.Count == 0)
            return null;

        dlls.Sort(StringComparer.Ordinal);
        return Refused(dlls[0], $"The plugin folder '{folder}' has no {ManifestFileName}: every plugin carries a manifest.");
    }

    private static PluginAssemblyCandidate Refused(string virtualPath, string failure) =>
        new()
        {
            VirtualPath = virtualPath,
            PhysicalPath = string.Empty,
            ManifestFailure = failure,
        };

    /// <summary>
    /// Resolves the candidate's physical path through the VFS. Returns <c>null</c> when the
    /// candidate is denied by the mount rights (the VFS policy wins).
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
