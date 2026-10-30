using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Cli.Commands.Scripting.Dispatch;

/// <summary>
/// The LLM usage credited to one <see cref="CommandInstance"/>, kept split the way the session's report keeps it:
/// prompt, completion, the cache part of the prompt, the reasoning part of the completion, and the cost.
/// </summary>
public sealed class CommandInstanceUsage
{
    /// <summary>No call credited yet.</summary>
    public static CommandInstanceUsage Empty { get; } = new();

    /// <summary>Prompt (input) tokens.</summary>
    public long PromptTokens { get; init; }

    /// <summary>Completion (output) tokens.</summary>
    public long CompletionTokens { get; init; }

    /// <summary>Prompt tokens served from the provider's cache, a part of <see cref="PromptTokens"/>; null while no call reported them.</summary>
    public long? CacheHitTokens { get; init; }

    /// <summary>Prompt tokens written to the provider's cache, a part of <see cref="PromptTokens"/>; null while no call reported them.</summary>
    public long? CacheWriteTokens { get; init; }

    /// <summary>Completion tokens spent reasoning, a part of <see cref="CompletionTokens"/>; null while no call reported them.</summary>
    public long? ReasoningTokens { get; init; }

    /// <summary>The LLM calls credited.</summary>
    public int Calls { get; init; }

    /// <summary>The calls nobody priced, a part of <see cref="Calls"/>: <see cref="Cost"/> leaves them out.</summary>
    public int UnpricedCalls { get; init; }

    /// <summary>The cost of the priced calls; null while none of the calls was priced.</summary>
    public decimal? Cost { get; init; }

    /// <summary>This usage plus one call, <paramref name="cost"/> null when nobody priced it.</summary>
    public CommandInstanceUsage Add(CostUsageEvent usage, decimal? cost)
    {
        ArgumentNullException.ThrowIfNull(usage);
        return new CommandInstanceUsage
        {
            PromptTokens = PromptTokens + Math.Max(0, usage.PromptTokens),
            CompletionTokens = CompletionTokens + Math.Max(0, usage.CompletionTokens),
            CacheHitTokens = AddReported(CacheHitTokens, usage.CacheHitTokens),
            CacheWriteTokens = AddReported(CacheWriteTokens, usage.CacheWriteTokens),
            ReasoningTokens = AddReported(ReasoningTokens, usage.ReasoningTokens),
            Calls = Calls + 1,
            UnpricedCalls = cost is null ? UnpricedCalls + 1 : UnpricedCalls,
            Cost = cost is null ? Cost : (Cost ?? 0m) + cost.Value,
        };
    }

    /// <summary>A running count plus one call's: a call that reported nothing leaves it as it was, null included (#254).</summary>
    private static long? AddReported(long? total, long? count)
        => count is { } value ? (total ?? 0L) + Math.Max(0L, value) : total;
}
