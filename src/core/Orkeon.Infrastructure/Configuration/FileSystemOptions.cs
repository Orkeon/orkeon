using System.Collections.ObjectModel;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Configuration options for the virtual file system.
/// Bound from the "Orkeon:FileSystem" configuration section.
/// </summary>
public class FileSystemOptions
{
    /// <summary>
    /// Mount definitions in the format "physical:virtual:rights[;subpath:rights;...]".
    /// </summary>
    public Collection<string> Mounts { get; } = [];
}
