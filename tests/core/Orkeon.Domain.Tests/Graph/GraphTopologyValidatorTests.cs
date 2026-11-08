using Orkeon.Domain.Common.StateMachine;
using Orkeon.Domain.Graph;

namespace Orkeon.Domain.Tests.Graph;

/// <summary>
/// FWK-27 (ADR-018, point 3): one validator describes every fault of a graph's shape, whichever
/// engine, mapper or export hands the graph over.
/// </summary>
public sealed class GraphTopologyValidatorTests
{
    private const string Start = "S";
    private const string End = "E";

    private static GraphTopology Topology(string[] nodes, params GraphEdgeDescription[] edges) =>
        new(Start, End, nodes, edges);

    private static GraphEdgeDescription Fixed(string from, string to) => new(from, [to], IsFixed: true);

    private static GraphEdgeDescription Conditional(string from, params string[] targets) => new(from, targets, IsFixed: false);

    private static GraphEdgeDescription Unknown(string from) => new(from, null, IsFixed: false);

    [Fact]
    public void ShouldReturnNothing_WhenTheGraphIsASoundChain()
    {
        var faults = GraphTopologyValidator.Validate(Topology(
            ["a", "b"], Fixed(Start, "a"), Fixed("a", "b"), Fixed("b", End)));

        Assert.Empty(faults);
    }

    [Fact]
    public void ShouldAcceptALoop_WhenOneOfItsEdgesLeadsToEnd()
    {
        var faults = GraphTopologyValidator.Validate(Topology(
            ["a"], Fixed(Start, "a"), Conditional("a", "a", End)));

        Assert.Empty(faults);
    }

    [Fact]
    public void ShouldAcceptStartToEnd_WhenTheGraphHasNoNode()
    {
        Assert.Empty(GraphTopologyValidator.Validate(Topology([], Fixed(Start, End))));
    }

