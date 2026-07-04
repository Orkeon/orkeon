using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Crew.ValueObjects;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// Thread-safe accumulator for token telemetry collected from <see cref="TaskResult"/>s
/// during crew execution (R10.8 / MAT-004). Every process strategy records each task
/// result into a tally and writes the measured sums into the crew metadata under the
/// canonical keys (<see cref="CrewMetadata.TotalTokensKey"/>,
/// <see cref="CrewMetadata.PromptTokensKey"/>, <see cref="CrewMetadata.CompletionTokensKey"/>)
/// — the same channel <c>SequentialProcessStrategy</c> historically used — so that
/// orchestrators can rebuild a real token usage instead of fabricating zeros.
/// </summary>
internal sealed class TokenUsageTally
{
    private int _totalTokens;
    private int _promptTokens;
    private int _completionTokens;

    /// <summary>Gets the accumulated total token count.</summary>
    public int TotalTokens => Volatile.Read(ref _totalTokens);

    /// <summary>Gets the accumulated prompt-side token count (0 when no provider reported the split).</summary>
    public int PromptTokens => Volatile.Read(ref _promptTokens);

    /// <summary>Gets the accumulated completion-side token count (0 when no provider reported the split).</summary>
    public int CompletionTokens => Volatile.Read(ref _completionTokens);

    /// <summary>
    /// Records the token telemetry of a single task execution. Safe to call concurrently
    /// (parallel strategies, A2A channel handlers).
    /// </summary>
    /// <param name="result">The task result whose token counters are accumulated. Null is ignored.</param>
    public void Record(TaskResult? result)
    {
        if (result is null)
            return;

        if (result.TokensUsed > 0)
            Interlocked.Add(ref _totalTokens, result.TokensUsed);
        if (result.PromptTokens > 0)
            Interlocked.Add(ref _promptTokens, result.PromptTokens);
        if (result.CompletionTokens > 0)
            Interlocked.Add(ref _completionTokens, result.CompletionTokens);
    }

    /// <summary>
    /// Writes the accumulated telemetry into a crew metadata builder using the canonical
    /// keys. The prompt/completion split is only written when at least one provider
    /// reported it, so a missing split is never presented as a measured zero.
    /// </summary>
    /// <param name="builder">The metadata builder to write into.</param>
    /// <returns>The same builder for chaining.</returns>
    public CrewMetadata.Builder WriteTo(CrewMetadata.Builder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return WriteTo(builder, TotalTokens, PromptTokens, CompletionTokens);
    }

    /// <summary>
    /// Writes externally accumulated token counters into a crew metadata builder using
    /// the canonical keys (used by <see cref="GraphProcessStrategy"/>, whose counting
    /// lives in the graph state).
    /// </summary>
    /// <param name="builder">The metadata builder to write into.</param>
    /// <param name="totalTokens">The measured total token count.</param>
    /// <param name="promptTokens">The measured prompt-side token count (0 = split unavailable).</param>
    /// <param name="completionTokens">The measured completion-side token count (0 = split unavailable).</param>
    /// <returns>The same builder for chaining.</returns>
    public static CrewMetadata.Builder WriteTo(
        CrewMetadata.Builder builder,
        int totalTokens,
        int promptTokens,
        int completionTokens)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddTotalTokens(totalTokens);
        if (promptTokens > 0 || completionTokens > 0)
        {
            builder.AddPromptTokens(promptTokens);
            builder.AddCompletionTokens(completionTokens);
        }

        return builder;
    }
}
