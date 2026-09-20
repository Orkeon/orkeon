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
    MountVisibility Visibility = MountVisibility.AgentFacing)
{
    /// <summary>
    /// The settings entry's id when the mount came from one (VFS-90), for the hosts and Studio.
    /// Agents never see it: the agent-facing listing and the denial message deal in virtual
    /// paths alone (ADR-008).
    /// </summary>
    public Orkeon.Domain.Common.MountId? Id { get; init; }
}
