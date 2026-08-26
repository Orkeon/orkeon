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
    /// <b>Visibility, not isolation.</b> The mount stays resolvable, and
    /// <c>IFileSystemService</c> does not know who is calling — the exchange logger writes
    /// through the very API the agent tools use. An agent that knows the name can still
    /// address it. Put nothing here that an agent knowing its name must not read.
    /// </para>
    /// </summary>
    public Collection<string> InternalMounts { get; } = [];
}
