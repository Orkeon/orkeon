using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Analysis.Core;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests;

public sealed class AnchorsQueryTests
{
    private const string Root = "/src";

    private static RaggableNode Node(string fqn, string file, UniversalNodeKind kind, NodeLevel level) => new()
    {
        Id = fqn,
        Kind = kind,
        Name = fqn.Split('.')[^1],
        Fqn = fqn,
        VirtualFilePath = file,
        Range = new NodeRange(0, 0, 1, 5, 0),
        Level = level,
        Language = "csharp",
        SourceSnippet = string.Empty,
        Sha256 = string.Empty,
    };

    private static RaggableNode Method(string fqn, string file) => Node(fqn, file, UniversalNodeKind.Function, NodeLevel.L3_Symbol);

    private static RaggableNode Type(string fqn, string file) => Node(fqn, file, UniversalNodeKind.Class, NodeLevel.L3_Symbol);

    private static RaggableEdge E(RaggableNode from, RaggableNode to, EdgeKind kind) => new($"{from.Id}->{to.Id}:{kind}", from.Id, to.Id, kind);

    [Fact]
    [Trait("BR", "RT-36")]
    public async Task ShouldReturnContainsCallersAndTests_WhenAnchorsGiven()
    {
        var alpha = Type("A.Alpha", "/src/A/Alpha.cs");
        var foo = Method("A.Alpha.Foo", "/src/A/Alpha.cs");
        var bar = Method("A.Alpha.Bar", "/src/A/Alpha.cs");
        var betaModule = Node("/src/B/Beta.cs", "/src/B/Beta.cs", UniversalNodeKind.Module, NodeLevel.L2_Module);
        var beta = Type("B.Beta", "/src/B/Beta.cs");
        var run = Method("B.Beta.Run", "/src/B/Beta.cs");
        var caller = Method("C.Caller.Call", "/src/C/Caller.cs");
        var outer = Method("C.Outer.Wrap", "/src/C/Outer.cs");
        var test = Method("A.Tests.AlphaTests.Exercises", "/src/A.Tests/AlphaTests.cs");
        var other = Method("D.Other.Thing", "/src/D/Other.cs");
        var store = new InMemoryRaggableStore(
            [alpha, foo, bar, betaModule, beta, run, caller, outer, test, other],
            [
                E(alpha, foo, EdgeKind.Contains), E(alpha, bar, EdgeKind.Contains),
                E(betaModule, beta, EdgeKind.Contains), E(beta, run, EdgeKind.Contains),
                E(caller, foo, EdgeKind.Calls), E(outer, caller, EdgeKind.Calls),
                E(test, foo, EdgeKind.Tests), E(other, other, EdgeKind.Calls),
            ],
            new FakeFileSystemService());

        var closure = await AnchorsQuery.ClosureAsync(
            store, ["A.Alpha", "Nope.Missing"], ["B/Beta.cs"], Root, CancellationToken.None);

        Assert.Contains("A.Alpha.Foo", closure.Nodes);
        Assert.Contains("A.Alpha.Bar", closure.Nodes);
        Assert.Contains("B.Beta", closure.Nodes);
        Assert.Contains("B.Beta.Run", closure.Nodes);
        Assert.Contains("C.Caller.Call", closure.Nodes);
        Assert.Contains("A.Tests.AlphaTests.Exercises", closure.Nodes);
        Assert.DoesNotContain("C.Outer.Wrap", closure.Nodes);
        Assert.DoesNotContain("D.Other.Thing", closure.Nodes);
        Assert.Equal(["Nope.Missing"], closure.Unresolved);
        Assert.False(closure.Truncated);
    }
}
