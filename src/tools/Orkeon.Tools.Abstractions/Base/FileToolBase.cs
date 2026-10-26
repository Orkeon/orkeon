using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Security;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Tools.Abstractions.Base;

/// <summary>
/// Base class for tools that operate on files.
/// </summary>
public abstract partial class FileToolBase : ToolBase
{
    /// <summary>Optional path validator for SSRF/traversal protection (defense in depth on top of the VFS).</summary>
    private readonly IPathValidator? _pathValidator;

    /// <summary>Virtual file system service for path resolution and access control. Always injected (required).</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1051", Justification = "Established protected base-class field referenced directly by derived file tools across multiple projects; converting to a property would break the inherited contract without behavioral benefit.")]
    protected readonly IFileSystemService _fileSystemService;

    /// <inheritdoc />
    public override string Category => "File Operations";

    /// <summary>
    /// Initializes a new instance of <see cref="FileToolBase"/> with virtual file system support.
    /// </summary>
    /// <param name="fileSystemService">Virtual file system service for path resolution. Required.</param>
    /// <param name="logger">Optional logger.</param>
    protected FileToolBase(IFileSystemService fileSystemService, ILogger? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystemService);
        _fileSystemService = fileSystemService;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="FileToolBase"/> with both virtual file system and path validation support.
    /// </summary>
    /// <param name="fileSystemService">Virtual file system service for path resolution. Required.</param>
    /// <param name="pathValidator">Path validator for SSRF/traversal protection (defense in depth).</param>
    /// <param name="logger">Optional logger.</param>
    protected FileToolBase(IFileSystemService fileSystemService, IPathValidator pathValidator, ILogger? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystemService);
        _fileSystemService = fileSystemService;
        _pathValidator = pathValidator;
    }

    /// <summary>
    /// The absolute virtual spelling of a path a caller wrote: a relative path joined to the
    /// working directory (<see cref="WorkingDirectoryResolver.Absolutize"/>), an absolute one
    /// unchanged. A tool addresses the file system and echoes the path with this spelling.
    /// </summary>
    /// <param name="path">The path as written in the request.</param>
    protected string AbsolutizePath(string path) => WorkingDirectoryResolver.Absolutize(path, _fileSystemService);

    /// <summary>
    /// Resolves a virtual path to a physical path and validates access rights via the virtual file system.
    /// </summary>
    /// <param name="path">
    /// The path to resolve: absolute (e.g. "/workspace/src/file.cs"), or relative to the working
    /// directory (e.g. "src/file.cs", resolved by <see cref="AbsolutizePath"/>).
    /// </param>
    /// <param name="requiredRight">The minimum access right required for the operation.</param>
    /// <returns>A <see cref="PathValidationResult"/> containing the resolved physical path or a denial reason.</returns>
    /// <remarks>
    /// An empty path is refused with a message naming the argument
    /// (<see cref="WorkingDirectoryResolver.PathRequiredMessage"/>). A relative path is made
    /// absolute first, so the VFS and <see cref="AgentRootGuard"/> both judge the path the tool
    /// will touch. The VFS performs the authoritative mount + rights validation. When an optional
    /// <see cref="IPathValidator"/> was supplied it is additionally applied to the resolved
    /// physical path as defense in depth (traversal/SSRF).
    /// </remarks>
    protected PathValidationResult ResolveVirtualPath(string path, FileAccessRights requiredRight)
    {
        if (string.IsNullOrWhiteSpace(path))
            return PathValidationResult.Denied(WorkingDirectoryResolver.PathRequiredMessage);

        var virtualPath = AbsolutizePath(path);
        var result = _fileSystemService.ResolveAndValidate(virtualPath, requiredRight);
        if (result.IsAllowed && AgentRootGuard.Check(_fileSystemService, virtualPath) is { } outsideRoot)
            return outsideRoot;
        if (result.IsAllowed && _pathValidator is not null && result.ResolvedPath is not null)
        {
            var defenseInDepth = _pathValidator.ValidatePath(result.ResolvedPath);
            if (!defenseInDepth.IsAllowed)
                return defenseInDepth;
        }
        return result;
    }

    /// <summary>
    /// Ensures a directory exists using the virtual file system.
    /// </summary>
    protected System.Threading.Tasks.Task EnsureDirectoryExistsAsync(string virtualFilePath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(virtualFilePath);

        return EnsureDirectoryExistsCoreAsync();

        async System.Threading.Tasks.Task EnsureDirectoryExistsCoreAsync()
        {
            // Virtual paths always use '/', never System.IO.Path.DirectorySeparatorChar.
            // Path.GetDirectoryName normalizes '/' to '\' on Windows, which would desynchronize
            // the directory entry from the path the VFS was queried with.
            var lastSlash = virtualFilePath.LastIndexOf('/');
            if (lastSlash <= 0) return;
            var dir = virtualFilePath[..lastSlash];
            await _fileSystemService.CreateDirectoryAsync(dir, ct).ConfigureAwait(false);
            LogDirectoryCreated(dir);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Created directory: {Directory}")]
    private partial void LogDirectoryCreated(string directory);
}
