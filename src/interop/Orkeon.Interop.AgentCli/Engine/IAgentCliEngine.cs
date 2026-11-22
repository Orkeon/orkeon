using System.Text.Json.Nodes;

namespace Orkeon.Interop.AgentCli.Engine;

/// <summary>
/// What the provider needs to know of one agent CLI: how to start it, how to read its stream into
/// neutral events, and how to write what it reads. The first and only engine is
/// <see cref="ClaudeCodeEngine"/>.
/// </summary>
internal interface IAgentCliEngine
{
    /// <summary>The engine's name, as <see cref="AgentCliOptions.Engine"/> spells it.</summary>
    string Name { get; }

    /// <summary>The prefix the CLI gives the bridge's tools (<c>mcp__orkeon__</c>), stripped from the calls Orkeon reads.</summary>
    string ToolPrefix { get; }

    /// <summary>The command line and environment of one session's process.</summary>
    AgentCliCommandLine BuildCommandLine(AgentCliLaunch launch);

    /// <summary>Reads one line of the CLI's standard output; null for a line that is not an event.</summary>
    AgentCliEvent? ReadEvent(string line);

    /// <summary>The line that hands the CLI a user message: it opens a turn, or joins the turn under way.</summary>
    string UserLine(string text);

    /// <summary>The line that interrupts the turn under way.</summary>
    string InterruptLine(string requestId);
}

/// <summary>What one session's process is started with.</summary>
/// <param name="SystemPromptPath">The file holding the system prompt, which replaces the CLI's own.</param>
/// <param name="McpConfigJson">The <c>--mcp-config</c> document that declares the bridge.</param>
/// <param name="Model">The model to answer on; null for the CLI's own.</param>
/// <param name="WorkingDirectory">The neutral folder the process runs in.</param>
internal sealed record AgentCliLaunch(string SystemPromptPath, string McpConfigJson, string? Model, string WorkingDirectory);

/// <summary>A process to start: the executable, its arguments, the variables to set and those to remove.</summary>
internal sealed record AgentCliCommandLine(
    string FileName,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<string> RemovedEnvironment);

/// <summary>The kinds of event the provider reads from a CLI's stream.</summary>
internal enum AgentCliEventKind
{
    /// <summary>The turn starts: the session id, the model, the credential the CLI runs on.</summary>
    Init,

    /// <summary>One content block of an assistant message (the CLI writes one line per block).</summary>
    AssistantBlock,

    /// <summary>The final usage and stop reason of the message under way.</summary>
    MessageDelta,

    /// <summary>The message under way is complete.</summary>
    MessageStop,

    /// <summary>The turn is over, on an answer or an error.</summary>
    Result,

    /// <summary>The subscription's limits after the turn.</summary>
    RateLimit,

    /// <summary>The CLI compacted the conversation on its own.</summary>
    CompactBoundary,
}

/// <summary>One event of a CLI's stream, read by its engine.</summary>
internal sealed record AgentCliEvent
{
    public required AgentCliEventKind Kind { get; init; }

    /// <summary>The message id, for <see cref="AgentCliEventKind.AssistantBlock"/>.</summary>
    public string? MessageId { get; init; }

    /// <summary>The model that answered, for <see cref="AgentCliEventKind.AssistantBlock"/> and <see cref="AgentCliEventKind.Init"/>.</summary>
    public string? Model { get; init; }

    /// <summary>The content blocks of the line, for <see cref="AgentCliEventKind.AssistantBlock"/>.</summary>
    public JsonArray? Blocks { get; init; }

    /// <summary>The usage object, for <see cref="AgentCliEventKind.MessageDelta"/>, <see cref="AgentCliEventKind.AssistantBlock"/> and <see cref="AgentCliEventKind.Result"/>.</summary>
    public JsonObject? Usage { get; init; }

    /// <summary>The stop reason, for <see cref="AgentCliEventKind.MessageDelta"/>.</summary>
    public string? StopReason { get; init; }

    /// <summary>The credential the CLI runs on (<c>none</c> for its login), for <see cref="AgentCliEventKind.Init"/>.</summary>
    public string? ApiKeySource { get; init; }

    /// <summary>The CLI's version, for <see cref="AgentCliEventKind.Init"/>.</summary>
    public string? Version { get; init; }

    /// <summary>Whether the turn failed, for <see cref="AgentCliEventKind.Result"/>.</summary>
    public bool IsError { get; init; }

    /// <summary>The HTTP status of an API failure, for <see cref="AgentCliEventKind.Result"/>.</summary>
    public int? ApiErrorStatus { get; init; }

    /// <summary>The result's subtype and terminal reason, for <see cref="AgentCliEventKind.Result"/>.</summary>
    public string? Subtype { get; init; }

    /// <summary>The text of the result, or of its errors, for <see cref="AgentCliEventKind.Result"/>.</summary>
    public string? Text { get; init; }

    /// <summary>The CLI's notional price of the turn, for <see cref="AgentCliEventKind.Result"/>; never billed.</summary>
    public decimal? NotionalCostUsd { get; init; }

    /// <summary>The limit's status (<c>allowed</c>, <c>allowed_warning</c>, <c>rejected</c>), for <see cref="AgentCliEventKind.RateLimit"/>.</summary>
    public string? RateLimitStatus { get; init; }

    /// <summary>When the limit resets, for <see cref="AgentCliEventKind.RateLimit"/>.</summary>
    public DateTimeOffset? ResetsAt { get; init; }

    /// <summary>Which limit, for <see cref="AgentCliEventKind.RateLimit"/>.</summary>
    public string? RateLimitType { get; init; }
}
