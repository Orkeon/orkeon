using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Adapters;

namespace Orkeon.Analysis.Tests;

public class RubyAdapterTests
{
    private static IReadOnlyList<Abstractions.Models.RaggableNode> Extract() => AdapterHarness.Extract(new RubyAdapter(), "/r/shapes.rb", TestFixtures.RubyLib);

    [Theory]
    [InlineData("Shapes", UniversalNodeKind.Module)]
    [InlineData("Circle", UniversalNodeKind.Class)]
    [InlineData("PI", UniversalNodeKind.Constant)]
    [InlineData("initialize", UniversalNodeKind.Constructor)]
    [InlineData("area", UniversalNodeKind.Method)]
    [InlineData("unit", UniversalNodeKind.Method)]
    [InlineData("helper", UniversalNodeKind.Function)]
    public void Declarations_get_their_kind(string name, UniversalNodeKind kind)
    {
        Assert.Contains(Extract(), n => n.Name == name && n.Kind == kind);
    }

    [Fact]
    public void Class_sits_in_its_module()
    {
        var nodes = Extract();
        Assert.Equal(nodes.Single(n => n.Name == "Shapes").Id, nodes.Single(n => n.Name == "Circle").ParentId);
    }

    [Fact]
    public void Signature_is_the_head_only()
    {
        var nodes = Extract();
        Assert.Equal("module Shapes", nodes.Single(n => n.Name == "Shapes").Signature);
        Assert.Equal("class Circle < Base", nodes.Single(n => n.Name == "Circle").Signature);
        Assert.Equal("def initialize(radius)", nodes.Single(n => n.Name == "initialize").Signature);
    }

    [Fact]
    public void Singleton_method_is_static()
    {
        Assert.Contains("static", Extract().Single(n => n.Name == "unit").Modifiers);
    }

    [Fact]
    public void Hash_comment_attached_to_class()
    {
        Assert.Equal("# A circle.", Extract().Single(n => n.Name == "Circle").DocComment);
    }

    [Fact]
    public void Method_body_yields_begin_rescue()
    {
        Assert.Contains(Extract().Single(n => n.Name == "area").Statements, s => s.Kind == StatementKind.TryCatch);
    }

    [Fact]
    public async Task Graph_links_require_relative_only_and_superclass()
    {
        var (nodes, edges) = await AdapterHarness.GraphAsync(new RubyAdapter(),
            ("/r/shapes.rb", TestFixtures.RubyLib),
            ("/r/util/io.rb", "module Io\nend\n"),
            ("/r/json.rb", "module Json\nend\n"),
            ("/r/base.rb", "class Base\nend\n"));

        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Imports, "/r/shapes.rb", "/r/util/io.rb"));
        Assert.False(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Imports, "/r/shapes.rb", "/r/json.rb"));
        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Extends, "Circle", "Base"));
    }
}
