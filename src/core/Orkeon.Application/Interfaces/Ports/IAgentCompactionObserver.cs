namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Told when a loop compacts its conversation, fails to, or reverts to the trim (#432). The run
/// protocol writes it as <c>agent.compacted</c>; a host that registers none loses only that line,
/// the compaction is logged either way.
/// </summary>
public interface IAgentCompactionObserver
{
    /// <summary>A compaction ran, failed, or opened the breaker.</summary>
    /// <param name="notice">What happened.</param>
    void Compacted(AgentCompactionNotice notice);
}

/// <summary>What one compaction did (#432): the fields of <c>agent.compacted</c>.</summary>
public sealed record AgentCompactionNotice
{
    /// <summary>The task the loop works on.</summary>
    public required string TaskId { get; init; }

    /// <summary>The agent's role.</summary>
    public required string AgentRole { get; init; }

    /// <summary>One of <see cref="AgentCompactionOutcomes"/>.</summary>
    public required string Outcome { get; init; }

    /// <summary>The estimated prompt before the compaction, in tokens.</summary>
    public required int TokensBefore { get; init; }

    /// <summary>The estimated prompt after it, in tokens (the same as before when nothing changed).</summary>
    public required int TokensAfter { get; init; }

    /// <summary>The context window, in tokens.</summary>
    public required int Window { get; init; }

    /// <summary>Where the window comes from: <c>profile</c>, <c>table</c> or <c>settings</c>.</summary>
    public required string WindowSource { get; init; }

    /// <summary>The messages kept verbatim: the system prompt, the task and the recent turns.</summary>
    public required int KeptMessages { get; init; }

    /// <summary>The tokens the summary call used, charged to the task.</summary>
    public required int SummaryTokens { get; init; }
}

/// <summary>The <c>outcome</c> of <c>agent.compacted</c>.</summary>
public static class AgentCompactionOutcomes
{
    /// <summary>The middle of the conversation was replaced by a summary.</summary>
    public const string Compacted = "compacted";

    /// <summary>The summary call failed, came back empty, or left the estimate at or over the trigger.</summary>
    public const string Failed = "failed";

    /// <summary>The third failure in a row: the loop reverts to the 40-message trim for the rest of the attempt.</summary>
    public const string Reverted = "reverted";
}
