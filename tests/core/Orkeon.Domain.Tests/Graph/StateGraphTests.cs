using Orkeon.Domain.Common.StateMachine;
using Orkeon.Domain.Graph;

namespace Orkeon.Domain.Tests.Graph;

/// <summary>
/// Tests for the LangGraph-style StateGraph engine.
/// Covers: linear flow, conditional routing, cycle control, circuit breaker.
/// </summary>
public class StateGraphTests
{
    #region Test State

    private sealed class PipelineState
    {
        public List<string> Log { get; init; } = [];
        public int Counter { get; set; }
        public bool ShouldLoop { get; set; }
        public bool ShouldFail { get; set; }
    }

    #endregion

    #region Linear Flow

    [Fact]
    public async System.Threading.Tasks.Task LinearGraph_ExecutesNodesInOrder()
    {
        // Arrange: START → A → B → C → END
        var graph = new StateGraph<PipelineState>(CircuitBreakerPolicy.Permissive)
            .AddNode("A", (s, _) => { s.Log.Add("A"); return System.Threading.Tasks.Task.FromResult(s); })
            .AddNode("B", (s, _) => { s.Log.Add("B"); return System.Threading.Tasks.Task.FromResult(s); })
            .AddNode("C", (s, _) => { s.Log.Add("C"); return System.Threading.Tasks.Task.FromResult(s); })
            .AddEdge(StateGraph<PipelineState>.StartNode, "A")
            .AddEdge("A", "B")
            .AddEdge("B", "C")
            .AddEdge("C", StateGraph<PipelineState>.EndNode);

        var runner = graph.Compile();
        var state = new PipelineState();

        // Act
        var result = await runner.RunAsync(state, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["A", "B", "C"], result.FinalState.Log);
        Assert.Equal(["A", "B", "C"], result.Trace);
        Assert.Equal(3, result.TotalTransitions);
    }

    [Fact]
    public async System.Threading.Tasks.Task SingleNode_ExecutesAndReturns()
    {
        // START → only → END
        var graph = new StateGraph<PipelineState>(CircuitBreakerPolicy.Permissive)
            .AddNode("only", (s, _) => { s.Log.Add("only"); return System.Threading.Tasks.Task.FromResult(s); })
            .AddEdge(StateGraph<PipelineState>.StartNode, "only")
            .AddEdge("only", StateGraph<PipelineState>.EndNode);

        var result = await graph.Compile().RunAsync(new PipelineState(), TestContext.Current.CancellationToken);

        Assert.Equal(["only"], result.FinalState.Log);
        Assert.Equal(1, result.TotalTransitions);
    }

    #endregion

    #region Conditional Routing

