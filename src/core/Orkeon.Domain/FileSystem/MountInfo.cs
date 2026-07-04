namespace Orkeon.Domain.FileSystem;

/// <summary>Read-only snapshot of a file system mount configuration.</summary>
/// <param name="VirtualPath">Virtual path prefix for the mount.</param>
/// <param name="DefaultRights">Default access rights for the mount.</param>
/// <param name="Overrides">Sub-path access right overrides.</param>
/// <param name="Visibility">Mount visibility (AgentFacing or Internal).</param>
public sealed record MountInfo(
    string VirtualPath,
    FileAccessRights DefaultRights,
    IReadOnlyList<SubPathOverride> Overrides,
    MountVisibility Visibility = MountVisibility.AgentFacing);
