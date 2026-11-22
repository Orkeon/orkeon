using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Interop.AgentCli;

/// <summary>
/// The <c>Llm:AgentCli</c> section: how the <c>agent-cli</c> provider starts the agent CLI that answers
/// a section naming it (<c>"Provider": "agent-cli"</c>, ADR-020). Every profile on the provider shares it.
/// </summary>
[Experimental(AgentCliDiagnostics.ExperimentalId, UrlFormat = AgentCliDiagnostics.ExperimentalUrl)]
public sealed class AgentCliOptions
{
    /// <summary>The configuration section, beside the <c>Llm</c> keys it extends.</summary>
    public const string SectionName = "Llm:AgentCli";

    /// <summary>
    /// The engine that answers: the CLI whose command line and stream the provider speaks. <c>claude</c>
    /// (Claude Code, <c>claude -p</c> in stream-json) is the only one.
    /// </summary>
    public string Engine { get; set; } = AgentCliEngines.Claude;

    /// <summary>
    /// The CLI's executable: a name looked up on the <c>PATH</c>, or a full path. <c>claude</c> by default.
    /// It runs on its own login (<c>claude /login</c>): the API key variables of the host are removed from
    /// its environment.
    /// </summary>
    public string Command { get; set; } = "claude";

    /// <summary>
    /// How many CLI processes may live at once across the host, 4 by default. Never a wait: past it, an
    /// idle session is closed first, and when none is idle the new one starts with a warning.
    /// </summary>
    public int MaxProcesses { get; set; } = 4;

    /// <summary>
    /// The longest tool result handed to the CLI, in characters, 100000 by default: a longer one is cut
    /// with a note saying so. The CLI's own limit is raised above it, so that a result is never replaced by
    /// a file the model cannot read.
    /// </summary>
    public int MaxToolResultCharacters { get; set; } = 100_000;

    /// <summary>
    /// The context size at which the CLI would compact the conversation itself (<c>--autocompact</c>),
    /// 100000000 by default — above every window, since Orkeon keeps the history and compacts it first.
    /// The CLI refuses less than 100000.
    /// </summary>
    public long AutocompactTokens { get; set; } = 100_000_000;

    /// <summary>
    /// Whether a CLI that answers on an API key rather than its login is refused, true by default: the
    /// provider exists to run on the subscription the CLI is signed in to.
    /// </summary>
    public bool RequireLogin { get; set; } = true;
}

/// <summary>The engines <see cref="AgentCliOptions.Engine"/> takes.</summary>
[Experimental(AgentCliDiagnostics.ExperimentalId, UrlFormat = AgentCliDiagnostics.ExperimentalUrl)]
public static class AgentCliEngines
{
    /// <summary>Claude Code's <c>claude -p</c>, in stream-json.</summary>
    public const string Claude = "claude";
}

/// <summary>The identifiers this experimental package reports under.</summary>
public static class AgentCliDiagnostics
{
    /// <summary>The experimental diagnostic of the agent CLI provider.</summary>
    public const string ExperimentalId = "ORKEXP008";

    /// <summary>Where the experimental diagnostics are described.</summary>
    public const string ExperimentalUrl = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md";

    /// <summary>The key a section names the provider with (<c>"Provider": "agent-cli"</c>).</summary>
    public const string ProviderKey = "agent-cli";
}
