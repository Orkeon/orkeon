namespace Orkeon.Domain.Configuration;

/// <summary>
/// Types of configuration changes.
/// </summary>
public enum ChangeType
{
    /// <summary>
    /// A new configuration item was added.
    /// </summary>
    Added,

    /// <summary>
    /// An existing configuration item was modified.
    /// </summary>
    Modified,

    /// <summary>
    /// A configuration item was removed.
    /// </summary>
    Removed,

    /// <summary>
    /// No change occurred.
    /// </summary>
    None
}
