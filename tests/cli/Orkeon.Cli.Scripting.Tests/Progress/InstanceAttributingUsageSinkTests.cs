using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Scripting.Dispatch;
using Orkeon.Cli.Scripting.Progress;

namespace Orkeon.Cli.Scripting.Tests.Progress;

public sealed class InstanceAttributingUsageSinkTests
{
    private static CommandInstance NewInstance()
        => new CommandInstanceRegistry().Register(
            "assistant", CommandInstanceKind.Async, "crew:main-loop", "main-loop", Guid.NewGuid());

    private static CostUsageEvent Usage(int prompt, int completion)
        => new() { CrewId = "main-loop", AgentId = "assistant", PromptTokens = prompt, CompletionTokens = completion };

    [Fact]
    public void Forwards_to_the_cost_manager_and_credits_the_ambient_instance()
    {
        var cost = new MockCostBudgetManager();
        var sink = new InstanceAttributingUsageSink(cost);
        var instance = NewInstance();

        ProgressAmbient.CurrentInstance = instance;
        try
        {
            sink.Record(Usage(100, 20));
            sink.Record(Usage(50, 5));
        }
        finally
        {
            ProgressAmbient.CurrentInstance = null;
        }

        Assert.Equal(2, cost.Recorded.Count);
        Assert.Equal(175, instance.TokensUsed);
        Assert.Equal(175, instance.Snapshot().tokens);
    }

    [Fact]
    public void Without_an_ambient_instance_only_the_session_total_moves()
    {
        // Foreground commands run unticketed: their usage belongs to the session,
        // not to any agents-pane row.
        var cost = new MockCostBudgetManager();
        new InstanceAttributingUsageSink(cost).Record(Usage(10, 1));

        Assert.Single(cost.Recorded);
    }

    [Fact]
    public void Without_a_cost_manager_the_instance_is_still_credited()
    {
        var sink = new InstanceAttributingUsageSink();
        var instance = NewInstance();

        ProgressAmbient.CurrentInstance = instance;
        try { sink.Record(Usage(7, 3)); }
        finally { ProgressAmbient.CurrentInstance = null; }

        Assert.Equal(10, instance.TokensUsed);
    }

    [Fact]
    public async Task The_ambient_instance_flows_through_async_hops()
    {
        // The whole attribution rides an AsyncLocal set in the dispatch Task.Run —
        // it must survive awaits the way the LLM call path actually awaits.
        var sink = new InstanceAttributingUsageSink();
        var instance = NewInstance();

        ProgressAmbient.CurrentInstance = instance;
        try
        {
            await Task.Run(async () =>
            {
                await Task.Yield();
                sink.Record(Usage(1, 2));
                await Task.Delay(1, TestContext.Current.CancellationToken).ConfigureAwait(false);
                sink.Record(Usage(3, 4));
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            ProgressAmbient.CurrentInstance = null;
        }

        Assert.Equal(10, instance.TokensUsed);
    }

    [Fact]
    public void Tokens_are_accepted_even_after_a_terminal_transition()
    {
        // The last response's usage legitimately races Complete — the final count in
        // ps/inspect must stay truthful rather than dropping the closing call.
        var instance = NewInstance();
        instance.Complete(new CommandResponse("crew:main-loop", "main-loop", success: true, payload: "ok", error: null));

        instance.AddTokens(42);

        Assert.Equal(42, instance.Snapshot().tokens);
    }

    // ── doubles (repo convention: hand-written, no mocking framework) ────────

    private sealed class MockCostBudgetManager : ICostBudgetManager
    {
        public List<CostUsageEvent> Recorded { get; } = new();

        public event EventHandler<BudgetAlertEventArgs>? OnBudgetAlert { add { } remove { } }

        public CostBudgetCheckResult RecordUsage(CostUsageEvent usageEvent)
        {
            Recorded.Add(usageEvent);
            return CostBudgetCheckResult.WithinBudget();
        }

        public CostReport GetReport(string? crewId = null, string? agentId = null) => new();
        public CostReport GetReportForPeriod(DateTime from, DateTime toDate, string? crewId = null) => new();
        public void SetCrewBudget(string crewId, BudgetLimit budget) { }
        public void SetAgentBudget(string crewId, string agentId, BudgetLimit budget) { }
        public IReadOnlyList<BudgetAlert> GetActiveAlerts() => Array.Empty<BudgetAlert>();
    }
}
