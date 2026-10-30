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
/// <remarks>
/// The cache counts follow <c>ModelUsage.SumReported</c>: a count no task reported stays null,
/// never a measured 0, while a reported 0 is kept.
/// </remarks>
internal sealed class TokenUsageTally
{
    private readonly Lock _gate = new();
    private int _totalTokens;
    private int _promptTokens;
    private int _completionTokens;
    private long? _cacheHitTokens;
    private long? _cacheMissTokens;
    private int _cacheHitReports;
    private int _cacheMissReports;

    /// <summary>Gets the accumulated total token count.</summary>
    public int TotalTokens { get { lock (_gate) return _totalTokens; } }

    /// <summary>Gets the accumulated prompt-side token count (0 when no provider reported the split).</summary>
    public int PromptTokens { get { lock (_gate) return _promptTokens; } }

    /// <summary>Gets the accumulated completion-side token count (0 when no provider reported the split).</summary>
    public int CompletionTokens { get { lock (_gate) return _completionTokens; } }

    /// <summary>Gets the accumulated cache-served prompt tokens; null when no task reported the count.</summary>
    public long? CacheHitTokens { get { lock (_gate) return _cacheHitTokens; } }

    /// <summary>Gets the accumulated cache-missed prompt tokens; null when no task reported the count.</summary>
    public long? CacheMissTokens { get { lock (_gate) return _cacheMissTokens; } }

    /// <summary>
    /// Records the token telemetry of a single task execution. Safe to call concurrently
    /// (parallel strategies, A2A channel handlers).
    /// </summary>
    /// <param name="result">The task result whose token counters are accumulated. Null is ignored.</param>
    public void Record(TaskResult? result)
    {
        if (result is null)
            return;

        lock (_gate)
        {
            if (result.TokensUsed > 0)
                _totalTokens += result.TokensUsed;
            if (result.PromptTokens > 0)
                _promptTokens += result.PromptTokens;
            if (result.CompletionTokens > 0)
                _completionTokens += result.CompletionTokens;
            if (result.CacheHitTokens is { } hit)
            {
                _cacheHitTokens = (_cacheHitTokens ?? 0) + hit;
                _cacheHitReports++;
            }
            if (result.CacheMissTokens is { } miss)
            {
                _cacheMissTokens = (_cacheMissTokens ?? 0) + miss;
                _cacheMissReports++;
            }
        }
    }

    /// <summary>The tally's counters at this moment, to meter one task on the delta around it.</summary>
    public TokenUsageMark Mark()
    {
        lock (_gate)
            return new TokenUsageMark(_totalTokens, _cacheHitTokens ?? 0, _cacheMissTokens ?? 0, _cacheHitReports, _cacheMissReports);
    }

    /// <summary>
    /// What was recorded since <paramref name="mark"/>: the tokens, and each cache count, null when
    /// no result recorded since reported it.
    /// </summary>
    public (int Tokens, long? CacheHit, long? CacheMiss) Since(TokenUsageMark mark)
    {
        lock (_gate)
        {
            return (
                _totalTokens - mark.Total,
                _cacheHitReports > mark.CacheHitReports ? (_cacheHitTokens ?? 0) - mark.CacheHit : null,
                _cacheMissReports > mark.CacheMissReports ? (_cacheMissTokens ?? 0) - mark.CacheMiss : null);
        }
    }

    /// <summary>Adds a reported count to a running total that stays null until one is reported.</summary>
    public static long? AddReported(long? total, long? count) =>
        count is { } value ? (total ?? 0) + value : total;

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
        return WriteTo(builder, TotalTokens, PromptTokens, CompletionTokens, CacheHitTokens, CacheMissTokens);
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
    /// <param name="cacheHitTokens">The measured cache-served prompt tokens; null when unreported.</param>
    /// <param name="cacheMissTokens">The measured cache-missed prompt tokens; null when unreported.</param>
    /// <returns>The same builder for chaining.</returns>
    public static CrewMetadata.Builder WriteTo(
        CrewMetadata.Builder builder,
        int totalTokens,
        int promptTokens,
        int completionTokens,
        long? cacheHitTokens = null,
        long? cacheMissTokens = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddTotalTokens(totalTokens);
        if (promptTokens > 0 || completionTokens > 0)
        {
            builder.AddPromptTokens(promptTokens);
            builder.AddCompletionTokens(completionTokens);
        }

        // Each cache count is written when reported, a measured 0 included; an unreported
        // count stays absent, never written as a measured zero.
        if (cacheHitTokens is { } hit)
            builder.AddCacheHitTokens(hit);
        if (cacheMissTokens is { } miss)
            builder.AddCacheMissTokens(miss);

        return builder;
    }
}

/// <summary>The counters of a <see cref="TokenUsageTally"/> at one moment (<see cref="TokenUsageTally.Mark"/>).</summary>
internal readonly record struct TokenUsageMark(int Total, long CacheHit, long CacheMiss, int CacheHitReports, int CacheMissReports);
