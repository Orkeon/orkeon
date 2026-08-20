using System.Text.Json.Serialization;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>The budget dimension that ran out, when one did.</summary>
internal enum ForgeBudgetDimension
{
    /// <summary>Refine/repair cycles.</summary>
    Iterations,

    /// <summary>LLM tokens across the whole session.</summary>
    Tokens,

    /// <summary>Wall-clock time across the whole session, resumes included.</summary>
    WallTime,
}

/// <summary>
/// The session's three-dimension budget (SPEC-ORKEON-FORGE §4.2), modelled on
/// <c>AgentExecutionBudget</c>: hard bounds, never advisory. Zero means unlimited for
/// tokens and wall time; iterations always have a bound. Consumption is persisted with
/// the session, so a resume keeps paying on the same meter — raising the budget is the
/// deliberate gesture that buys more cycles, not the resume itself.
/// </summary>
internal sealed class ForgeBudget
{
    /// <summary>Default refine/repair cycles per session.</summary>
    public const int DefaultMaxIterations = 3;

    /// <summary>Cycles allowed. Always bounded.</summary>
    [JsonPropertyName("maxIterations")]
    public int MaxIterations { get; init; } = DefaultMaxIterations;

    /// <summary>LLM tokens allowed; 0 = unlimited.</summary>
    [JsonPropertyName("maxTokens")]
    public long MaxTokens { get; init; }

    /// <summary>Wall-clock seconds allowed; 0 = unlimited.</summary>
    [JsonPropertyName("maxWallSeconds")]
    public long MaxWallSeconds { get; init; }

    /// <summary>Cycles consumed so far (the first pass counts as 1).</summary>
    [JsonPropertyName("consumedIterations")]
    public int ConsumedIterations { get; set; }

    /// <summary>Tokens consumed so far, across resumes.</summary>
    [JsonPropertyName("consumedTokens")]
    public long ConsumedTokens { get; set; }

    /// <summary>Wall-clock seconds consumed so far, across resumes.</summary>
    [JsonPropertyName("consumedWallSeconds")]
    public long ConsumedWallSeconds { get; set; }

    /// <summary>True while another refine/repair cycle may start.</summary>
    [JsonIgnore]
    public bool CanStartIteration => ConsumedIterations < MaxIterations;

    /// <summary>Registers the start of a cycle.</summary>
    public void RegisterIteration() => ConsumedIterations++;

    /// <summary>Adds LLM token consumption.</summary>
    public void RegisterTokens(long tokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tokens);
        ConsumedTokens += tokens;
    }

    /// <summary>Adds elapsed wall-clock time.</summary>
    public void RegisterWallTime(TimeSpan elapsed)
    {
        if (elapsed > TimeSpan.Zero)
            ConsumedWallSeconds += (long)elapsed.TotalSeconds;
    }

    /// <summary>
    /// The first exhausted dimension, or null while everything is within bounds.
    /// Iterations are only exhausted when a *new* cycle would be needed — the check
    /// belongs to the moment of looping back, and <see cref="CanStartIteration"/> is
    /// what the engine consults there; here it reports the standing overshoot.
    /// </summary>
    public ForgeBudgetDimension? ExhaustedDimension()
    {
        if (MaxTokens > 0 && ConsumedTokens >= MaxTokens)
            return ForgeBudgetDimension.Tokens;

        if (MaxWallSeconds > 0 && ConsumedWallSeconds >= MaxWallSeconds)
            return ForgeBudgetDimension.WallTime;

        return null;
    }

    /// <summary>The compact form the protocol's <c>cost.updated</c> and <c>session.started</c> carry.</summary>
    public object ToEventPayload() => new
    {
        maxIterations = MaxIterations,
        maxTokens = MaxTokens,
        maxWallSeconds = MaxWallSeconds,
        consumedIterations = ConsumedIterations,
        consumedTokens = ConsumedTokens,
        consumedWallSeconds = ConsumedWallSeconds,
    };
}
