using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.FileSystem.Constants.Directory;
using Microsoft.Extensions.Logging;

namespace Orkeon.Tools.FileSystem;

// ── Request / Response records ────────────────────────────────────────
/// <summary>Request parameters for the directory_read tool.</summary>
public record DirectoryReadRequest
{
    /// <summary>The directory path to list.</summary>
    [FieldSchema(Description = "The directory path to list", Example = "/workspace/src")]
    public string Path { get; init; } = "";

    /// <summary>Whether to list contents recursively (default: false).</summary>
    [FieldSchema(Description = "Whether to list contents recursively (default: false)", IsRequired = false, Example = true)]
    public bool Recursive { get; init; } = false;

    /// <summary>Glob pattern to filter results (e.g., '*.cs', '*.txt').</summary>
    [FieldSchema(Description = "Glob pattern to filter results (e.g., '*.cs', '*.txt')", IsRequired = false, Example = "*.cs")]
    public string Pattern { get; init; } = "*";

    /// <summary>Maximum number of entries to return (default: 500). Prevents context window overflow on large directories.</summary>
    [FieldSchema(Description = "Maximum number of entries to return (default: 500). Use a smaller value to avoid overloading context windows.", IsRequired = false, Example = 500, Default = 500)]
    public int MaxResults { get; init; } = 500;
}

/// <summary>Represents a single file or directory entry returned by the directory_read tool.</summary>
public record DirectoryEntry
{
    /// <summary>File or directory name.</summary>
    [ReturnSchema(Description = "File or directory name", Example = "Program.cs")]
    public string Name { get; init; } = "";

    /// <summary>Full path to the file or directory.</summary>
    [ReturnSchema(Description = "Full path to the file or directory", Example = "/workspace/src/Program.cs")]
    public string Path { get; init; } = "";

    /// <summary>File size in bytes (null for directories).</summary>
    [ReturnSchema(Description = "File size in bytes (null for directories)", Example = 2048)]
    public long? Size { get; init; }

    /// <summary>Entry type: 'file' or 'directory'.</summary>
    [ReturnSchema(Description = "Entry type: 'file' or 'directory'", Example = "file")]
    public string Type { get; init; } = "";
}

/// <summary>Response returned by the directory_read tool.</summary>
public record DirectoryReadResponse
{
    /// <summary>The directory path that was listed.</summary>
    [ReturnSchema(Description = "The directory path that was listed", Example = "/workspace/src")]
    public string Path { get; init; } = "";

    /// <summary>List of files in the directory.</summary>
    [ReturnSchema(Description = "List of files in the directory")]
    public IReadOnlyList<DirectoryEntry> Files { get; init; } = [];

    /// <summary>List of subdirectories.</summary>
    [ReturnSchema(Description = "List of subdirectories")]
    public IReadOnlyList<DirectoryEntry> Directories { get; init; } = [];

    /// <summary>Total number of files found.</summary>
    [ReturnSchema(Description = "Total number of files found", Example = 12)]
    public int TotalFiles { get; init; }

    /// <summary>Total number of subdirectories found.</summary>
    [ReturnSchema(Description = "Total number of subdirectories found", Example = 3)]
    public int TotalDirectories { get; init; }

    /// <summary>Whether the results were truncated due to MaxResults limit.</summary>
    [ReturnSchema(Description = "Whether the results were truncated due to MaxResults limit", Example = false)]
    public bool Truncated { get; init; }

    /// <summary>Total entries found before truncation (only set when Truncated is true).</summary>
    [ReturnSchema(Description = "Total entries found before truncation (only set when Truncated is true)", Example = 19911)]
    public int? TotalEntriesBeforeTruncation { get; init; }

    /// <summary>Optional advisory message (e.g., when a RaggableTree index is available and codebase_map would be a better fit).</summary>
    [ReturnSchema(Description = "Optional advisory message shown when a richer structural tool is available.")]
    public string? Warning { get; init; }
}

/// <summary>
/// Tool for listing directory contents including files and subdirectories.
/// </summary>
[ToolContract("directory_read",
    Name = "directory_read",
    Description = "List directory contents including files and subdirectories. Supports recursive listing and glob pattern filtering.",
    Category = "File System")]
public partial class DirectoryReadTool : FileToolBase<DirectoryReadRequest, DirectoryReadResponse>
{
    /// <summary>Declared access class for permission gates.</summary>
    public override ToolAccess Access => ToolAccess.Read;

    private readonly IRaggableStore? _raggableStore;

    /// <summary>Initializes a new instance of <see cref="DirectoryReadTool"/> with an optional RaggableTree store for advisory warnings.</summary>
    public DirectoryReadTool(
        IFileSystemService fileSystemService,
        IPathValidator pathValidator,
        IRaggableStore? raggableStore = null,
        ILogger<DirectoryReadTool>? logger = null)
        : base(fileSystemService, pathValidator, logger)
    {
        _raggableStore = raggableStore;
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(DirectoryReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Path))
            return "Path cannot be empty";

        var pathValidation = ResolveVirtualPath(request.Path, FileAccessRights.Read);
        if (!pathValidation.IsAllowed)
            return pathValidation.DenialReason ?? "Invalid or unsafe directory path";

