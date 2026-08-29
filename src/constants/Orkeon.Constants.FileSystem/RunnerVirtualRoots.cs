namespace Orkeon.Constants.FileSystem;

/// <summary>
/// The virtual roots a runner mounts on its own behalf.
/// <para>
/// Every one of these is a <b>name</b>, never a disk path. A runner needs the VFS to reach
/// directories it computed physically — the crew's own folder, the exchange-log folder — and
/// the tempting shortcut is to mount them 1:1 (<c>C:\x:C:\x:ro</c>) so the absolute path it
/// already holds resolves unchanged. That shortcut is what put absolute disk paths in front of
/// agents, through <c>list_mounts</c> and through every access-denied message, under a sentence
/// telling them absolute paths are not allowed. ADR-008 closes it: physical stays on the left of
/// the mount string, and the loader is handed the virtual spelling.
/// </para>
/// <para>
/// They live in a satellite (ADR-009) because the engine and the tooling must agree on them and
/// cannot reference each other. Studio predicts, in a launch preview, the roots the runner will
/// mount; it may not reference <c>Orkeon.Hosting</c> to read them, so it used to copy them and a
/// drift test pinned the copies pairwise. That test could not catch an omission: when
/// <see cref="Sandbox"/> joined the reserved roots the copy never gained it, three equalities
/// stayed true, and the Studio editor kept accepting a mount every runner refuses.
/// </para>
/// </summary>
public static class RunnerVirtualRoots
{
    /// <summary>
    /// The crew definition's own directory, read-only. A single-file crew is addressed as
    /// <c>/crew/&lt;file&gt;.yaml</c>, a multi-file crew directory as <c>/crew</c> itself.
    /// </summary>
    public const string Crew = "/crew";

    /// <summary>
    /// An Orkeon Scripting entry point's directory, read-only. Distinct from
    /// <see cref="Crew"/> because a script's relative imports resolve against it.
    /// </summary>
    public const string Script = "/script";

    /// <summary>
    /// Where <c>--llm-log</c> exchange files land. Registered as an <b>internal</b> mount
    /// (<c>Orkeon:FileSystem:InternalMounts</c>): the VFS must reach it, no agent has any
    /// business addressing it.
    /// </summary>
    public const string LlmLogs = "/llm-logs";

    /// <summary>
    /// Where the code sandboxes stage the snippets they run. Registered as an <b>internal</b>
    /// mount by <c>AddOrkeonFileSystem</c>, so it is present in every registry a runner builds
    /// — which is why it belongs in the reserved list: a user <c>--mount</c> claiming
    /// <c>/sandbox</c> used to reach the registry's duplicate-virtual-path check and surface as
    /// an unhandled exception out of a DI factory, instead of the one-line diagnostic the other
    /// reserved roots get.
    /// </summary>
    public const string Sandbox = "/sandbox";

    /// <summary>
    /// All four, for a caller that has to refuse a user mount claiming any of them. A set, not
    /// four comparisons: an omission is what the pairwise drift test could not see.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [Crew, Script, LlmLogs, Sandbox];
}
