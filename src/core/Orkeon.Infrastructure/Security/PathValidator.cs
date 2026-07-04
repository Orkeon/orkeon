using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.Infrastructure.Security;

/// <summary>
/// Validates file system paths against security rules to prevent directory traversal,
/// access to sensitive system files, and execution of dangerous file types.
/// </summary>
public partial class PathValidator : IPathValidator
{
    private readonly PathSecurityOptions _options;
    private readonly ILogger<PathValidator> _logger;

    /// <summary>
    /// Built-in blocked file extensions (dangerous executables and scripts).
    /// </summary>
    private static readonly HashSet<string> BuiltInBlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".ps1", ".sh", ".bash",
        ".vbs", ".wsf", ".msi", ".com", ".scr", ".pif"
    };

    /// <summary>
    /// Blocked system paths that should never be accessible.
    /// </summary>
    private static readonly string[] BlockedSystemPaths =
    [
        "/etc/passwd",
        "/etc/shadow",
        "/etc/hosts",
        "/root/.ssh"
    ];

    /// <summary>Initializes a new instance of <see cref="PathValidator"/>.</summary>
    /// <param name="options">The path security options.</param>
    /// <param name="logger">The logger.</param>
    public PathValidator(IOptions<PathSecurityOptions> options, ILogger<PathValidator> logger)
    {
        _options = options?.Value ?? new PathSecurityOptions();
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Constructor for testing without DI.
    /// </summary>
    public PathValidator(PathSecurityOptions options, ILogger<PathValidator> logger)
    {
        _options = options ?? new PathSecurityOptions();
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Fail-closed path validation: any failure normalizing the requested path or workspace root via Path.GetFullPath is logged and converted into a Denied result rather than throwing.")]
    public PathValidationResult ValidatePath(string requestedPath, string? workspaceRoot = null)
    {
        // Null/empty check
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            LogPathValidationDeniedNullOr();
            return PathValidationResult.Denied("Path cannot be null or empty");
        }

        // Very long path check
        if (requestedPath.Length > 4096)
        {
            LogPathValidationDeniedPathExceeds(requestedPath.Length);
            return PathValidationResult.Denied("Path exceeds maximum allowed length of 4096 characters");
        }

        string resolvedPath;
        try
        {
            resolvedPath = Path.GetFullPath(requestedPath);
        }
        catch (Exception ex)
        {
            LogPathValidationDeniedFailedTo(ex, requestedPath);
            return PathValidationResult.Denied($"Invalid path: {ex.Message}");
        }

        // Check blocked system paths
        var systemPathCheck = CheckBlockedSystemPaths(resolvedPath);
        if (!systemPathCheck.IsAllowed)
            return systemPathCheck;

        // Check blocked extensions
        var extensionCheck = CheckBlockedExtensions(resolvedPath);
        if (!extensionCheck.IsAllowed)
            return extensionCheck;

        // Resolve workspace root
        var effectiveWorkspaceRoot = workspaceRoot
            ?? _options.DefaultWorkspaceRoot
            ?? Directory.GetCurrentDirectory();

        string resolvedWorkspaceRoot;
        try
        {
            resolvedWorkspaceRoot = Path.GetFullPath(effectiveWorkspaceRoot);
        }
        catch (Exception ex)
        {
            LogPathValidationDeniedFailedTo2(ex, effectiveWorkspaceRoot);
            return PathValidationResult.Denied($"Invalid workspace root: {ex.Message}");
        }

        // Symlink resolution
        if (_options.ResolveSymlinks)
        {
            var symlinkCheck = ResolveAndCheckSymlink(ref resolvedPath, resolvedWorkspaceRoot);
            if (symlinkCheck != null && !symlinkCheck.IsAllowed)
                return symlinkCheck;
        }

        // Check if path is within workspace root or additional allowed directories
        if (!IsPathUnderAllowedDirectory(resolvedPath, resolvedWorkspaceRoot))
        {
            LogPathValidationDeniedIsOutside(resolvedPath, resolvedWorkspaceRoot);
            return PathValidationResult.Denied($"Path is outside the allowed workspace directory");
        }

        LogPathValidationAllowed(resolvedPath);
        return PathValidationResult.Allowed(resolvedPath);
    }

    private PathValidationResult CheckBlockedSystemPaths(string resolvedPath)
    {
        var normalizedPath = resolvedPath.Replace('\\', '/');
        foreach (var blockedPath in BlockedSystemPaths)
        {
            if (normalizedPath.Equals(blockedPath, StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.StartsWith(blockedPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                LogPathValidationDeniedMatchesBlocked(resolvedPath, blockedPath);
                return PathValidationResult.Denied($"Access to system path '{blockedPath}' is not allowed");
            }
        }
        return PathValidationResult.Allowed(resolvedPath);
    }

    private PathValidationResult CheckBlockedExtensions(string resolvedPath)
    {
        var extension = Path.GetExtension(resolvedPath);
        if (string.IsNullOrEmpty(extension))
            return PathValidationResult.Allowed(resolvedPath);

        if (BuiltInBlockedExtensions.Contains(extension))
        {
            LogPathValidationDeniedHasBlocked(resolvedPath, extension);
            return PathValidationResult.Denied($"File extension '{extension}' is not allowed");
        }

        if (_options.AdditionalBlockedExtensions.Contains(extension))
        {
            LogPathValidationDeniedHasAdditional(resolvedPath, extension);
            return PathValidationResult.Denied($"File extension '{extension}' is not allowed");
        }

        return PathValidationResult.Allowed(resolvedPath);
    }

    // NOTE (P4-VFS-40): VirtualFileEntry.LinkTarget (from P1-VFS-03, via IFileSystemService.TryGetEntryAsync)
    // already exposes symlink information as a virtual path. Callers that go through IFileSystemService
    // should use TryGetEntryAsync + entry.LinkTarget instead of this low-level sync check.
    // This method is kept synchronous because ValidatePath is called on hot paths where an async cascade
    // would require invasive changes across the tool layer (pragmatic trade-off).
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Symlink-resolution barrier: any I/O failure probing symlink targets (e.g. the file does not exist yet for a write) is logged and treated as 'no symlink redirection to check' — the path itself was already validated above.")]
    private PathValidationResult? ResolveAndCheckSymlink(ref string resolvedPath, string resolvedWorkspaceRoot)
    {
        try
        {
            var fileInfo = new FileInfo(resolvedPath);
            if (fileInfo.LinkTarget != null)
            {
                // Resolve the symlink target
                var symlinkTarget = Path.GetFullPath(fileInfo.LinkTarget,
                    Path.GetDirectoryName(resolvedPath) ?? resolvedPath);

                if (!IsPathUnderAllowedDirectory(symlinkTarget, resolvedWorkspaceRoot))
                {
                    LogPathValidationDeniedSymlinkPoints(resolvedPath, symlinkTarget);
                    return PathValidationResult.Denied("Symlink target is outside the allowed workspace directory");
                }

                resolvedPath = symlinkTarget;
            }

            // Also check directory symlinks in the path
            var dirInfo = new DirectoryInfo(Path.GetDirectoryName(resolvedPath) ?? resolvedPath);
            if (dirInfo.LinkTarget != null)
            {
                var dirTarget = Path.GetFullPath(dirInfo.LinkTarget,
                    dirInfo.Parent?.FullName ?? resolvedPath);

                var targetFilePath = Path.Combine(dirTarget, Path.GetFileName(resolvedPath));
                if (!IsPathUnderAllowedDirectory(targetFilePath, resolvedWorkspaceRoot))
                {
                    LogPathValidationDeniedDirectorySymlink();
                    return PathValidationResult.Denied("Directory symlink target is outside the allowed workspace directory");
                }

                resolvedPath = targetFilePath;
            }
        }
        catch (Exception ex)
        {
            LogCouldNotCheckSymlinksFor(ex, resolvedPath);
            // If the file doesn't exist yet (e.g., for write operations), we can't check symlinks
            // but the path itself has already been validated
        }

        return null;
    }

    private bool IsPathUnderAllowedDirectory(string resolvedPath, string resolvedWorkspaceRoot)
    {
        var comparison = GetPathComparison();

        // CRITICAL: Normalize with trailing separator to prevent /workspace-evil/ matching /workspace/
        if (IsPathUnderDirectory(resolvedPath, resolvedWorkspaceRoot, comparison))
            return true;

        // Check additional allowed directories
        foreach (var allowedDir in _options.AdditionalAllowedDirectories)
        {
            try
            {
                var resolvedAllowedDir = Path.GetFullPath(allowedDir);
                if (IsPathUnderDirectory(resolvedPath, resolvedAllowedDir, comparison))
                    return true;
            }
            catch (Exception ex) when (ex is ArgumentException or System.Security.SecurityException or PathTooLongException or NotSupportedException)
            {
                // Skip invalid additional directories
            }
        }

        return false;
    }

    private static bool IsPathUnderDirectory(string path, string directory, StringComparison comparison)
    {
        // Normalize directory with trailing separator
        var normalizedDir = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        // The path is under the directory if it starts with the normalized directory path
        // OR if it equals the directory itself (exact match)
        return path.StartsWith(normalizedDir, comparison) ||
               path.Equals(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), comparison);
    }

    private static StringComparison GetPathComparison()
    {
        // Windows is case-insensitive, Linux/Mac are case-sensitive
        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Path validation denied: null or empty path")]
    private partial void LogPathValidationDeniedNullOr();

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Path validation denied: path exceeds maximum length ({Length})")]
    private partial void LogPathValidationDeniedPathExceeds(int length);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Path validation denied: failed to resolve path '{Path}'")]
    private partial void LogPathValidationDeniedFailedTo(Exception ex, object path);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Path validation denied: failed to resolve workspace root '{Root}'")]
    private partial void LogPathValidationDeniedFailedTo2(Exception ex, object root);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Path validation denied: '{Path}' is outside workspace root '{Root}'")]
    private partial void LogPathValidationDeniedIsOutside(object path, object root);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Path validation allowed: '{Path}'")]
    private partial void LogPathValidationAllowed(object path);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Path validation denied: '{Path}' matches blocked system path '{Blocked}'")]
    private partial void LogPathValidationDeniedMatchesBlocked(object path, object blocked);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Path validation denied: '{Path}' has blocked extension '{Extension}'")]
    private partial void LogPathValidationDeniedHasBlocked(object path, object extension);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Path validation denied: '{Path}' has additional blocked extension '{Extension}'")]
    private partial void LogPathValidationDeniedHasAdditional(object path, object extension);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Path validation denied: symlink '{Path}' points outside workspace to '{Target}'")]
    private partial void LogPathValidationDeniedSymlinkPoints(object path, object target);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Path validation denied: directory symlink points outside workspace")]
    private partial void LogPathValidationDeniedDirectorySymlink();

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Could not check symlinks for '{Path}' (file may not exist yet)")]
    private partial void LogCouldNotCheckSymlinksFor(Exception ex, object path);

}
