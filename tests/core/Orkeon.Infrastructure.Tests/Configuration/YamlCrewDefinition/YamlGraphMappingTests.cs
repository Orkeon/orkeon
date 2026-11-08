using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>FWK-28 (ADR-018, points 5 to 7): the YAML <c>graph:</c> block becomes the nodes, edges and budgets of the crew's <see cref="GraphConfig"/>.</summary>
public sealed class YamlGraphMappingTests
{
    private static YamlCrewDefinitionLoader Loader() =>
        new(new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);

    private static Task<CrewConfiguration> LoadAsync(string yaml) =>
        Loader().LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldTranslateTheBlockIntoNodesAndEdges()
    {
        var graph = (await LoadAsync(YamlGraphTestCrews.Triage(YamlGraphTestCrews.Branching))).GraphConfig;

        Assert.NotNull(graph);
        Assert.Equal("triage", graph.Start);
        Assert.Equal(
            [("triage", "triage"), ("fix", "fix"), ("escalate", "escalate")],
            graph.Nodes!.Select(n => (n.Id, n.Task)).ToArray());
        Assert.Equal(
            [("triage", "fix"), ("triage", "escalate"), ("fix", GraphConfig.End), ("escalate", GraphConfig.End)],
            graph.Edges.Select(e => (e.From, e.To)).ToArray());
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldReadTheWhenOfAnEdgeAsAConditionOnTheFromTask_AndLeaveTheOtherwiseWithout()
    {
        var edges = (await LoadAsync(YamlGraphTestCrews.Triage(YamlGraphTestCrews.Branching))).GraphConfig!.Edges;

        var condition = Assert.IsType<TaskCondition>(edges[0].When);
        Assert.Equal("triage", condition.DecisionTask);
        Assert.Equal(["bug"], condition.Clauses.Single().Options.ToArray());
        Assert.Null(edges[1].When);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldCarryTheBudgets_MaxVisitsBecomingTheDomainsMaxStateVisits()
    {
        var graph = (await LoadAsync(YamlGraphTestCrews.Triage(YamlGraphTestCrews.Branching))).GraphConfig!;

        Assert.Equal(50, graph.MaxTransitions);
        Assert.Equal(5, graph.MaxStateVisits);
        Assert.Equal("strict", graph.CircuitBreakerPreset);
        Assert.Equal(600, graph.MaxTotalDurationSeconds);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldKeepTheImplicitGraph_WhenTheBlockHoldsOnlyBudgets()
    {
        var graph = (await LoadAsync(YamlGraphTestCrews.Triage("graph:\n  maxVisits: 4"))).GraphConfig!;

        Assert.Null(graph.Nodes);
        Assert.Empty(graph.Edges);
        Assert.Equal(4, graph.MaxStateVisits);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldMakeOneImplicitNodePerTask_WhenEdgesComeWithoutNodes()
    {
        var yaml = YamlGraphTestCrews.Triage("""
graph:
  edges:
    - { from: triage, to: fix, when: { triage.category: [bug] } }
    - { from: triage, to: escalate }
    - { from: fix,      to: END }
    - { from: escalate, to: END }
""");

        var graph = (await LoadAsync(yaml)).GraphConfig!;

        Assert.Null(graph.Nodes);
        Assert.Equal(4, graph.Edges.Count);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldReadASnakeCaseBlock()
    {
        var graph = (await LoadAsync(YamlGraphTestCrews.Triage("graph:\n  max_visits: 7\n  max_transitions: 21"))).GraphConfig!;

        Assert.Equal(7, graph.MaxStateVisits);
        Assert.Equal(21, graph.MaxTransitions);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldRefuseTheRetiredGraphConfigKey_PointingToGraph()
    {
        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => LoadAsync(YamlGraphTestCrews.Triage("graphConfig:\n  maxStateVisits: 3")));

        Assert.Contains("graphConfig", ex.Message, StringComparison.Ordinal);
        Assert.Contains("graph:", ex.Message, StringComparison.Ordinal);
    }
}
