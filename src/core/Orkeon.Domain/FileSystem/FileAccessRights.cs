namespace Orkeon.Domain.FileSystem;

/// <summary>Flags defining file system access permissions.</summary>
[Flags]
public enum FileAccessRights
{
    /// <summary>No access.</summary>
    None   = 0,
    /// <summary>Permission to read files.</summary>
    Read   = 1,
    /// <summary>Permission to write to existing files.</summary>
    Write  = 2,
    /// <summary>Permission to create new files.</summary>
    Create = 4,
    /// <summary>Permission to delete files.</summary>
    Delete = 8,

    /// <summary>Read-only access (alias for <see cref="Read"/>).</summary>
    ReadOnly          = Read,
    /// <summary>Full read, write, create, and delete access.</summary>
    ReadWrite         = Read | Write | Create | Delete,
    /// <summary>Read, write, and create access without delete.</summary>
    ReadWriteNoDelete = Read | Write | Create
}
