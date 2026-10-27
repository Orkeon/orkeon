using System.Collections.ObjectModel;
using Orkeon.Infrastructure.Constants.Security;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Configuration options for path security validation.
/// </summary>
public class PathSecurityOptions
{
    /// <summary>
    /// The default workspace root directory. If null, defaults to the current working directory.
    /// </summary>
    public string? DefaultWorkspaceRoot { get; set; }

    /// <summary>
    /// Additional file extensions to block beyond the built-in list.
    /// </summary>
    public HashSet<string> AdditionalBlockedExtensions { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a plain read of a built-in script extension (<c>.sh</c>, <c>.bash</c>, <c>.ps1</c>,
    /// <c>.bat</c>, <c>.cmd</c>, <c>.vbs</c>, <c>.wsf</c>) is refused too. Default: <see langword="false"/>:
    /// reading a script is not executing it, so a read passes while a write, a creation, a deletion
    /// or a command target on it is refused. Built-in binary extensions and
    /// <see cref="AdditionalBlockedExtensions"/> refuse reads whatever this says.
    /// </summary>
    public bool BlockReadsOfBlockedExtensions { get; set; }

    /// <summary>
    /// Whether to resolve symlinks and verify the target is within the workspace. Default: true.
    /// </summary>
    public bool ResolveSymlinks { get; set; } = true;

    /// <summary>
    /// Maximum allowed file size in bytes. Default: 50 MB.
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = SecurityDefaults.MaxFileSizeBytes;

    /// <summary>
    /// Additional directories that are allowed beyond the workspace root.
    /// </summary>
    public Collection<string> AdditionalAllowedDirectories { get; } = [];
}
