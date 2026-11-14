using Orkeon.Domain.FileSystem;

namespace Orkeon.Application.Agent
{
    /// <summary>
    /// #348, #378: the <c>## File system</c> section of an agent's system prompt, the one text both shapes of
    /// agent read. The runtime appends it to the prompt it composes for a declarative turn; an agent body of
    /// <c>.ork.ts</c> reads it as <c>ctx.fileSystem.prompt</c> and places it in the prompt it writes itself.
    /// </summary>
    public static class AgentFileSystemPrompt
    {
        /// <summary>
        /// The virtual paths the agent may touch, so it never guesses one (<c>/</c>, <c>/tmp</c>) the file
        /// system refuses: its file-system scope when it declares one (#349), else the agent-facing mounts
        /// (internal ones are never listed, ADR-008). Sorted by virtual path, so the same mounts give the same
        /// text and the prompt cache holds across runs.
        /// </summary>
        /// <param name="mounts">The agent-facing mounts of the run.</param>
        /// <param name="scope">The agent's file-system scope, or <see langword="null"/>.</param>
        /// <param name="workingDirectory">Where relative paths resolve, or <see langword="null"/>.</param>
        /// <param name="isRoot">Whether <paramref name="workingDirectory"/> is the agent's working root.</param>
        /// <returns>
        /// The section from its <c>## File system</c> heading to its last line, with no line break before or
        /// after it; an empty string when no path is visible.
        /// </returns>
        public static string Render(
            IReadOnlyList<MountInfo> mounts, AgentFileSystemScope? scope, string? workingDirectory, bool isRoot)
        {
            ArgumentNullException.ThrowIfNull(mounts);
            var visible = (scope is null ? mounts : scope.Narrow(mounts))
                .OrderBy(m => m.VirtualPath, StringComparer.Ordinal)
                .ToList();
            if (visible.Count == 0)
                return string.Empty;

            var lines = new List<string>
            {
                "## File system",
                string.Empty,
                "Only these virtual paths exist; any other path is refused:",
            };
            foreach (var mount in visible)
            {
                lines.Add(FormattableString.Invariant($"- {mount.VirtualPath} ({DescribeRights(mount.DefaultRights)})"));
                foreach (var subPath in mount.Overrides.OrderBy(o => o.RelativePath, StringComparer.Ordinal))
                    lines.Add(FormattableString.Invariant(
                        $"  - {mount.VirtualPath.TrimEnd('/')}/{subPath.RelativePath.Trim('/')} ({DescribeRights(subPath.Rights)})"));
            }

            // A scoped run whose working directory lies outside its scope is told so: a relative path would
            // resolve where it is refused (a working root always lies inside, AgentWorkingRoot.ResolveScope).
            if (!string.IsNullOrWhiteSpace(workingDirectory))
                lines.Add(isRoot
                    ? FormattableString.Invariant($"Your working root is {workingDirectory}: relative paths resolve there.")
                    : scope is not null && scope.RightsAt(workingDirectory) == FileAccessRights.None
                        ? FormattableString.Invariant(
                            $"Relative paths resolve against {workingDirectory}, outside your file-system scope: write every path in full, under one of the paths above.")
                        : FormattableString.Invariant($"Relative paths resolve against {workingDirectory}."));
            if (visible.Any(m => m.DefaultRights.HasFlag(FileAccessRights.Write)
                                 || m.Overrides.Any(o => o.Rights.HasFlag(FileAccessRights.Write))))
                lines.Add("Write intermediate notes under a read-write path.");

            return string.Join(Environment.NewLine, lines);
        }

        private static string DescribeRights(FileAccessRights rights) => rights switch
        {
            FileAccessRights.ReadOnly => "read-only",
            FileAccessRights.ReadWrite => "read-write",
            FileAccessRights.ReadWriteNoDelete => "read-write, no delete",
            FileAccessRights.None => "no access",
#pragma warning disable CA1308 // the enum's words, lowered for the prompt, not a normalisation for a comparison
            _ => rights.ToString().ToLowerInvariant(),
#pragma warning restore CA1308
        };
    }
}