    [Fact]
    public async System.Threading.Tasks.Task ConditionalEdge_RoutesBranch()
    {
        // START → check → [if Counter > 0: "positive", else: "negative"] → END
        var graph = new StateGraph<PipelineState>(CircuitBreakerPolicy.Permissive)
            .AddNode("check", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddNode("positive", (s, _) => { s.Log.Add("positive"); return System.Threading.Tasks.Task.FromResult(s); })
            .AddNode("negative", (s, _) => { s.Log.Add("negative"); return System.Threading.Tasks.Task.FromResult(s); })
            .AddEdge(StateGraph<PipelineState>.StartNode, "check")
            .AddConditionalEdge("check",
                s => s.Counter > 0 ? "positive" : "negative",
                ["positive", "negative"])
            .AddEdge("positive", StateGraph<PipelineState>.EndNode)
            .AddEdge("negative", StateGraph<PipelineState>.EndNode);

        var runner = graph.Compile();

        // Route to positive
        var r1 = await runner.RunAsync(new PipelineState { Counter = 5 }, TestContext.Current.CancellationToken);
        Assert.Equal(["positive"], r1.FinalState.Log);

        // Route to negative
        var r2 = await runner.RunAsync(new PipelineState { Counter = -1 }, TestContext.Current.CancellationToken);
        Assert.Equal(["negative"], r2.FinalState.Log);
    }

    [Fact]
    public async System.Threading.Tasks.Task ConditionalEdge_CanRouteToEnd()
    {
        // START → decide → [END or "extra"] → END
        var graph = new StateGraph<PipelineState>(CircuitBreakerPolicy.Permissive)
            .AddNode("decide", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddNode("extra", (s, _) => { s.Log.Add("extra"); return System.Threading.Tasks.Task.FromResult(s); })
            .AddEdge(StateGraph<PipelineState>.StartNode, "decide")
            .AddConditionalEdge("decide",
                s => s.Counter == 0
                    ? StateGraph<PipelineState>.EndNode
                    : "extra",
                ["extra", StateGraph<PipelineState>.EndNode])
            .AddEdge("extra", StateGraph<PipelineState>.EndNode);

        var result = await graph.Compile().RunAsync(new PipelineState { Counter = 0 }, TestContext.Current.CancellationToken);
        Assert.Empty(result.FinalState.Log);
        Assert.Equal(["decide"], result.Trace);
    }

    #endregion

    #region Controlled Cycles

    [Fact]
    public async System.Threading.Tasks.Task ControlledLoop_ExecutesUntilConditionMet()
    {
        // START → increment → route → [increment | END]
        // Loop 3 times then exit
        var graph = new StateGraph<PipelineState>(CircuitBreakerPolicy.Permissive)
            .AddNode("increment", (s, _) =>
            {
                s.Counter++;
                s.Log.Add($"inc:{s.Counter}");
                return System.Threading.Tasks.Task.FromResult(s);
            })
            .AddNode("route", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddEdge(StateGraph<PipelineState>.StartNode, "increment")
            .AddEdge("increment", "route")
            .AddConditionalEdge("route",
                s => s.Counter < 3 ? "increment" : StateGraph<PipelineState>.EndNode,
                ["increment", StateGraph<PipelineState>.EndNode]);

        var result = await graph.Compile().RunAsync(new PipelineState(), TestContext.Current.CancellationToken);

        Assert.Equal(3, result.FinalState.Counter);
        Assert.Equal(["inc:1", "inc:2", "inc:3"], result.FinalState.Log);
        // 3 increment + 3 route = 6 transitions
        Assert.Equal(6, result.TotalTransitions);
    }

    #endregion

    #region Circuit Breaker

    [Fact]
    public async System.Threading.Tasks.Task CircuitBreaker_TripsOnInfiniteLoop()
    {
        // Infinite loop: START → loop → loop → loop → ...
        var policy = new CircuitBreakerPolicy
        {
            MaxTransitions = 10,
            MaxStateVisits = 0, // disable visit check for this test
            StateTimeout = TimeSpan.Zero,
            MaxTotalDuration = TimeSpan.Zero
        };

        var graph = new StateGraph<PipelineState>(policy)
            .AddNode("loop", (s, _) => { s.Counter++; return System.Threading.Tasks.Task.FromResult(s); })
            .AddNode("route", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddEdge(StateGraph<PipelineState>.StartNode, "loop")
            .AddEdge("loop", "route")
            .AddConditionalEdge("route",
                _ => "loop", // always loop
                ["loop", StateGraph<PipelineState>.EndNode]);

        var ex = await Assert.ThrowsAsync<GraphCircuitBrokenException>(
            () => graph.Compile().RunAsync(new PipelineState(), TestContext.Current.CancellationToken));

        Assert.Contains("Max transitions exceeded", ex.Message);
        Assert.True(ex.TransitionCount >= 10);
    }

    [Fact]
    public async System.Threading.Tasks.Task CircuitBreaker_TripsOnCycleDetection()
    {
        var policy = new CircuitBreakerPolicy
        {
            MaxTransitions = 100,
            MaxStateVisits = 3, // trip after 3 visits to same node
            StateTimeout = TimeSpan.Zero,
            MaxTotalDuration = TimeSpan.Zero
        };

        var graph = new StateGraph<PipelineState>(policy)
            .AddNode("worker", (s, _) => { s.Counter++; return System.Threading.Tasks.Task.FromResult(s); })
            .AddNode("route", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddEdge(StateGraph<PipelineState>.StartNode, "worker")
            .AddEdge("worker", "route")
            .AddConditionalEdge("route", _ => "worker", ["worker", StateGraph<PipelineState>.EndNode]);

        var ex = await Assert.ThrowsAsync<GraphCircuitBrokenException>(
            () => graph.Compile().RunAsync(new PipelineState(), TestContext.Current.CancellationToken));

        Assert.Contains("Cycle detected", ex.Message);
    }

    #endregion

    #region Observability

    [Fact]
    public async System.Threading.Tasks.Task OnNodeCompleted_FiringForEachNode()
    {
        var graph = new StateGraph<PipelineState>(CircuitBreakerPolicy.Permissive)
            .AddNode("A", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddNode("B", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddEdge(StateGraph<PipelineState>.StartNode, "A")
            .AddEdge("A", "B")
            .AddEdge("B", StateGraph<PipelineState>.EndNode);

        var runner = graph.Compile();
        var observedNodes = new List<string>();

        runner.OnNodeCompleted += (_, args) => observedNodes.Add(args.NodeName);

        await runner.RunAsync(new PipelineState(), TestContext.Current.CancellationToken);

        Assert.Equal(["A", "B"], observedNodes);
    }

    [Fact]
    public async System.Threading.Tasks.Task OnCircuitBroken_FiringOnTrip()
    {
        var policy = new CircuitBreakerPolicy
        {
            MaxTransitions = 2,
            MaxStateVisits = 0,
            StateTimeout = TimeSpan.Zero,
            MaxTotalDuration = TimeSpan.Zero
        };

        var graph = new StateGraph<PipelineState>(policy)
            .AddNode("loop", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddNode("route", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddEdge(StateGraph<PipelineState>.StartNode, "loop")
            .AddEdge("loop", "route")
            .AddConditionalEdge("route", _ => "loop", ["loop", StateGraph<PipelineState>.EndNode]);

        var runner = graph.Compile();
        string? brokenReason = null;
        runner.OnCircuitBroken += (_, args) => brokenReason = args.Reason;

        await Assert.ThrowsAsync<GraphCircuitBrokenException>(
            () => runner.RunAsync(new PipelineState(), TestContext.Current.CancellationToken));

        Assert.NotNull(brokenReason);
        Assert.Contains("Max transitions", brokenReason);
    }

    #endregion

    #region Cancellation

    [Fact]
    public async System.Threading.Tasks.Task Cancellation_StopsExecution()
    {
        using var cts = new CancellationTokenSource();

        var graph = new StateGraph<PipelineState>(CircuitBreakerPolicy.Permissive)
            .AddNode("step", async (s, _) =>
            {
                s.Counter++;
                if (s.Counter >= 2) await cts.CancelAsync();
                return s;
            })
            .AddNode("route", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddEdge(StateGraph<PipelineState>.StartNode, "step")
            .AddEdge("step", "route")
            .AddConditionalEdge("route", _ => "step", ["step", StateGraph<PipelineState>.EndNode]);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => graph.Compile().RunAsync(new PipelineState(), cts.Token));
    }

    #endregion

    #region Validation

    [Fact]
    public void Compile_FailsWithoutStartEdge()
    {
        var graph = new StateGraph<PipelineState>()
            .AddNode("A", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddEdge("A", StateGraph<PipelineState>.EndNode);

        Assert.Throws<InvalidOperationException>(() => graph.Compile());
    }

    [Fact]
    public void Compile_FailsWhenNodeHasNoOutgoingEdge()
    {
        var graph = new StateGraph<PipelineState>()
            .AddNode("A", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddEdge(StateGraph<PipelineState>.StartNode, "A");
        // Missing: A → END

        Assert.Throws<InvalidOperationException>(() => graph.Compile());
    }

    [Fact]
    public void AddNode_RejectsDuplicateName()
    {
        var graph = new StateGraph<PipelineState>()
            .AddNode("A", (s, _) => System.Threading.Tasks.Task.FromResult(s));

        Assert.Throws<ArgumentException>(() =>
            graph.AddNode("A", (s, _) => System.Threading.Tasks.Task.FromResult(s)));
    }

    [Fact]
    public void AddNode_RejectsReservedNames()
    {
        var graph = new StateGraph<PipelineState>();

        Assert.Throws<ArgumentException>(() =>
            graph.AddNode(StateGraph<PipelineState>.StartNode, (s, _) => System.Threading.Tasks.Task.FromResult(s)));

        Assert.Throws<ArgumentException>(() =>
            graph.AddNode(StateGraph<PipelineState>.EndNode, (s, _) => System.Threading.Tasks.Task.FromResult(s)));
    }

    [Fact]
    public void AddEdge_RejectsEdgeFromEnd()
    {
        var graph = new StateGraph<PipelineState>()
            .AddNode("A", (s, _) => System.Threading.Tasks.Task.FromResult(s));

        Assert.Throws<ArgumentException>(() =>
            graph.AddEdge(StateGraph<PipelineState>.EndNode, "A"));
    }

    [Fact]
    public void AddEdge_RejectsEdgeToStart()
    {
        var graph = new StateGraph<PipelineState>()
            .AddNode("A", (s, _) => System.Threading.Tasks.Task.FromResult(s));

        Assert.Throws<ArgumentException>(() =>
            graph.AddEdge("A", StateGraph<PipelineState>.StartNode));
    }

    [Fact]
    public async System.Threading.Tasks.Task ConditionalEdge_ThrowsOnUndeclaredTarget()
    {
        var graph = new StateGraph<PipelineState>(CircuitBreakerPolicy.Permissive)
            .AddNode("check", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddNode("A", (s, _) => System.Threading.Tasks.Task.FromResult(s))
            .AddEdge(StateGraph<PipelineState>.StartNode, "check")
            .AddConditionalEdge("check",
                _ => "B", // "B" is not in declared targets
                ["A", StateGraph<PipelineState>.EndNode])
            .AddEdge("A", StateGraph<PipelineState>.EndNode);

        var runner = graph.Compile();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(new PipelineState(), TestContext.Current.CancellationToken));
    }

    #endregion

    #region State Mutation

    [Fact]
    public async System.Threading.Tasks.Task StateFlowsThrough_AccumulatingChanges()
    {
        var graph = new StateGraph<PipelineState>(CircuitBreakerPolicy.Permissive)
            .AddNode("double", (s, _) => { s.Counter *= 2; return System.Threading.Tasks.Task.FromResult(s); })
            .AddNode("add_ten", (s, _) => { s.Counter += 10; return System.Threading.Tasks.Task.FromResult(s); })
            .AddEdge(StateGraph<PipelineState>.StartNode, "double")
            .AddEdge("double", "add_ten")
            .AddEdge("add_ten", StateGraph<PipelineState>.EndNode);

        var result = await graph.Compile().RunAsync(new PipelineState { Counter = 5 }, TestContext.Current.CancellationToken);

        // 5 * 2 = 10, 10 + 10 = 20
        Assert.Equal(20, result.FinalState.Counter);
    }

    #endregion
}
