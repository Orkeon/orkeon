using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Abstractions.DTOs.Tools;
using Orkeon.Analysis.Abstractions.Models;
using Orkeon.Tools.Analysis.Tests.Doubles;

namespace Orkeon.Tools.Analysis.Tests;

public class AccessCostToolTests
{
    private static RaggableNode Member(string fqn, string file, string receiver, string call)
    {
        var node = TestGraph.Symbol(fqn, file, UniversalNodeKind.Method, "csharp");
        node.StatementsMutable.Add(new StatementNode
        {
            Id = $"{fqn}#s1",
            ParentSymbolId = node.Id,
            Kind = StatementKind.Await,
            StartLine = 3,
            EndLine = 3,
            Expression = $"await {receiver}.{call}(ct)",
            Receiver = receiver,
        });
        return node;
    }

    [Fact]
    [Trait("BR", "RT-35")]
    public async Task ShouldReturnCostOfChangedMembers_WhenChangedFilesGiven()
    {
        var a = Member("/src/Orders/A::Handle", "/src/Orders/A.cs", "_orderRepository", "GetAsync");
        var b = Member("/src/Billing/B::Handle", "/src/Billing/B.cs", "_billRepository", "AddAsync");
        using var tool = new AccessCostTool(TestGraph.Store([a, b], []), new FakeGitDiffProvider());

        var resp = await tool.ExecuteTypedForTest(
            new AccessCostRequest { ChangedFiles = ["/src/Orders/A.cs"] }, CancellationToken.None);

        Assert.NotNull(resp.Report);
        var member = Assert.Single(resp.Report!.Members);
        Assert.Equal("/src/Orders/A::Handle", member.Member);
        var call = Assert.Single(member.PortCalls);
        Assert.Equal("_orderRepository", call.Receiver);
        Assert.Equal("GetAsync", call.Method);
    }

    [Fact]
    [Trait("BR", "RT-35")]
    public async Task ShouldRestrictToRoots_WhenRootsGiven()
    {
        var a = Member("/src/Orders/A::Handle", "/src/Orders/A.cs", "_orderRepository", "GetAsync");
        var b = Member("/src/Billing/B::Handle", "/src/Billing/B.cs", "_billRepository", "AddAsync");
        using var tool = new AccessCostTool(TestGraph.Store([a, b], []), new FakeGitDiffProvider());

        var resp = await tool.ExecuteTypedForTest(
            new AccessCostRequest
            {
                ChangedFiles = ["/src/Orders/A.cs", "/src/Billing/B.cs"],
                Roots = ["/src/Billing"],
            }, CancellationToken.None);

        Assert.NotNull(resp.Report);
        var member = Assert.Single(resp.Report!.Members);
        Assert.Equal("/src/Billing/B::Handle", member.Member);
    }

    [Fact]
    [Trait("BR", "RT-35")]
    public async Task ShouldRefuseRequest_WhenNeitherFqnsNorChangedFiles()
    {
        var a = Member("/src/Orders/A::Handle", "/src/Orders/A.cs", "_orderRepository", "GetAsync");
        var git = new FakeGitDiffProvider();
        using var tool = new AccessCostTool(TestGraph.Store([a], []), git);

        var resp = await tool.ExecuteTypedForTest(new AccessCostRequest(), CancellationToken.None);

        Assert.Null(resp.Report);
        Assert.Equal(
            ["Provide fqns or changed_files: neither was given and the working tree has no changes."],
            resp.Errors);
        Assert.Equal(1, git.WorkingTreeCallCount);
    }
}
