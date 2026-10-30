using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Progress;
using Orkeon.Cli.Commands.Scripting.Tests.Doubles;

namespace Orkeon.Cli.Commands.Scripting.Tests.Progress;

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

    /// <summary>#226 - the instance keeps the split of its usage and the cost the session settled.</summary>
    [Trait("BR", "CH-226")]
    [Fact]
    public void The_instance_keeps_the_split_of_its_usage_and_the_settled_cost()
    {
        var cost = new MockCostBudgetManager { NextCost = 0.25m };
        var sink = new InstanceAttributingUsageSink(cost);
        var instance = NewInstance();

        ProgressAmbient.CurrentInstance = instance;
        try
        {
            sink.Record(Usage(100, 20) with { CacheHitTokens = 60, CacheWriteTokens = 10, ReasoningTokens = 5 });
            sink.Record(Usage(50, 5) with { CacheHitTokens = 40 });
        }
        finally
        {
            ProgressAmbient.CurrentInstance = null;
        }

        var usage = instance.Usage;
        Assert.Equal(150, usage.PromptTokens);
        Assert.Equal(25, usage.CompletionTokens);
        Assert.Equal(100, usage.CacheHitTokens);
        Assert.Equal(10, usage.CacheWriteTokens);
        Assert.Equal(5, usage.ReasoningTokens);
        Assert.Equal(2, usage.Calls);
        Assert.Equal(0.50m, usage.Cost);
        Assert.Equal(175, instance.TokensUsed);
    }

    /// <summary>#254 - a count no call reported stays null on the instance, never 0.</summary>
    [Trait("BR", "CH-254")]
    [Fact]
    public void The_instance_keeps_an_unreported_count_null()
    {
        var sink = new InstanceAttributingUsageSink(new MockCostBudgetManager());
        var instance = NewInstance();

        ProgressAmbient.CurrentInstance = instance;
        try
        {
            sink.Record(Usage(100, 20));
            sink.Record(Usage(50, 5) with { CacheHitTokens = 5 });
        }
        finally
        {
            ProgressAmbient.CurrentInstance = null;
        }

        var usage = instance.Usage;
        Assert.Equal(5, usage.CacheHitTokens);
        Assert.Null(usage.CacheWriteTokens);
        Assert.Null(usage.ReasoningTokens);
    }

    [Trait("BR", "CH-226")]
    [Fact]
    public void An_instance_none_of_whose_calls_was_priced_has_no_cost()
    {
        var sink = new InstanceAttributingUsageSink(new MockCostBudgetManager { NextCost = null });
        var instance = NewInstance();

        ProgressAmbient.CurrentInstance = instance;
        try { sink.Record(Usage(7, 3)); }
        finally { ProgressAmbient.CurrentInstance = null; }

        Assert.Null(instance.Usage.Cost);
        Assert.Equal(1, instance.Usage.Calls);
    }

    [Trait("BR", "CH-226")]
    [Fact]
    public void Without_a_cost_manager_the_vendor_charge_is_the_instance_cost()
    {
        var sink = new InstanceAttributingUsageSink();
        var instance = NewInstance();

        ProgressAmbient.CurrentInstance = instance;
        try { sink.Record(Usage(7, 3) with { Cost = 0.01m }); }
        finally { ProgressAmbient.CurrentInstance = null; }

        Assert.Equal(0.01m, instance.Usage.Cost);
    }
}
