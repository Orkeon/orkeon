namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Surface exposed to JS as <c>ctx.fileSystem</c> in an agent <c>body</c> (#378): the text the runtime writes
/// in the system prompt of a declarative turn, which a body places in the prompt it gives
/// <c>ctx.llm.act</c> itself — nothing is added to that prompt behind the script's back.
/// </summary>
#pragma warning disable IDE1006
public sealed class JsAgentFileSystem
{
    private readonly Func<string> _prompt;

    internal JsAgentFileSystem(Func<string> prompt) => _prompt = prompt;

    /// <summary>
    /// The <c>## File system</c> section of this agent in this run — its file-system scope, else the
    /// agent-facing mounts, sorted by virtual path, their rights, where relative paths resolve, the note on
    /// read-write paths — from its heading to its last line, with no line break around it. Read from the
    /// mounts at each access. Empty when no path is visible, when the agent has no file-system tool, or when
    /// it sets <c>announceMounts(false)</c>.
    /// </summary>
    public string prompt => _prompt();
}
#pragma warning restore IDE1006
