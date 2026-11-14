using Orkeon.Domain.FileSystem;

namespace Orkeon.Application.Agent
{
    /// <summary>
    /// RT-06: the check every agent working root passes before a run starts, shared by the declared agents
    /// (<see cref="AgentExecutionService"/>) and the script agents of <c>.ork.ts</c>.
    /// </summary>
    public static class AgentWorkingRoot
    {
        /// <summary>
        /// A working root is an intersection with the mounts, so one under no mount, relative or holding a
        /// <c>..</c> segment never starts a run. Returns the root normalised once (no <c>//</c>, <c>.</c> or
        /// trailing <c>/</c>), or <c>null</c> when <paramref name="root"/> is <c>null</c>.
        /// </summary>
        /// <param name="root">The declared root, or <c>null</c> for an unrooted run.</param>
        /// <param name="mounts">The mounts of the file system the root must sit under; none refuses every root.</param>
        /// <exception cref="InvalidOperationException">The root is not under any mount of the file system.</exception>
        public static string? NormalizeMounted(string? root, IReadOnlyList<MountInfo> mounts)
        {
            if (root is null)
                return null;

            var segments = root.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Where(segment => segment != ".").ToList();
            var normalized = "/" + string.Join('/', segments);
            ArgumentNullException.ThrowIfNull(mounts);
            var mounted = root.TrimStart().StartsWith('/') && !segments.Contains("..")
                && mounts.Any(m =>
                {
                    var mount = m.VirtualPath.Replace('\\', '/').TrimEnd('/');
                    return normalized.Equals(mount, StringComparison.Ordinal) || normalized.StartsWith(mount + "/", StringComparison.Ordinal);
                });
            if (!mounted)
                throw new InvalidOperationException($"The agent working root '{root}' is not under any mount of the file system.");
            return normalized;
        }

        /// <summary>
        /// #349: the file-system scope an agent run starts with. Its declared entries are checked against
        /// the mounts and bounded by <paramref name="parent"/>; without entries, the run inherits
        /// <paramref name="parent"/> as it is (<see langword="null"/> when there is none, and the mounts
        /// alone decide). A working root must lie inside the scope: the agent could not work there.
        /// </summary>
        /// <param name="agentName">The agent, named by the refusals.</param>
        /// <param name="declared">The entries the agent declares; empty when it declares none.</param>
        /// <param name="mounts">The agent-facing mounts of the run.</param>
        /// <param name="parent">The scope of the run that started this one, or <see langword="null"/>.</param>
        /// <param name="root">The run's working root, normalised by <see cref="NormalizeMounted"/>, or <see langword="null"/>.</param>
        /// <exception cref="InvalidOperationException">An entry is refused, or the root lies outside the scope.</exception>
        public static AgentFileSystemScope? ResolveScope(
            string agentName, IReadOnlyList<FileSystemScopeEntry> declared, IReadOnlyList<MountInfo> mounts,
            AgentFileSystemScope? parent, string? root)
        {
            ArgumentNullException.ThrowIfNull(declared);
            var scope = declared.Count > 0 ? AgentFileSystemScope.Resolve(agentName, declared, mounts, parent) : parent;
            if (scope is not null && root is not null && scope.RightsAt(root) == FileAccessRights.None)
                throw new InvalidOperationException(
                    $"The agent working root '{root}' of agent '{agentName}' lies outside its file-system scope: {scope.Describe()}.");
            return scope;
        }
    }
}
