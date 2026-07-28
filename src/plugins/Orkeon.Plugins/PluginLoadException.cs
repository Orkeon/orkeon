namespace Orkeon.Plugins;

/// <summary>
/// Thrown when a plugin assembly cannot be loaded, when its <see cref="IOrkeonPlugin"/>
/// implementations cannot be instantiated, or when a plugin fails while contributing
/// its services to the host container.
/// </summary>
/// <remarks>
/// Messages reference the assembly by its <em>virtual</em> path; physical paths are
/// confined to inner exceptions raised by the runtime.
/// </remarks>
#pragma warning disable S3925 // BinaryFormatter serialization is obsolete in .NET 10 (SYSLIB0051); ISerializable pattern not required
public sealed class PluginLoadException : Exception
#pragma warning restore S3925
{
    /// <summary>Creates an empty exception.</summary>
    public PluginLoadException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public PluginLoadException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    public PluginLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception with a message and the failing virtual path.</summary>
    public PluginLoadException(string message, string virtualPath)
        : base(message)
    {
        VirtualPath = virtualPath;
    }

    /// <summary>
    /// Creates an exception with a message, the failing virtual path, and an inner exception.
    /// </summary>
    public PluginLoadException(string message, string virtualPath, Exception innerException)
        : base(message, innerException)
    {
        VirtualPath = virtualPath;
    }

    /// <summary>
    /// Virtual path of the plugin assembly that failed to load, when known.
    /// </summary>
    public string? VirtualPath { get; }
}
