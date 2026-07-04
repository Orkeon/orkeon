namespace Orkeon.Cli.Scripting.Runtime;

/// <summary>
/// Canonical names used by <see cref="ScriptServiceWhitelist"/> for services exposed
/// via <c>ctx.services.get(name)</c>. Centralised here so scripts and tests reference
/// constants instead of magic strings.
/// </summary>
public static class ScriptServiceKeys
{
    /// <summary>The <see cref="Orkeon.Domain.FileSystem.IFileSystemService"/> instance — virtual FS.</summary>
    public const string FileSystem = "fs";

    /// <summary>
    /// Reserved key for a host-supplied configuration view. NOT populated by
    /// <see cref="DefaultScriptServiceWhitelist"/> (R2.6 / SEC-009): the raw
    /// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> carries
    /// secrets (API keys, connection strings) and must never reach untrusted
    /// scripts. A host may bind this key to a <strong>filtered</strong> view of
    /// non-sensitive keys by calling <c>ScriptServiceWhitelist.Add</c> itself.
    /// </summary>
    public const string Configuration = "configuration";

    /// <summary>All registered <see cref="Orkeon.Domain.Tools.IBaseTool"/> instances as an array.</summary>
    public const string Tools = "tools";

    /// <summary>Optional: the host's default <see cref="Orkeon.Domain.SharedKernel.ILlmProvider"/>.</summary>
    public const string Llm = "llm";

    /// <summary>Optional: a script-scoped <see cref="Microsoft.Extensions.Logging.ILogger"/>.</summary>
    public const string Logger = "logger";

    /// <summary>The command-dispatch façade — <c>ctx.services.get("commands")</c> (design §4, §8 item 1).</summary>
    public const string Commands = "commands";

    /// <summary>
    /// Optional: the crew-launching façade — <c>ctx.services.get("script-host")</c>. Lets a
    /// <c>*.cmd.ts</c> run a <c>crew.ork.ts</c> by name (sync or async) and pass it an input.
    /// The host-side engine the control plane calls for long work (exp 07 SPEC §6).
    /// </summary>
    public const string ScriptHost = "script-host";
}