    [Fact]
    public void ShouldNameTheOrphan_WhenNothingReachesANode()
    {
        var faults = GraphTopologyValidator.Validate(Topology(
            ["a", "island"], Fixed(Start, "a"), Fixed("a", End), Fixed("island", End)));

        var fault = Assert.Single(faults);
        Assert.Contains("'island'", fault, StringComparison.Ordinal);
        Assert.Contains("orphan", fault, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldNameTheUnknownTarget_AndTheIdsThatExist()
    {
        var faults = GraphTopologyValidator.Validate(Topology(
            ["a", "b"], Fixed(Start, "a"), Fixed("a", "ghost"), Fixed("b", End)));

        Assert.Contains(faults, f => f.Contains("targets unknown node 'ghost'", StringComparison.Ordinal)
            && f.Contains("a, b", StringComparison.Ordinal));
    }

    [Fact]
    public void ShouldNameTheUnknownSource_WhenAnEdgeLeavesAnUndeclaredNode()
    {
        var faults = GraphTopologyValidator.Validate(Topology(
            ["a"], Fixed(Start, "a"), Fixed("a", End), Fixed("phantom", End)));

        Assert.Contains(faults, f => f.Contains("'phantom'", StringComparison.Ordinal));
    }

    [Fact]
    public void ShouldReportNoPathToEnd_WhenTheGraphIsAPureCycle()
    {
        var faults = GraphTopologyValidator.Validate(Topology(
            ["a"], Fixed(Start, "a"), Fixed("a", "a")));

        Assert.Contains(faults, f => f.Contains("no path from START to END", StringComparison.Ordinal));
    }

    [Fact]
    public void ShouldNameANodeThatCannotReachEnd_WhenOthersCan()
    {
        var faults = GraphTopologyValidator.Validate(Topology(
            ["a", "trap"], Fixed(Start, "a"), Conditional("a", "trap", End), Fixed("trap", "trap")));

        var fault = Assert.Single(faults);
        Assert.Contains("'trap'", fault, StringComparison.Ordinal);
        Assert.Contains("cannot reach END", fault, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldNameTheNodeWithoutOutgoingEdge()
    {
        var faults = GraphTopologyValidator.Validate(Topology(
            ["a", "b"], Fixed(Start, "a"), Fixed("a", "b")));

        Assert.Contains(faults, f => f.Contains("'b' has no outgoing edge", StringComparison.Ordinal));
    }

    [Fact]
    public void ShouldReportTheMissingStartEdge()
    {
        var faults = GraphTopologyValidator.Validate(Topology(["a"], Fixed("a", End)));

        Assert.Contains(faults, f => f.Contains("no edge from START", StringComparison.Ordinal));
    }

    [Fact]
    public void ShouldRefuseSeveralFixedEdges_FromOneNode()
    {
        var faults = GraphTopologyValidator.Validate(Topology(
            ["a", "b"], Fixed(Start, "a"), Fixed("a", "b"), Fixed("a", End), Fixed("b", End)));

        Assert.Contains(faults, f => f.Contains("'a'", StringComparison.Ordinal)
            && f.Contains("fixed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ShouldRefuseAFixedEdge_BesideAConditionalOne()
    {
        var faults = GraphTopologyValidator.Validate(Topology(
            ["a"], Fixed(Start, "a"), Fixed("a", End), Conditional("a", "a", End)));

        Assert.Contains(faults, f => f.Contains("'a'", StringComparison.Ordinal)
            && f.Contains("fixed", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("S")]
    [InlineData("E")]
    public void ShouldRefuseAReservedOrEmptyNodeId(string id)
    {
        var faults = GraphTopologyValidator.Validate(Topology(
            [id], Fixed(Start, End)));

        Assert.NotEmpty(faults);
    }

    [Fact]
    public void ShouldRefuseADuplicatedNodeId()
    {
        var faults = GraphTopologyValidator.Validate(Topology(
            ["a", "a"], Fixed(Start, "a"), Fixed("a", End)));

        Assert.Contains(faults, f => f.Contains("'a'", StringComparison.Ordinal)
            && f.Contains("twice", StringComparison.Ordinal));
    }

    [Fact]
    public void ShouldTreatAnEdgeWithUnknownTargets_AsReachingEndAndEveryNode()
    {
        // A script's function edge, a conditional edge declared without possibleTargets.
        var faults = GraphTopologyValidator.Validate(Topology(
            ["a", "b"], Fixed(Start, "a"), Unknown("a"), Fixed("b", End)));

        Assert.Empty(faults);
    }

    [Fact]
    public void ShouldReturnEveryFault_NotTheFirst()
    {
        var faults = GraphTopologyValidator.Validate(Topology(
            ["a", "island", "dead"], Fixed(Start, "a"), Fixed("a", "ghost"), Fixed("island", "island")));

        Assert.True(faults.Count >= 4, string.Join(" | ", faults));
        Assert.Contains(faults, f => f.Contains("'ghost'", StringComparison.Ordinal));
        Assert.Contains(faults, f => f.Contains("'island'", StringComparison.Ordinal));
        Assert.Contains(faults, f => f.Contains("'dead' has no outgoing edge", StringComparison.Ordinal));
    }

    private sealed class State;

    [Fact]
    public void Compile_ShouldRefuseAnOrphan_WithTheValidatorsText()
    {
        var graph = new StateGraph<State>()
            .AddNode("a", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddNode("island", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddEdge(StateGraph<State>.StartNode, "a")
            .AddEdge("a", StateGraph<State>.EndNode)
            .AddEdge("island", StateGraph<State>.EndNode);

        var ex = Assert.Throws<InvalidOperationException>(() => graph.Compile());

        Assert.Contains("'island'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("orphan", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compile_ShouldRefuseAGraphWithNoPathToEnd()
    {
        var graph = new StateGraph<State>()
            .AddNode("a", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddEdge(StateGraph<State>.StartNode, "a")
            .AddEdge("a", "a");

        var ex = Assert.Throws<InvalidOperationException>(() => graph.Compile());

        Assert.Contains("no path from START to END", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_ShouldAcceptAConditionalEdgeWithoutDeclaredTargets()
    {
        var graph = new StateGraph<State>()
            .AddNode("a", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddNode("b", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddEdge(StateGraph<State>.StartNode, "a")
            .AddConditionalEdge("a", _ => "b")
            .AddEdge("b", StateGraph<State>.EndNode);

        Assert.NotNull(graph.Compile());
    }

    [Fact]
    public async System.Threading.Tasks.Task OnNodeEntered_ShouldReportEachVisit_InOrder()
    {
        var graph = new StateGraph<State>(new CircuitBreakerPolicy { MaxTransitions = 20, MaxStateVisits = 10 })
            .AddNode("a", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddNode("b", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddEdge(StateGraph<State>.StartNode, "a")
            .AddEdge("a", "b");
        var bVisits = 0;
        graph.AddConditionalEdge("b", _ => ++bVisits < 2 ? "a" : StateGraph<State>.EndNode, ["a", StateGraph<State>.EndNode]);
        var runner = graph.Compile();
        var entered = new List<(string Node, int Visit)>();
        runner.OnNodeEntered += (_, e) => entered.Add((e.NodeName, e.Visit));

        await runner.RunAsync(new State(), TestContext.Current.CancellationToken);

        Assert.Equal([("a", 1), ("b", 1), ("a", 2), ("b", 2)], entered);
    }
}
