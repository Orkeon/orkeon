using Jint;
using Jint.Native;
using Jint.Runtime;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Internal;
using Orkeon.Scripting.Orchestration;
using Orkeon.Scripting.Tests.Doubles;
using static Orkeon.Tests.Shared.Assertions.AssertEx;
using Orkeon.Scripting.Tests.Internal;

namespace Orkeon.Scripting.Tests.Orchestration;

/// <summary>
/// FWK-27 (ADR-018): <c>stateGraph(...)</c> hands its shape to the shared topology validator when
/// it is built, and the script's loop tells the host each time it enters a node.
/// </summary>
public sealed class JsStateGraphValidationTests
{
    private static Engine NewEngine(MockGraphNodeObserver? observer = null) =>
        new JsEngineFactory(hostPorts: new ScriptingHostPorts { GraphObserver = observer }).Create();

    private static InvalidScriptException Refused(string literal) =>
        ScriptRefusal.Bridged<InvalidScriptException>(() => NewEngine().Evaluate($"stateGraph({literal});"), "stateGraph");

    [Fact]
    [Trait("BR", "FWK-27")]
    public void ShouldRefuseAnOrphanNode_AtConstruction()
    {
        var ex = Refused("""
            { name: "x", nodes: { a: (s) => s, island: (s) => s },
              edges: { [START]: "a", a: END, island: END } }
            """);

        Assert.Contains("'island'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("orphan", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("BR", "FWK-27")]
    public void ShouldRefuseANodeWithoutOutgoingEdge_AtConstruction()
    {
        var ex = Refused("""
            { name: "x", nodes: { a: (s) => s, b: (s) => s },
              edges: { [START]: "a", a: "b" } }
            """);

        Assert.Contains("'b' has no outgoing edge", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-27")]
    public void ShouldRefuseAnEdgeFromAnUndeclaredNode_AtConstruction()
    {
        var ex = Refused("""
            { name: "x", nodes: { a: (s) => s },
              edges: { [START]: "a", a: END, phantom: END } }
            """);

        Assert.Contains("'phantom'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-27")]
    public void ShouldReportEveryFault_InOneMessage()
    {
        var ex = Refused("""
            { name: "x", nodes: { a: (s) => s, island: (s) => s },
              edges: { [START]: "a", a: "ghost", island: "island" } }
            """);

        Assert.Contains("'ghost'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'island'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "FWK-27")]
    public void ShouldAcceptANodeReachedOnlyByAFunctionEdge()
    {
        // The targets of a function edge are unknown: the validator assumes it may lead anywhere.
        var graph = NewEngine().Evaluate("""
            stateGraph({ name: "x", nodes: { a: (s) => s, b: (s) => s },
              edges: { [START]: "a", a: (s) => "b", b: END } });
            """);

        Assert.NotNull(graph.ToObject());
    }

    [Fact]
    [Trait("BR", "FWK-27")]
    public async Task ShouldTellTheObserverEachNodeEntered_WithItsVisit()
    {
        var observer = new MockGraphNodeObserver();
        var engine = NewEngine(observer);
        var graph = (JsStateGraph)(await engine.EvaluateAsync("""
            stateGraph({
                name: "loopy",
                nodes: { a: (s) => ({ n: s.n + 1 }), b: (s) => s },
                edges: { [START]: "a", a: (s) => s.n < 2 ? "a" : "b", b: END },
            });
            """, cancellationToken: TestContext.Current.CancellationToken)).ToObject()!;
        var input = await engine.EvaluateAsync("({ n: 0 })", cancellationToken: TestContext.Current.CancellationToken);

        var promise = engine.Invoke(graph.run, input);
        await promise.UnwrapIfPromiseAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [("a", 1), ("a", 2), ("b", 1)],
            observer.Entered.Select(e => (e.Node, e.Visit)).ToArray());
        Assert.All(observer.Entered, e => Assert.Equal("loopy", e.Graph));
        Assert.All(observer.Entered, e => Assert.Null(e.Task));
    }

    [Fact]
    [Trait("BR", "FWK-27")]
    public async Task ShouldRunWithoutAnObserver()
    {
        var engine = NewEngine();
        var graph = (JsStateGraph)(await engine.EvaluateAsync("""
            stateGraph({ name: "x", nodes: { a: (s) => s }, edges: { [START]: "a", a: END } });
            """, cancellationToken: TestContext.Current.CancellationToken)).ToObject()!;
        var input = await engine.EvaluateAsync("({})", cancellationToken: TestContext.Current.CancellationToken);

        var promise = engine.Invoke(graph.run, input);

        Assert.NotNull(await promise.UnwrapIfPromiseAsync(TestContext.Current.CancellationToken));
    }
}
