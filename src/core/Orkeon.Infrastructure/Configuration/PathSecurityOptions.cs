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
