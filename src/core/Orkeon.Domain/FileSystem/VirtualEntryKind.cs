namespace Orkeon.Domain.FileSystem;

/// <summary>Kind of entry returned by virtual file system enumeration.</summary>
public enum VirtualEntryKind
{
    /// <summary>Regular file.</summary>
    File,

    /// <summary>Directory.</summary>
    Directory,

    /// <summary>Symbolic link (file or directory). Not followed unless explicitly opted in.</summary>
    SymLink
}
