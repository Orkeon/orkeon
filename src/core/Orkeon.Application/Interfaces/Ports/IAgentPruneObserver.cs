namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Told when a loop's older read-tool results were judged for pruning (#434). The run protocol writes it as
/// <c>agent.pruned</c>; a host that registers none loses only that line, the pruning is logged either way.
/// </summary>
public interface IAgentPruneObserver
{
    /// <summary>The decision model was asked about a loop's older read-tool results, before a model call.</summary>
    /// <param name="notice">What was offered, and what became of it.</param>
    void Pruned(AgentPruneNotice notice);
}

/// <summary>One pruning of a loop's read-tool results (#434): the fields of <c>agent.pruned</c>.</summary>
public sealed record AgentPruneNotice
{
    /// <summary>The task the loop works on.</summary>
    public required string TaskId { get; init; }

    /// <summary>The agent's role.</summary>
    public required string AgentRole { get; init; }

    /// <summary>The estimated prompt before the pruning, in tokens.</summary>
    public required int EstimatedTokens { get; init; }

    /// <summary>The estimate from which results are judged, in tokens.</summary>
    public required int ThresholdTokens { get; init; }

    /// <summary>The results offered to the decision model.</summary>
    public required int Offered { get; init; }

    /// <summary>The results it answered for; the rest stay as they are and may be offered again.</summary>
    public required int Judged { get; init; }

    /// <summary>The results kept whole.</summary>
    public required int Kept { get; init; }

    /// <summary>The results cut to their first characters.</summary>
    public required int Cut { get; init; }

    /// <summary>The results removed.</summary>
    public required int Dropped { get; init; }

    /// <summary>The tokens the pruning freed, estimated at four characters a token.</summary>
    public required int FreedTokens { get; init; }
}
