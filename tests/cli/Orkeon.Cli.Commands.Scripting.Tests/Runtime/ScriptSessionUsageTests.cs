using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.Commands.Scripting.Dispatch;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Doubles;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary>#226 - <c>ctx.services.get("usage")</c>: the session's usage by model and by command instance.</summary>
public sealed class ScriptSessionUsageTests
{
    private static CostReport TwoModels() => new()
    {
        TotalCost = 0.75m,
        TotalCalls = 3,
        ModelUsageByModel = new Dictionary<string, ModelUsage>
        {
            ["gpt-4o"] = new() { PromptTokens = 1000, CompletionTokens = 200, CacheReadTokens = 600, CacheWriteTokens = 50, ReasoningTokens = 30, Cost = 0.75m, Priced = true },
            ["local-model"] = new() { PromptTokens = 10, CompletionTokens = 5 },
        },
    };

    [Trait("BR", "CH-226")]
    [Fact]
    public void The_report_lists_the_session_totals_and_each_model()
    {
        var usage = new ScriptSessionUsage(new MockCostBudgetManager { Report = TwoModels() }, instances: null);

        var report = usage.report();

        Assert.Equal(1010, report.promptTokens);
        Assert.Equal(205, report.completionTokens);
        Assert.Equal(600, report.cacheHitTokens);
        Assert.Equal(50, report.cacheWriteTokens);
        Assert.Equal(30, report.reasoningTokens);
        Assert.Equal(1215, report.totalTokens);
        Assert.Equal(3, report.calls);
        Assert.Equal(0.75, report.cost);

        Assert.Equal(["gpt-4o", "local-model"], report.byModel.Select(m => m.model));
        var priced = report.byModel[0];
        Assert.Equal(1000, priced.promptTokens);
        Assert.Equal(200, priced.completionTokens);
        Assert.Equal(600, priced.cacheHitTokens);
        Assert.Equal(50, priced.cacheWriteTokens);
        Assert.Equal(30, priced.reasoningTokens);
        Assert.Equal(0.75, priced.cost);
        Assert.Null(report.byModel[1].cost);
    }

    [Trait("BR", "CH-226")]
    [Fact]
    public void A_session_where_no_model_is_priced_has_no_cost()
    {
        var report = new ScriptSessionUsage(new MockCostBudgetManager
        {
            Report = new CostReport { ModelUsageByModel = new Dictionary<string, ModelUsage> { ["local-model"] = new() { PromptTokens = 1 } } },
        }, instances: null).report();

        Assert.Null(report.cost);
    }

    [Trait("BR", "CH-226")]
    [Fact]
    public void The_report_lists_each_command_instance_with_its_split_and_cost()
    {
        var registry = new CommandInstanceRegistry();
        var crew = registry.Register("assistant", CommandInstanceKind.Async, "crew:main-loop", "main-loop", Guid.NewGuid());
        crew.AddUsage(new CostUsageEvent { PromptTokens = 100, CompletionTokens = 20, CacheHitTokens = 80, ReasoningTokens = 4 }, 0.1m);
        var idle = registry.Register("review", CommandInstanceKind.Async, "crew:review", "review", Guid.NewGuid());

        var report = new ScriptSessionUsage(costManager: null, registry).report();

        Assert.Empty(report.byModel);
        Assert.Null(report.cost);
        var row = Assert.Single(report.byCommand, c => c.ticket == crew.Ticket);
        Assert.Equal("assistant", row.name);
        Assert.Equal(crew.State.ToToken(), row.state);
        Assert.Equal(100, row.promptTokens);
        Assert.Equal(20, row.completionTokens);
        Assert.Equal(80, row.cacheHitTokens);
        Assert.Equal(4, row.reasoningTokens);
        Assert.Equal(1, row.calls);
        Assert.Equal(0.1, row.cost);
        var none = Assert.Single(report.byCommand, c => c.ticket == idle.Ticket);
        Assert.Equal(0, none.calls);
        Assert.Null(none.cost);
    }

    [Trait("BR", "CH-226")]
    [Fact]
    public void The_whitelist_serves_usage_only_when_the_host_keeps_a_cost_manager_or_instances()
    {
        var built = DefaultScriptServiceWhitelist.Build().Build();
        Assert.Contains(ScriptServiceKeys.Usage, built.Optional);

        using var bare = new ServiceCollection().BuildServiceProvider();
        Assert.False(new ScriptServiceLocator(built, bare).has(ScriptServiceKeys.Usage));

        using var host = new ServiceCollection()
            .AddSingleton<ICostBudgetManager>(new MockCostBudgetManager { Report = TwoModels() })
            .AddSingleton(new CommandInstanceRegistry())
            .BuildServiceProvider();
        var locator = new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), host);
        Assert.True(locator.has(ScriptServiceKeys.Usage));
        var usage = Assert.IsType<ScriptSessionUsage>(locator.get(ScriptServiceKeys.Usage));
        Assert.Equal(2, usage.report().byModel.Count);
    }
}
