using System.Collections.Immutable;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// The thinking a task of an agent keeps across its attempts when its config leaves something to System One (#439):
/// the answer picked at its first attempt, raised one step among <see cref="Options"/> after a failed attempt (a failed
/// result the run would retry, or a thrown error), never lowered.
/// </summary>
/// <remarks>
/// Shared by every execution of the same task by the same agent. Each failed execution counts once: a concurrent
/// success never clears another's failure, and each failure raises one later attempt. A round of the output that fails
/// its guardrail raises the level of its own execution (<see cref="RaiseFrom"/>); the kept one follows when it is
/// higher, so a concurrent execution keeps the level it started at.
/// </remarks>
/// <param name="thinking">The task's thinking as written, its override merged, with something left to System One.</param>
/// <param name="standing">The thinking that stands without <c>auto</c>.</param>
/// <param name="settled">The thinking the first attempt runs on.</param>
/// <param name="options">The answers offered, cheapest first.</param>
internal sealed class KeptThinking(
    LlmThinkingConfig thinking,
    LlmThinkingConfig? standing,
    LlmThinkingConfig? settled,
    ImmutableList<string> options)
{
    private readonly Lock _gate = new();
    private LlmThinkingConfig? _settled = settled;
    private int _failures;

    /// <summary>The answers offered, cheapest first.</summary>
    public ImmutableList<string> Options => options;

    /// <summary>The thinking the next attempt runs on.</summary>
    public LlmThinkingConfig? Current
    {
        get
        {
            lock (_gate)
                return _settled;
        }
    }

    /// <summary>Records how an execution ended: each failed one raises one later attempt.</summary>
    /// <param name="failed">Whether the execution failed in a way a raise may help.</param>
    public void NoteAttempt(bool failed)
    {
        if (!failed)
            return;
        lock (_gate)
            _failures++;
    }

    /// <summary>
    /// After a failed execution not yet answered, the level one step up among the answers offered, kept from then on;
    /// null when no failure waits, when the level is already the top one, or when it is the provider's own, which no
    /// answer names.
    /// </summary>
    /// <returns>The level before and after, or null.</returns>
    public (string? Previous, string? Raised)? RaiseAfterFailure()
    {
        lock (_gate)
        {
            if (_failures == 0)
                return null;
            _failures--;
            if (Step(_settled) is not { } step)
                return null;
            _settled = step.Settled;
            return (step.Previous, step.Raised);
        }
    }

    /// <summary>
    /// The thinking one step above <paramref name="from"/>, the level one execution runs at, after a round of its output
    /// failed its guardrail; the kept level becomes it when it is higher. Null at the top, or when the level is the
    /// provider's own.
    /// </summary>
    /// <param name="from">The thinking the execution runs on.</param>
    /// <returns>The raised thinking and the levels before and after, or null.</returns>
    public (LlmThinkingConfig? Settled, string? Previous, string? Raised)? RaiseFrom(LlmThinkingConfig? from)
    {
        lock (_gate)
        {
            if (Step(from) is not { } step)
                return null;
            if (Rank(step.Settled) > Rank(_settled))
                _settled = step.Settled;
            return step;
        }
    }

    /// <summary>The level an <c>agent.effort</c> event names for a settled thinking: <c>off</c> when it is switched off.</summary>
    /// <param name="settled">A settled thinking.</param>
    /// <returns>The level, or null for the provider's own.</returns>
    public static string? LevelOf(LlmThinkingConfig? settled) =>
        settled is { Enabled: false } ? LlmThinkingConfig.Off : settled?.Effort;

    private (LlmThinkingConfig? Settled, string? Previous, string? Raised)? Step(LlmThinkingConfig? from)
    {
        var above = LlmThinkingConfig.Above(LlmThinkingConfig.AnswerOf(from, options), options);
        if (above is null)
            return null;
        var next = thinking.Settle(above, standing);
        // A thinking held off below stays off: the answer above changes nothing, and no raise is told.
        if (Equals(next, from))
            return null;
        return (next, LevelOf(from), LevelOf(next));
    }

    private int Rank(LlmThinkingConfig? settled) =>
        LlmThinkingConfig.AnswerOf(settled, options) is { } answer ? options.IndexOf(answer) : -1;
}
