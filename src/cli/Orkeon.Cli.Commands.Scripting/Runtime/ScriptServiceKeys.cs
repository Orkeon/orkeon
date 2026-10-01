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
    /// Optional: the crew-launching facade — <c>ctx.services.get("script-host")</c>. Lets a
    /// <c>*.cmd.ts</c> run a <c>crew.ork.ts</c> by name (sync or async) and pass it an input.
    /// The host-side engine the control plane calls for long work.
    /// </summary>
    public const string ScriptHost = "script-host";
}
