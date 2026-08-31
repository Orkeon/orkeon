namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// The one <c>cost.updated</c> builder, so a live reading and a stage-boundary reading can
/// never drift apart.
/// <para>
/// The event used to be emitted from a single place — the engine, once a stage RETURNED.
/// That is precisely when the user has stopped waiting. An interview is one stage of up to
/// twenty-four turns, so the meter stood at zero for the entire conversation and then jumped
/// in one step, which is the opposite of what a meter is for. A long stage now reports after
/// each turn, and the engine still closes the reading when it charges the budget.
/// </para>
/// </summary>
internal static class ForgeCost
{
    /// <summary>
    /// Emits the session's standing meter: what the budget has already been charged, plus
    /// what the running stage has spent and not yet handed over.
    /// </summary>
    /// <param name="events">The protocol writer.</param>
    /// <param name="budget">The session budget — the authority on what is already charged.</param>
    /// <param name="pending">
    /// The running stage's own spend, not yet registered. Zero at a stage boundary, where the
    /// engine has just charged it; nonzero mid-stage. Passing it separately is what keeps the
    /// figure honest without charging the budget twice.
    /// </param>
    public static void Emit(ForgeEventWriter events, ForgeBudget budget, ForgeUsageSnapshot pending)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(budget);

        var total = budget.ConsumedTokens + pending.TotalTokens;
        events.Emit("cost.updated", new
        {
            tokens = total,
            // Both directions: a user watching a compose wants to see what is going UP and
            // what is coming BACK, not one number that only grows.
            promptTokens = budget.ConsumedPromptTokens + pending.PromptTokens,
            completionTokens = budget.ConsumedCompletionTokens + pending.CompletionTokens,
            // How much of the total the runtime had to approximate because a provider
            // reported nothing — a figure a screen must mark rather than pass off as a count.
            estimatedTokens = budget.ConsumedEstimatedTokens + pending.EstimatedTokens,
            // USD is the client's business (it knows the pricing).
            budgetRemaining = budget.MaxTokens > 0 ? Math.Max(0, budget.MaxTokens - total) : (long?)null,
        });
    }
}
