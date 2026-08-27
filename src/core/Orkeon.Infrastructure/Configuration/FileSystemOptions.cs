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

    /// <summary>
    /// Infrastructure mounts, same grammar as <see cref="Mounts"/>, registered with
    /// <see cref="Orkeon.Domain.FileSystem.MountVisibility.Internal"/>: resolvable, but
    /// absent from <c>GetAvailableMounts()</c> and therefore from <c>list_mounts</c>, the
    /// agent prompt's mount table and access-denied messages. This is where a runner puts
    /// what it needs the VFS to reach but no agent has any business addressing — the LLM
    /// exchange log directory, for one. The mount-string grammar has no room for a
    /// visibility token, which is why this is a separate list rather than a fourth field.
    /// <para>
    /// <b>A boundary, not just a hiding place.</b> The <c>IFileSystemService</c> every
    /// tool holds refuses these mounts, and reports them exactly like a path that does not
    /// exist. Infrastructure that must reach one asks for
    /// <c>PrivilegedFileSystemAccess</c> by name. It used to be a hiding place — resolvable
    /// for anyone who typed the name, and the names are documented — so a single
    /// <c>file_read /llm-logs/…</c> handed an agent every prompt of the run.
    /// </para>
    /// </summary>
    public Collection<string> InternalMounts { get; } = [];
}
