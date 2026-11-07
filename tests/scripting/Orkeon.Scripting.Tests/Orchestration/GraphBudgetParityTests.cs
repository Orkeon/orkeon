using Jint;
using Jint.Native;
using Jint.Runtime;
using Orkeon.Domain.Common.StateMachine;
using Orkeon.Domain.Graph;
using Orkeon.Scripting.Internal;
using Orkeon.Scripting.Orchestration;

namespace Orkeon.Scripting.Tests.Orchestration;

/// <summary>
/// FWK-27 (ADR-018, point 4): on one node that loops, the script's <c>maxRetryCycles = k</c> and the
/// Domain engine's <c>MaxStateVisits = k + 1</c> are one bound: both let the node run k + 1 times
/// and trip on the next entry.
/// </summary>
public sealed class GraphBudgetParityTests
{
    private const int K = 2;

    private sealed class Loop
    {
        public int Runs { get; set; }
    }

    private static async Task<bool> DomainCompletesAsync(int runs, CancellationToken ct)
    {
        var graph = new StateGraph<Loop>(new CircuitBreakerPolicy { MaxTransitions = 1000, MaxStateVisits = K + 1 })
            .AddNode("a", (s, _) => { s.Runs++; return System.Threading.Tasks.Task.FromResult(s); })
            .AddEdge(StateGraph<Loop>.StartNode, "a")
            .AddConditionalEdge("a", s => s.Runs < runs ? "a" : StateGraph<Loop>.EndNode, ["a", StateGraph<Loop>.EndNode]);

        try
        {
            await graph.Compile().RunAsync(new Loop(), ct);
            return true;
        }
        catch (GraphCircuitBrokenException)
        {
            return false;
        }
    }

    private static async Task<bool> ScriptCompletesAsync(int runs, CancellationToken ct)
    {
        var engine = new JsEngineFactory().Create();
        var graph = (JsStateGraph)(await engine.EvaluateAsync($$"""
            stateGraph({
                name: "loop",
                nodes: { a: (s) => ({ runs: s.runs + 1 }) },
                edges: { [START]: "a", a: (s) => s.runs < {{runs}} ? "a" : END },
                graphConfig: { maxTransitions: 1000, maxStateVisits: 1000, maxRetryCycles: {{K}} },
            });
            """, cancellationToken: TestContext.Current.CancellationToken)).ToObject()!;
        var input = await engine.EvaluateAsync("({ runs: 0 })", cancellationToken: ct);

        try
        {
            await engine.Invoke(graph.run, input).UnwrapIfPromiseAsync(ct);
            return true;
        }
        catch (PromiseRejectedException ex) when (JsHostError.Unwrap(ex.RejectedValue) is InvalidOperationException)
        {
            return false;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [Trait("BR", "FWK-27")]
    public async Task ShouldTripOnTheSameEntry_WhenTheNodeLoops(int runs)
    {
        var ct = TestContext.Current.CancellationToken;

        var domain = await DomainCompletesAsync(runs, ct);
        var script = await ScriptCompletesAsync(runs, ct);

        Assert.Equal(runs <= K + 1, domain);
        Assert.Equal(domain, script);
    }
}
