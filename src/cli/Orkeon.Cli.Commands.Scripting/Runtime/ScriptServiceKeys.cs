namespace Orkeon.Cli.Commands.Scripting.Runtime;

/// <summary>
/// Canonical names used by <see cref="ScriptServiceWhitelist"/> for services exposed
/// via <c>ctx.services.get(name)</c>. Centralised here so scripts and tests reference
/// constants instead of magic strings.
/// </summary>
public static class ScriptServiceKeys
{
    /// <summary>The <see cref="Orkeon.Domain.FileSystem.IFileSystemService"/> instance — virtual FS.</summary>
    public const string FileSystem = "fs";

    /// <summary>All registered <see cref="Orkeon.Domain.Tools.IBaseTool"/> instances as an array.</summary>
    public const string Tools = "tools";

    /// <summary>Optional: the host's default <see cref="Orkeon.Domain.SharedKernel.ILlmProvider"/>.</summary>
    public const string Llm = "llm";

    /// <summary>Optional: a script-scoped <see cref="Microsoft.Extensions.Logging.ILogger"/>.</summary>
    public const string Logger = "logger";

    /// <summary>The command-dispatch facade — <c>ctx.services.get("commands")</c>.</summary>
    public const string Commands = "commands";

    /// <summary>
    /// Optional: the session's tool-call rules — <c>ctx.services.get("permissions")</c>
    /// (<see cref="PermissionRulesFacade"/>). Present in the REPL, which keeps them.
    /// </summary>
    public const string Permissions = "permissions";

    /// <summary>
    /// Optional: the crew-launching facade — <c>ctx.services.get("script-host")</c>. Lets a
    /// <c>*.cmd.ts</c> run a <c>crew.ork.ts</c> by name (sync or async) and pass it an input.
    /// The host-side engine the control plane calls for long work.
    /// </summary>
    public const string ScriptHost = "script-host";

    /// <summary>
    /// Optional: the tool approvals waiting for the operator — <c>ctx.services.get("approvals")</c>
    /// (<see cref="ScriptPendingApprovals"/>). Read-only; present when the host watches them (the REPL).
    /// </summary>
    public const string Approvals = "approvals";

    /// <summary>
    /// Optional: the REPL's persisted session — <c>ctx.services.get("session")</c> (<see cref="SessionFacade"/>):
    /// <c>save()</c> now, <c>load(path)</c> another saved session. Present when the session is persisted per project.
    /// </summary>
    public const string Session = "session";
}
