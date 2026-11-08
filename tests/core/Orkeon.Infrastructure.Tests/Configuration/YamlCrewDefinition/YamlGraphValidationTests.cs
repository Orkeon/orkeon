using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>FWK-28 (ADR-018, points 3 and 6): a graph the topology validator or the edge rules refuse never loads, and each message names the node.</summary>
public sealed class YamlGraphValidationTests
{
    private static async System.Threading.Tasks.Task<string> RefusalAsync(string yaml)
    {
        var loader = new YamlCrewDefinitionLoader(
            new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);
        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken));
        return ex.Message;
    }

    private const string Nodes = """
  nodes:
    - { id: triage,   task: triage }
    - { id: fix,      task: fix }
    - { id: escalate, task: escalate }
""";

    private const string Terminal = """
    - { from: fix,      to: END }
    - { from: escalate, to: END }
""";

    private static string Block(string edges, string nodes = Nodes, string budgets = "") =>
        $"graph:\n{nodes}\n  edges:\n{edges}\n{Terminal}\n{budgets}";

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldRefuseAnOrphanNode()
    {
        var message = await RefusalAsync(YamlGraphTestCrews.Triage(Block(
            "    - { from: triage, to: fix }")));

        Assert.Contains("escalate", message, StringComparison.Ordinal);
        Assert.Contains("orphan", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldRefuseAnEdgeToAnUnknownNode_NamingItAndTheOnesThatExist()
    {
        var message = await RefusalAsync(YamlGraphTestCrews.Triage(Block(
            "    - { from: triage, to: ghost }")));

        Assert.Contains("ghost", message, StringComparison.Ordinal);
        Assert.Contains("fix", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldRefuseTwoEdgesWithoutWhenFromOneNode()
    {
        var message = await RefusalAsync(YamlGraphTestCrews.Triage(Block(
            "    - { from: triage, to: fix }\n    - { from: triage, to: escalate }")));

        Assert.Contains("'triage'", message, StringComparison.Ordinal);
        Assert.Contains("without when", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldRefuseAWhenOnATaskThatIsNotADecision()
    {
        var message = await RefusalAsync(YamlGraphTestCrews.Triage("""
graph:
  nodes:
    - { id: triage,   task: triage }
    - { id: fix,      task: fix }
    - { id: escalate, task: escalate }
  edges:
    - { from: triage,   to: fix }
    - { from: fix,      to: escalate, when: { fix.category: [bug] } }
    - { from: fix,      to: END }
    - { from: escalate, to: END }
"""));

        Assert.Contains("'fix'", message, StringComparison.Ordinal);
        Assert.Contains("not a decision task", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldRefuseADecisionWithNoOtherwiseEdge()
    {
        var message = await RefusalAsync(YamlGraphTestCrews.Triage(Block(
            "    - { from: triage, to: fix, when: { triage.category: [bug] } }\n    - { from: triage, to: escalate, when: { triage.category: [other] } }")));

        Assert.Contains("'triage'", message, StringComparison.Ordinal);
        Assert.Contains("otherwise", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldRefuseSeveralEdgesOnAnOrdinaryTask()
    {
        var message = await RefusalAsync(YamlGraphTestCrews.Triage("""
graph:
  nodes:
    - { id: triage,   task: triage }
    - { id: fix,      task: fix }
    - { id: escalate, task: escalate }
  edges:
    - { from: triage,   to: fix }
    - { from: fix,      to: escalate }
    - { from: fix,      to: END }
    - { from: escalate, to: END }
"""));

        Assert.Contains("'fix'", message, StringComparison.Ordinal);
        Assert.Contains("exactly one", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldRefuseAWhenReadingAnotherTaskThanTheFromNode()
    {
        var message = await RefusalAsync(YamlGraphTestCrews.Triage(Block(
            "    - { from: triage, to: fix, when: { fix.category: [bug] } }\n    - { from: triage, to: escalate }")));

        Assert.Contains("'triage'", message, StringComparison.Ordinal);
        Assert.Contains("fix", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldRefuseAWhenOnAQuestionTheDecisionDoesNotAsk()
    {
        var message = await RefusalAsync(YamlGraphTestCrews.Triage(Block(
            "    - { from: triage, to: fix, when: { triage.severity: [high] } }\n    - { from: triage, to: escalate }")));

        Assert.Contains("severity", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldRefuseATaskNoNodeRuns()
    {
        var message = await RefusalAsync(YamlGraphTestCrews.Triage("""
graph:
  nodes:
    - { id: triage, task: triage }
    - { id: fix,    task: fix }
  edges:
    - { from: triage, to: fix }
    - { from: fix,    to: END }
"""));

        Assert.Contains("'escalate'", message, StringComparison.Ordinal);
        Assert.Contains("no node", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldRefuseATaskTwoNodesRun()
    {
        var message = await RefusalAsync(YamlGraphTestCrews.Triage("""
graph:
  nodes:
    - { id: triage,    task: triage }
    - { id: fix,       task: fix }
    - { id: fix_again, task: fix }
    - { id: escalate,  task: escalate }
  edges:
    - { from: triage,    to: fix }
    - { from: fix,       to: fix_again }
    - { from: fix_again, to: escalate }
    - { from: escalate,  to: END }
"""));

        Assert.Contains("'fix_again'", message, StringComparison.Ordinal);
        Assert.Contains("already runs", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldRefuseANodeOfAnUnknownTask()
    {
        var message = await RefusalAsync(YamlGraphTestCrews.Triage("""
graph:
  nodes:
    - { id: triage,   task: triage }
    - { id: fix,      task: fix }
    - { id: escalate, task: ghost }
  edges:
    - { from: triage,   to: fix }
    - { from: fix,      to: escalate }
    - { from: escalate, to: END }
"""));

        Assert.Contains("'escalate'", message, StringComparison.Ordinal);
        Assert.Contains("ghost", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldRefuseAPathWithNoWayToEnd()
    {
        var message = await RefusalAsync(YamlGraphTestCrews.Triage("""
graph:
  nodes:
    - { id: triage,   task: triage }
    - { id: fix,      task: fix }
    - { id: escalate, task: escalate }
  edges:
    - { from: triage,   to: fix }
    - { from: fix,      to: escalate }
    - { from: escalate, to: fix }
"""));

        Assert.Contains("path", message, StringComparison.Ordinal);
        Assert.Contains("END", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldRefuseATaskLevelDependenciesBesideNodes()
    {
        var message = await RefusalAsync(YamlGraphTestCrews.Triage("""
graph:
  nodes:
    - { id: triage,   task: triage }
    - { id: fix,      task: fix }
    - { id: escalate, task: escalate }
  edges:
    - { from: triage,   to: fix }
    - { from: fix,      to: escalate }
    - { from: escalate, to: END }
""", extraTask: "    dependencies: [triage]"));

        Assert.Contains("'fix'", message, StringComparison.Ordinal);
        Assert.Contains("dependencies", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldRefuseAGraphBlockOutsideTheGraphProcess()
    {
        var message = await RefusalAsync(YamlGraphTestCrews.Triage(YamlGraphTestCrews.Branching, process: "sequential"));

        Assert.Contains("graph:", message, StringComparison.Ordinal);
        Assert.Contains("process: graph", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldRefuseTheStartAndEndWordsAsNodeIds()
    {
        var message = await RefusalAsync(YamlGraphTestCrews.Triage("""
graph:
  nodes:
    - { id: END,      task: triage }
    - { id: fix,      task: fix }
    - { id: escalate, task: escalate }
  edges:
    - { from: fix,      to: escalate }
    - { from: escalate, to: END }
"""));

        Assert.Contains("END", message, StringComparison.Ordinal);
        Assert.Contains("reserved", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-28")]
    public async System.Threading.Tasks.Task ShouldReportEveryFaultAtOnce()
    {
        var message = await RefusalAsync(YamlGraphTestCrews.Triage("""
graph:
  nodes:
    - { id: triage,   task: triage }
    - { id: fix,      task: fix }
    - { id: escalate, task: escalate }
  edges:
    - { from: triage, to: ghost }
    - { from: fix,    to: END }
"""));

        Assert.Contains("ghost", message, StringComparison.Ordinal);
        Assert.Contains("escalate", message, StringComparison.Ordinal);
    }
}
