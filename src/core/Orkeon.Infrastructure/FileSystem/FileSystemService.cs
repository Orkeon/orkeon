using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.Infrastructure.FileSystem;

/// <summary>
/// Implementation of <see cref="IFileSystemService"/> that chains the domain
/// <see cref="FileSystemRegistry"/> with <see cref="IPathValidator"/> for defense-in-depth.
/// </summary>
public sealed partial class FileSystemService : IFileSystemService
{
    private readonly FileSystemRegistry _bootRegistry;
    private readonly IFileSystemScope? _scope;
    private readonly IPathValidator _pathValidator;
    private readonly ILogger<FileSystemService> _logger;

    /// <summary>Boot mount base paths, used to redact physical paths from error messages.</summary>
    private readonly IReadOnlyList<string> _basePaths;

    /// <summary>Initializes a new instance of <see cref="FileSystemService"/>.</summary>
    /// <param name="registry">The boot-time file system registry (default mounts).</param>
    /// <param name="pathValidator">The path validator.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="scope">
    /// Optional ambient scope override (P2-O-05). When an execution flow has entered a scoped
    /// registry, operations resolve against it; otherwise they use the boot registry (unchanged).
    /// </param>
    public FileSystemService(
        FileSystemRegistry registry,
        IPathValidator pathValidator,
        ILogger<FileSystemService> logger,
        IFileSystemScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(pathValidator);
        ArgumentNullException.ThrowIfNull(logger);

        _bootRegistry = registry;
        _scope = scope;
        _pathValidator = pathValidator;
        _logger = logger;

        _basePaths = CollectBasePaths(registry);
    }

    /// <summary>
    /// The registry active for the current operation: the ambient scope's registry when an execution
    /// flow has entered one, otherwise the boot registry. Read per operation so scoped mounts never
    /// leak across async flows and existing (no-scope) behavior is byte-identical.
    /// </summary>
    private FileSystemRegistry ActiveRegistry => _scope?.Current ?? _bootRegistry;

    // Collects mount base paths for redaction (normalize to full paths).
    // Uses GetAllMountsInternal to include internal mounts (e.g. sandbox) in redaction,
    // so physical paths are never leaked regardless of mount visibility.
    // Identity mounts (physical == virtual) are excluded: their "physical" path IS the
    // public virtual name, and redacting it strips the only actionable hint from denial
    // messages ("Available mounts: [REDACTED]"). Since ADR-008 this covers ONE case — the
    // container convention where a Unix mount point is spelled the same on both sides
    // (`/output:/output:rw`, see docker/orkeon-example). It no longer covers runner
    // auto-injection: the crew directory and the exchange-log directory are mounted under
    // names now, so both are redacted like any other physical path.
    private static List<string> CollectBasePaths(FileSystemRegistry registry)
    {
        return registry.GetAllMountsInternal()
            .Select(m => (m.VirtualPath, BasePath: GetBasePathFromRegistry(registry, m.VirtualPath)))
            .Where(x => x.BasePath is not null
                        && !string.Equals(x.BasePath, x.VirtualPath, StringComparison.Ordinal))
            .Select(x => x.BasePath!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public PathValidationResult ResolveAndValidate(string virtualPath, FileAccessRights requiredRight)
    {
        // Step 1: Resolve virtual path via the domain registry
        string physicalPath;
        try
        {
            physicalPath = ActiveRegistry.ResolveAndCheckRights(virtualPath, requiredRight);
        }
        catch (FileAccessDeniedException ex)
        {
            LogAccessDeniedByRegistry(virtualPath, requiredRight);
            var safeMessage = RedactPhysicalPaths(ex.Message);
            return PathValidationResult.Denied(safeMessage);
        }

        // Step 2: Defense-in-depth — run through IPathValidator
        var pathValidation = _pathValidator.ValidatePath(physicalPath);
        if (!pathValidation.IsAllowed)
        {
            LogAccessDeniedByPathValidator(virtualPath);
            var safeReason = RedactPhysicalPaths(pathValidation.DenialReason ?? "Path validation failed");
            return PathValidationResult.Denied(safeReason);
        }

        LogAccessGranted(virtualPath);
        return PathValidationResult.Allowed(physicalPath);
    }

    /// <inheritdoc />
    public string? ToVirtualPath(string physicalPath)
    {
        return ActiveRegistry.ToVirtualPath(physicalPath);
    }

    /// <inheritdoc />
    public IReadOnlyList<MountInfo> GetAvailableMounts()
    {
        return ActiveRegistry.GetAvailableMounts();
    }

    /// <summary>
    /// Replaces any occurrence of known physical base paths in <paramref name="message"/>
    /// with "[REDACTED]" so that physical paths are never leaked to callers.
    /// </summary>
    internal string RedactPhysicalPaths(string message)
    {
        // Redact boot mount paths plus, when an execution flow has entered a scoped registry, that
        // scope's mount paths too — so scoped physical paths are never leaked in denial messages.
        // Only runs on the (cold) access-denied path, so recomputing scope base paths is cheap.
        var scoped = _scope?.Current;
        var basePaths = scoped is not null && !ReferenceEquals(scoped, _bootRegistry)
            ? _basePaths.Concat(CollectBasePaths(scoped))
            : _basePaths;

        return basePaths.Aggregate(message, (current, basePath) =>
            current.Contains(basePath, StringComparison.Ordinal)
                ? current.Replace(basePath, "[REDACTED]", StringComparison.Ordinal)
                : current);
    }

    /// <summary>
    /// Attempts to extract the physical base path for a given virtual path by resolving
    /// the mount root with <see cref="FileAccessRights.Read"/>.
    /// Falls back to <c>null</c> if the mount root cannot be resolved.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort base-path discovery: any resolution/rights failure yields null (path redaction is simply skipped for that mount) and must not propagate.")]
    private static string? GetBasePathFromRegistry(FileSystemRegistry registry, string virtualPath)
    {
        try
        {
            // Resolve the mount root to discover the physical base path
            var physicalRoot = registry.ResolveAndCheckRights(virtualPath, FileAccessRights.Read);
            // The basePath is the directory of the resolved root (or the root itself)
            return Path.GetFullPath(physicalRoot);
        }
        catch
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Access denied by registry for virtual path '{VirtualPath}' requiring {RequiredRight}")]
    private partial void LogAccessDeniedByRegistry(string virtualPath, FileAccessRights requiredRight);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Access denied by path validator for virtual path '{VirtualPath}'")]
    private partial void LogAccessDeniedByPathValidator(string virtualPath);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Access granted for virtual path '{VirtualPath}'")]
    private partial void LogAccessGranted(string virtualPath);
}
