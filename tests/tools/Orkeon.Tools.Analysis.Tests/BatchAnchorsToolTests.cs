using System.Collections.Immutable;
using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Tools.Analysis.Tests;

public class BatchAnchorsToolTests
{
    private static RaggableNode Fn(string fqn) => TestGraph.Symbol(fqn, $"/src/{fqn}.cs");

    private static ImmutableList<string> L(params string[] items) => [.. items];

    [Fact]
    [Trait("BR", "RT-36")]
    public async Task ShouldListSharedNodes_WhenTwoAnchorSetsOverlap()
    {
        var a = Fn("X.A");
        var b = Fn("X.B");
        var shared = Fn("X.Shared");
        using var tool = new BatchAnchorsTool(TestGraph.Store(
            [a, b, shared],
            [TestGraph.Edge(shared, a, EdgeKind.Calls), TestGraph.Edge(shared, b, EdgeKind.Calls)]));

        var resp = await tool.ExecuteTypedForTest(
            new BatchAnchorsRequest
            {
                Action = "overlap",
                A = new AnchorSet { Fqns = L("X.A") },
                B = new AnchorSet { Fqns = L("X.B") },
            },
            CancellationToken.None);

        Assert.Equal(["X.Shared"], resp.Shared);
        Assert.False(resp.Disjoint);
        Assert.False(resp.Truncated);
    }

    [Fact]
    [Trait("BR", "RT-36")]
    public async Task ShouldReportDisjoint_WhenClosuresShareNothing()
    {
        var c = Fn("Y.C");
        var d = Fn("Z.D");
        using var tool = new BatchAnchorsTool(TestGraph.Store([c, d], []));

        var resp = await tool.ExecuteTypedForTest(
            new BatchAnchorsRequest
            {
                Action = "overlap",
                A = new AnchorSet { Fqns = L("Y.C") },
                B = new AnchorSet { Fqns = L("Z.D") },
            },
            CancellationToken.None);

        Assert.True(resp.Disjoint);
        Assert.Empty(resp.Shared);
        Assert.False(resp.Truncated);
    }

    [Fact]
    [Trait("BR", "RT-36")]
    public async Task ShouldNotReportDisjoint_WhenSameUnresolvedAnchorGivenOnBothSides()
    {
        using var tool = new BatchAnchorsTool(TestGraph.Store([Fn("X.A")], []));

        var resp = await tool.ExecuteTypedForTest(
            new BatchAnchorsRequest
            {
                Action = "overlap",
                A = new AnchorSet { Files = L("src/X/New.cs") },
                B = new AnchorSet { Files = L("src/X/New.cs") },
            },
            CancellationToken.None);

        Assert.False(resp.Disjoint);
        Assert.Equal(["src/X/New.cs"], resp.Shared);
    }
}
