namespace Orkeon.Domain.FileSystem;

/// <summary>Controls whether a mount is visible to agents via GetAvailableMounts().</summary>
public enum MountVisibility
{
    /// <summary>Listed by GetAvailableMounts() — default, visible to agents/tools.</summary>
    AgentFacing = 0,

    /// <summary>Hidden from agents. Accessible only via direct IFileSystemService injection.</summary>
    Internal = 1
}
