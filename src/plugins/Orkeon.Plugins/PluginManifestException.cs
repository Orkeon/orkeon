namespace Orkeon.Plugins;

/// <summary>
/// Thrown when a plugin manifest is missing, malformed, out of range for this Orkeon release,
/// at odds with the plugin it describes, or when the plugin registers a tool it did not declare.
/// It is a <see cref="PluginLoadException"/>: <see cref="OrkeonPluginsOptions.ContinueOnError"/> covers it.
/// </summary>
#pragma warning disable S3925 // BinaryFormatter serialization is obsolete in .NET 10 (SYSLIB0051); ISerializable pattern not required
public sealed class PluginManifestException : PluginLoadException
#pragma warning restore S3925
{
    /// <summary>Creates an empty exception.</summary>
    public PluginManifestException()
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    public PluginManifestException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    public PluginManifestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception with a message and the virtual path of the plugin.</summary>
    public PluginManifestException(string message, string virtualPath)
        : base(message, virtualPath)
    {
    }

    /// <summary>Creates an exception with a message, the virtual path of the plugin and an inner exception.</summary>
    public PluginManifestException(string message, string virtualPath, Exception innerException)
        : base(message, virtualPath, innerException)
    {
    }
}
