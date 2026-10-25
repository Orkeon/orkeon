using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Adapters;

namespace Orkeon.Analysis.Tests;

public class BashAdapterTests
{
    private static IReadOnlyList<Abstractions.Models.RaggableNode> Extract() => AdapterHarness.Extract(new BashAdapter(), "/r/run.sh", TestFixtures.BashLib);

    [Fact]
    public void Both_function_forms_are_functions()
    {
        var nodes = Extract();
        Assert.Contains(nodes, n => n.Name == "greet" && n.Kind == UniversalNodeKind.Function);
        Assert.Contains(nodes, n => n.Name == "cleanup" && n.Kind == UniversalNodeKind.Function);
    }

    [Fact]
    public void Readonly_top_level_variable_is_a_constant()
    {
        var max = Extract().Single(n => n.Name == "MAX");
        Assert.Equal(UniversalNodeKind.Constant, max.Kind);
        Assert.Contains("readonly", max.Modifiers);
    }

    [Fact]
    public void Local_of_a_function_is_not_a_symbol()
    {
        Assert.DoesNotContain(Extract(), n => n.Name == "name");
    }

    [Fact]
    public void Comment_attached_to_function_and_shebang_never_is()
    {
        var nodes = Extract();
        Assert.Equal("# Greets a person.", nodes.Single(n => n.Name == "greet").DocComment);
        Assert.DoesNotContain(nodes, n => n.DocComment?.StartsWith("#!", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Function_body_yields_if_and_for()
    {
        var greet = Extract().Single(n => n.Name == "greet");
        Assert.Contains(greet.Statements, s => s.Kind == StatementKind.If);
        Assert.Contains(greet.Statements, s => s.Kind == StatementKind.ForIn);
    }

    [Fact]
    public async Task Graph_links_quoted_source_and_function_calls()
    {
        const string source = "source \"./lib/common.sh\"\nmain() { log hi; }\n";
        var (nodes, edges) = await AdapterHarness.GraphAsync(new BashAdapter(),
            ("/r/run.sh", source),
            ("/r/lib/common.sh", "log() { echo \"$1\"; }\n"));

        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Imports, "/r/run.sh", "/r/lib/common.sh"));
        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Calls, "main", "log"));
    }

    [Fact]
    public void Source_built_from_a_variable_is_not_resolved()
    {
        Assert.Null(new BashAdapter().ResolveImportPath("\"$HOME/env.sh\"", "/r/run.sh"));
    }
}
