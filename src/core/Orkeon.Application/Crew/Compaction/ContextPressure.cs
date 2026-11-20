namespace Orkeon.Application.Crew.Compaction;

/// <summary>
/// How full a loop's conversation is before a model call (#432): the estimate a
/// <see cref="IContextPressureStage"/> reads, and that compaction compares to
/// <see cref="TriggerTokens"/>.
/// </summary>
public sealed record ContextPressure
{
    /// <summary>The task the loop works on.</summary>
    public required string TaskId { get; init; }

    /// <summary>The agent's role.</summary>
    public required string AgentRole { get; init; }

    /// <summary>The estimated prompt of the next call, in tokens.</summary>
    public required int EstimatedTokens { get; init; }

    /// <summary>The model's context window.</summary>
    public required ContextWindow Window { get; init; }

    /// <summary>The tokens held back for the call's output: its cap, at most half the window.</summary>
    public required int ReservedOutputTokens { get; init; }

    /// <summary>The estimate at which the loop compacts: the trigger share of the usable window.</summary>
    public required int TriggerTokens { get; init; }

    /// <summary>The usable window: the window less <see cref="ReservedOutputTokens"/>.</summary>
    public int BudgetTokens => Window.Tokens - ReservedOutputTokens;

    /// <summary>The share of <see cref="BudgetTokens"/> the estimate fills (above 1 past the window).</summary>
    public double FillShare => BudgetTokens <= 0 ? 1 : (double)EstimatedTokens / BudgetTokens;
}
