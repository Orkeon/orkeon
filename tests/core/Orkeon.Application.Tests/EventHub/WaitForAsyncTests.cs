using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.EventHub;
using Orkeon.Domain.Autonomous;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.EventHub;

namespace Orkeon.Application.Tests.EventHub;

public sealed class WaitForAsyncTests
{
    private static InMemoryEventHub NewHub(out DefaultEventHubCallerContext caller)
    {
        caller = new DefaultEventHubCallerContext();
        return new InMemoryEventHub(caller, NullLogger<InMemoryEventHub>.Instance);
    }

    [Fact]
    public async System.Threading.Tasks.Task WaitFor_Finite_expired_yields_WaitTimedOutMessage()
    {
        using var hub = NewHub(out _);

        var msg = await hub.WaitForAsync(
            new WaitOnTopic("never.published", null),
            FiniteWaitTimeout.Of(TimeSpan.FromMilliseconds(80)),
            CancellationToken.None);

        Assert.Equal("_system.wait_timed_out", msg.Topic);
        Assert.True(msg.Metadata.ContainsKey("original_topic"));
        Assert.Equal("never.published", msg.Metadata["original_topic"]);
        Assert.True(msg.Metadata.ContainsKey("original_wait_id"));
        Assert.True(msg.Metadata.ContainsKey("original_started_at"));
        Assert.Equal(Domain.Common.CrewId.System, msg.SourceCrewId);
    }

    [Fact]
    public async System.Threading.Tasks.Task WaitFor_Forever_without_message_blocks_until_cancelled()
    {
        using var hub = NewHub(out _);
        using var cts = new CancellationTokenSource();

        var task = hub.WaitForAsync(
            new WaitOnTopic("never.published.forever", null),
            ForeverWaitTimeout.Instance,
            cts.Token);

        // Let it park, then cancel — must surface as OperationCanceledException.
        await System.Threading.Tasks.Task.Delay(50, TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async System.Threading.Tasks.Task WaitFor_when_budget_exhausted_throws_BudgetExhausted_distinct_from_timeout()
    {
        using var hub = NewHub(out _);

        // Short wall-time budget so the watcher fires very quickly.
        var budget = new AgentExecutionBudget { MaxWallTime = TimeSpan.FromMilliseconds(80) };

        // Wait timeout deliberately longer than the budget so the failure comes from the budget,
        // not the wait deadline (which would surface a WaitTimedOutMessage instead).
        var ex = await Assert.ThrowsAsync<BudgetExhaustedException>(
            () => hub.WaitForAsync(
                new WaitOnTopic("never.published.budget", null),
                FiniteWaitTimeout.Of(TimeSpan.FromSeconds(5)),
                budget,
                TimeSpan.FromMilliseconds(10),
                CancellationToken.None));

        Assert.True(budget.IsExhausted);
        Assert.NotEqual(BudgetDimension.ToolCalls, ex.Dimension);  // dimension is whatever the watcher picks
    }

    [Fact]
    public async System.Threading.Tasks.Task WaitFor_OnTopic_returns_first_matching_message()
    {
        using var hub = NewHub(out _);

        var waitTask = hub.WaitForAsync(
            new WaitOnTopic("orders.shipped", null),
            FiniteWaitTimeout.Of(TimeSpan.FromSeconds(2)),
            CancellationToken.None);

        await System.Threading.Tasks.Task.Yield();
        await hub.PublishAsync("orders.shipped", new { id = "o-9" }, options: null, CancellationToken.None);

        var msg = await waitTask;
        Assert.Equal("orders.shipped", msg.Topic);
    }

    [Fact]
    public async System.Threading.Tasks.Task WaitFor_OnMailbox_returns_next_posted_message()
    {
        using var hub = NewHub(out _);
        var addr = MailboxAddress.Parse(new Uri($"agent://{CrewId.Create()}/{AgentId.Create()}"));

        var waitTask = hub.WaitForAsync(
            new WaitOnMailbox(addr),
            FiniteWaitTimeout.Of(TimeSpan.FromSeconds(2)),
            CancellationToken.None);

        await System.Threading.Tasks.Task.Yield();
        await hub.PostAsync(addr, new { msg = "hi" }, CancellationToken.None);

        var msg = await waitTask;
        Assert.NotNull(msg.TargetMailbox);
        Assert.Equal(addr.Raw, msg.TargetMailbox!.Raw);
    }
}
