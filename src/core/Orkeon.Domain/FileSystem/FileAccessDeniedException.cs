namespace Orkeon.Domain.FileSystem;

/// <summary>Exception thrown when file access is denied by the virtual file system.</summary>
#pragma warning disable S3925 // BinaryFormatter serialization is obsolete in .NET 10; ISerializable pattern not required
public class FileAccessDeniedException : Exception
#pragma warning restore S3925
{
    /// <summary>Virtual path that was denied access.</summary>
    public string VirtualPath { get; }
    /// <summary>Access right that was required but not granted.</summary>
    public FileAccessRights? RequiredRight { get; }
    /// <summary>Alternative mount paths that may satisfy the request.</summary>
    public IReadOnlyList<string>? AlternativeMounts { get; }

    /// <summary>Initializes a new instance with path, right, and optional alternative mounts.</summary>
    public FileAccessDeniedException(
        string message,
        string virtualPath,
        FileAccessRights? requiredRight = null,
        IReadOnlyList<string>? alternativeMounts = null)
        : base(message)
    {
        VirtualPath = virtualPath;
        RequiredRight = requiredRight;
        AlternativeMounts = alternativeMounts;
    }

    /// <summary>Initializes a new instance wrapping an inner exception.</summary>
    public FileAccessDeniedException(string message, Exception innerException)
        : base(message, innerException)
    {
        VirtualPath = string.Empty;
    }

    /// <summary>Initializes a new instance of <see cref="FileAccessDeniedException"/>.</summary>
    public FileAccessDeniedException() { VirtualPath = string.Empty; }

    /// <summary>Initializes a new instance of <see cref="FileAccessDeniedException"/>.</summary>
    /// <param name="message">The exception message.</param>
    public FileAccessDeniedException(string message) : base(message) { VirtualPath = string.Empty; }
}