        // Store resolved path check happens in ExecuteTypedAsync
        return null;
    }

    /// <summary>Default maximum entries when not specified by caller.</summary>
    private const int DefaultMaxResults = 500;

    /// <summary>Absolute hard limit to prevent context window overflow regardless of caller request.</summary>
    private const int HardMaxResults = 2000;

    /// <inheritdoc />
    protected override Task<DirectoryReadResponse> ExecuteTypedAsync(
        DirectoryReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteCoreAsync();

        async Task<DirectoryReadResponse> ExecuteCoreAsync()
        {
            var pathValidation = ResolveVirtualPath(request.Path, FileAccessRights.Read);
            var resolvedPath = pathValidation.ResolvedPath!;
            var displayPath = _fileSystemService.ToVirtualPath(resolvedPath) ?? request.Path;

            var vPath = request.Path;

            await EnsureDirectoryExistsAsync(vPath, displayPath, cancellationToken).ConfigureAwait(false);

            var pattern = NormalizePattern(request.Pattern);
            var maxResults = ResolveMaxResults(request.MaxResults);

            var allEntries = await CollectEntriesAsync(request, vPath, pattern, cancellationToken)
                .ConfigureAwait(false);

            var totalRawEntries = allEntries.Count;
            var truncated = totalRawEntries > maxResults;

            var fileEntries = allEntries.Where(e => e.Kind == VirtualEntryKind.File).ToList();
            var dirEntries = allEntries.Where(e => e.Kind == VirtualEntryKind.Directory).ToList();

            // Distribute the budget: prioritize files, give remaining to directories
            var fileBudget = Math.Min(fileEntries.Count, maxResults);
            var dirBudget = Math.Min(dirEntries.Count, maxResults - fileBudget);

            var files = MapEntries(fileEntries, fileBudget, "file", includeSize: true);
            var directories = MapEntries(dirEntries, dirBudget, "directory", includeSize: false);

            if (truncated)
                LogTruncatedDirectory(displayPath, totalRawEntries, files.Count + directories.Count, maxResults);
            else
                LogListedDirectory(displayPath, files.Count, directories.Count);

            var warning = await ResolveWarningAsync(cancellationToken).ConfigureAwait(false);

            return new DirectoryReadResponse
            {
                Path = displayPath,
                Files = files,
                Directories = directories,
                TotalFiles = fileEntries.Count,
                TotalDirectories = dirEntries.Count,
                Truncated = truncated,
                TotalEntriesBeforeTruncation = truncated ? totalRawEntries : null,
                Warning = warning,
            };
        }
    }

    private async Task EnsureDirectoryExistsAsync(
        string vPath, string displayPath, CancellationToken cancellationToken)
    {
        var exists = await _fileSystemService.ExistsAsync(vPath, cancellationToken).ConfigureAwait(false);

        if (!exists)
            throw new DirectoryNotFoundException($"Directory not found: {displayPath}");
    }

    private static int ResolveMaxResults(int requested)
        => requested > 0 ? Math.Min(requested, HardMaxResults) : DefaultMaxResults;

    private async Task<List<VirtualFileEntry>> CollectEntriesAsync(
        DirectoryReadRequest request, string vPath, string pattern, CancellationToken cancellationToken)
    {
        var opts = new VirtualEnumerationOptions(Recursive: request.Recursive, SearchPattern: pattern);
        var entries = new List<VirtualFileEntry>();
        await foreach (var entry in _fileSystemService.EnumerateFilesAsync(vPath, opts, cancellationToken).ConfigureAwait(false))
            entries.Add(entry);
        return entries;
    }

    private static List<DirectoryEntry> MapEntries(
        List<VirtualFileEntry> entries, int budget, string type, bool includeSize)
        => entries
            .Take(budget)
            .Select(e => new DirectoryEntry
            {
                Name = System.IO.Path.GetFileName(e.VirtualPath),
                Path = e.VirtualPath,
                Size = includeSize ? e.SizeBytes : null,
                Type = type
            })
            .ToList();

    private async Task<string?> ResolveWarningAsync(CancellationToken cancellationToken)
    {
        if (_raggableStore is null)
            return null;

        var nodeCount = await _raggableStore.GetNodeCountAsync(cancellationToken).ConfigureAwait(false);
        return nodeCount > 0
            ? "A RaggableTree index is available. Prefer codebase_map for structural inventory."
            : null;
    }

    /// <summary>
    /// Normalizes glob patterns for VFS enumeration:
    /// - Strips <c>**/</c> prefixes (recursion is handled by <see cref="VirtualEnumerationOptions.Recursive"/>)
    /// - Falls back to <c>*</c> when only <c>**</c> remains
    /// </summary>
    private static string NormalizePattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return DirectoryReadDefaults.DefaultPattern;

        // Strip "**/" or "**\" prefixes — recursion is handled by VirtualEnumerationOptions.Recursive
        var normalized = pattern;
        while (normalized.StartsWith("**/", StringComparison.Ordinal) || normalized.StartsWith("**\\", StringComparison.Ordinal))
            normalized = normalized[3..];

        // If only "**" remains after stripping, match everything
        if (normalized is "**" or "")
            return "*";

        return normalized;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Listed directory: {Path} ({Files} files, {Dirs} directories)")]
    private partial void LogListedDirectory(string path, int files, int dirs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Directory listing truncated: {Path} had {TotalEntries} entries, returned {ReturnedEntries} (maxResults={MaxResults})")]
    private partial void LogTruncatedDirectory(string path, int totalEntries, int returnedEntries, int maxResults);
}
