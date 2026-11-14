namespace Orkeon.Domain.FileSystem;

/// <summary>
/// One entry of an agent's declared file-system scope (<c>filesystem:</c> of a YAML agent, #349): a
/// virtual path the agent may touch and, optionally, the rights it narrows the mount's to there.
/// </summary>
/// <remarks>
/// An entry only selects and narrows: the mount (and its sub-path overrides) stays the ceiling. Without
/// <see cref="Access"/>, the agent gets the mount's effective rights below <see cref="Path"/>; with it,
/// the intersection of the two. An entry is checked against the mounts when the run starts
/// (<see cref="AgentFileSystemScope.Resolve"/>).
/// </remarks>
/// <param name="Path">The absolute virtual path the entry covers, its subtree included.</param>
/// <param name="Access">The rights the entry narrows to; <see langword="null"/> inherits the mount's.</param>
public sealed record FileSystemScopeEntry(string Path, FileAccessRights? Access = null)
{
    /// <summary>
    /// Reads the <c>access</c> token of an entry: <c>ro</c>, <c>rw</c> or <c>rwnd</c>, the grammar of a mount
    /// string.
    /// </summary>
    /// <param name="access">The token.</param>
    /// <exception cref="FormatException">The token is none of the three.</exception>
    public static FileAccessRights ParseAccess(string access)
    {
        ArgumentNullException.ThrowIfNull(access);
        return access.Trim() switch
        {
            var t when t.Equals("ro", StringComparison.OrdinalIgnoreCase) => FileAccessRights.ReadOnly,
            var t when t.Equals("rw", StringComparison.OrdinalIgnoreCase) => FileAccessRights.ReadWrite,
            var t when t.Equals("rwnd", StringComparison.OrdinalIgnoreCase) => FileAccessRights.ReadWriteNoDelete,
            _ => throw new FormatException($"Unknown access value: '{access}'. Valid values: ro, rw, rwnd"),
        };
    }
}
