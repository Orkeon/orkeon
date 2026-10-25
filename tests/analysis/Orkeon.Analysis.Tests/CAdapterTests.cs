using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Adapters;

namespace Orkeon.Analysis.Tests;

public class CAdapterTests
{
    private static IReadOnlyList<Abstractions.Models.RaggableNode> Extract() => AdapterHarness.Extract(new CAdapter(), "/r/a.c", TestFixtures.CLib);

    [Theory]
    [InlineData("add", UniversalNodeKind.Function)]
    [InlineData("helper", UniversalNodeKind.Function)]
    [InlineData("point", UniversalNodeKind.Struct)]
    [InlineData("point_t", UniversalNodeKind.Type)]
    [InlineData("color", UniversalNodeKind.Enum)]
    [InlineData("MAX", UniversalNodeKind.Constant)]
    public void Declarations_get_their_kind(string name, UniversalNodeKind kind)
    {
        Assert.Contains(Extract(), n => n.Name == name && n.Kind == kind);
    }

    [Fact]
    public void Struct_used_as_a_type_is_not_a_declaration()
    {
        Assert.Single(Extract(), n => n.Name == "point");
    }

    [Fact]
    public void Prototype_keeps_its_storage_class()
    {
        var helper = Extract().Single(n => n.Name == "helper");
        Assert.Contains("static", helper.Modifiers);
        Assert.Equal("static int helper(int a)", helper.Signature);
    }

    [Fact]
    public void Signature_stops_before_the_body()
    {
        Assert.Equal("int add(int a, int b)", Extract().Single(n => n.Name == "add").Signature);
    }

    [Fact]
    public void Doc_comment_attached_only_when_it_is_a_doc_comment()
    {
        var nodes = Extract();
        Assert.Contains("Adds two numbers", nodes.Single(n => n.Name == "add").DocComment);
        Assert.True(string.IsNullOrEmpty(nodes.Single(n => n.Name == "point").DocComment));
    }

    [Fact]
    public void Function_body_yields_for_call_and_return()
    {
        var add = Extract().Single(n => n.Name == "add");
        Assert.Contains(add.Statements, s => s.Kind == StatementKind.For);
        Assert.Contains(add.Statements, s => s.Kind == StatementKind.FunctionCall);
        Assert.Contains(add.Statements, s => s.Kind == StatementKind.Return);
    }

    [Fact]
    public async Task Graph_links_local_include_and_calls()
    {
        var (nodes, edges) = await AdapterHarness.GraphAsync(new CAdapter(),
            ("/r/src/a.c", "#include <stdio.h>\n#include \"../inc/util.h\"\nint run(void) { return twice(2); }\n"),
            ("/r/inc/util.h", "int twice(int a) { return a * 2; }\n"));

        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Imports, "/r/src/a.c", "/r/inc/util.h"));
        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Calls, "run", "twice"));
    }

    [Fact]
    public void System_include_resolves_to_an_external_module()
    {
        Assert.Equal("ext::system::stdio.h", new CAdapter().ResolveImportPath("<stdio.h>", "/r/a.c"));
    }
}
