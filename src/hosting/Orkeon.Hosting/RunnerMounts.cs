namespace Orkeon.Hosting;

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
/// </summary>
public static class RunnerMounts
{
    /// <summary>
    /// The crew definition's own directory, read-only. A single-file crew is addressed as
    /// <c>/crew/&lt;file&gt;.yaml</c>, a multi-file crew directory as <c>/crew</c> itself.
    /// </summary>
    public const string CrewVirtualRoot = "/crew";

    /// <summary>
    /// An Orkéon Scripting entry point's directory, read-only. Distinct from
    /// <see cref="CrewVirtualRoot"/> because a script's relative imports resolve against it.
    /// </summary>
    public const string ScriptVirtualRoot = "/script";

    /// <summary>
    /// Where <c>--llm-log</c> exchange files land. Registered as an <b>internal</b> mount
    /// (<c>Orkeon:FileSystem:InternalMounts</c>): the VFS must reach it, no agent has any
    /// business addressing it.
    /// </summary>
    public const string LlmLogVirtualRoot = "/llm-logs";
}
