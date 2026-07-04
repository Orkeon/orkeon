using Orkeon.Application.EventHub;
using Orkeon.Domain.Autonomous;

namespace Orkeon.Infrastructure.EventHub;

/// <summary>
/// Budget-aware wait helpers for <see cref="IEventHub"/>.
/// <para>
/// Ambiguity resolved: spec §9.4 references <c>AgentExecutionBudget.LinkedToken</c>, a member
/// that does not exist on <see cref="AgentExecutionBudget"/>. These extensions poll
/// <see cref="AgentExecutionBudget.IsExhausted"/> on a short cadence and surface a
/// <see cref="BudgetExhaustedException"/> distinct from a wait timeout when the budget runs out.
/// </para>
/// </summary>
public static class EventHubBudgetExtensions
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// Waits for a message exactly like <see cref="IEventHub.WaitForAsync"/>, but observes
    /// <paramref name="budget"/> and throws <see cref="BudgetExhaustedException"/> when any
    /// dimension is exhausted.
    /// </summary>
    public static Task<Message> WaitForAsync(
        this IEventHub hub,
        WaitDescriptor descriptor,
        WaitTimeout timeout,
        AgentExecutionBudget budget,
        CancellationToken ct)
        => hub.WaitForAsync(descriptor, timeout, budget, DefaultPollInterval, ct);

    /// <summary>
    /// Variant of <see cref="WaitForAsync(IEventHub,WaitDescriptor,WaitTimeout,AgentExecutionBudget,CancellationToken)"/>
    /// allowing a custom <paramref name="pollInterval"/> (used by tests to keep wall time small).
    /// </summary>
    public static Task<Message> WaitForAsync(
        this IEventHub hub,
        WaitDescriptor descriptor,
        WaitTimeout timeout,
        AgentExecutionBudget budget,
        TimeSpan pollInterval,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(timeout);
        ArgumentNullException.ThrowIfNull(budget);
        if (pollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "Poll interval must be positive.");

        return WaitForCoreAsync(hub, descriptor, timeout, budget, pollInterval, ct);
    }

    private static async Task<Message> WaitForCoreAsync(
        IEventHub hub,
        WaitDescriptor descriptor,
        WaitTimeout timeout,
        AgentExecutionBudget budget,
        TimeSpan pollInterval,
        CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var stopWatcher = new CancellationTokenSource();
        var watcher = WatchBudgetAsync(budget, linked, pollInterval, stopWatcher.Token);

        try
        {
            return await hub.WaitForAsync(descriptor, timeout, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (budget.IsExhausted && !ct.IsCancellationRequested)
        {
            throw new BudgetExhaustedException(
                BudgetDimension.WallTime,
                $"Budget exhausted while waiting: {DescribeBudget(budget)}");
        }
        finally
        {
            await stopWatcher.CancelAsync().ConfigureAwait(false);
            try { await watcher.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
            stopWatcher.Dispose();
        }
    }

    private static async System.Threading.Tasks.Task WatchBudgetAsync(
        AgentExecutionBudget budget,
        CancellationTokenSource linked,
        TimeSpan pollInterval,
        CancellationToken stopToken)
    {
        while (!stopToken.IsCancellationRequested)
        {
            if (budget.IsExhausted)
            {
                await linked.CancelAsync().ConfigureAwait(false);
                return;
            }
            try { await System.Threading.Tasks.Task.Delay(pollInterval, stopToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private static string DescribeBudget(AgentExecutionBudget b)
        => $"tool_calls={b.CurrentToolCalls}/{b.MaxToolCalls}, " +
           $"delegation={b.CurrentDelegationDepth}/{b.MaxDelegationDepth}, " +
           $"tokens={b.CurrentTokensConsumed}/{b.MaxTokensConsumed}, " +
           $"spawned={b.CurrentSpawnedAgents}/{b.MaxSpawnedAgents}, " +
           $"wall={b.Elapsed:g}/{b.MaxWallTime:g}";
}
