using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Adapters;

namespace Orkeon.Analysis.Tests;

public class CppAdapterTests
{
    private static IReadOnlyList<Abstractions.Models.RaggableNode> Extract() => AdapterHarness.Extract(new CppAdapter(), "/r/a.cpp", TestFixtures.CppLib);

    [Theory]
    [InlineData("geo", UniversalNodeKind.Namespace)]
    [InlineData("Shape", UniversalNodeKind.Class)]
    [InlineData("Circle", UniversalNodeKind.Struct)]
    [InlineData("~Shape", UniversalNodeKind.Destructor)]
    [InlineData("Circle", UniversalNodeKind.Constructor)]
    [InlineData("r_", UniversalNodeKind.Field)]
    [InlineData("twice", UniversalNodeKind.Function)]
    [InlineData("perimeter", UniversalNodeKind.Method)]
    public void Declarations_get_their_kind(string name, UniversalNodeKind kind)
    {
        Assert.Contains(Extract(), n => n.Name == name && n.Kind == kind);
    }

    [Fact]
    public void Member_declaration_and_definition_are_methods_of_their_class()
    {
        var nodes = Extract();
        var shape = nodes.Single(n => n.Name == "Shape");
        var circle = nodes.Single(n => n.Name == "Circle" && n.Kind == UniversalNodeKind.Struct);
        Assert.Contains(nodes, n => n.Name == "area" && n.Kind == UniversalNodeKind.Method && n.ParentId == shape.Id);
        Assert.Contains(nodes, n => n.Name == "area" && n.Kind == UniversalNodeKind.Method && n.ParentId == circle.Id);
    }

    [Fact]
    public void Access_label_and_specifiers_are_modifiers()
    {
        var nodes = Extract();
        var circle = nodes.Single(n => n.Name == "Circle" && n.Kind == UniversalNodeKind.Struct);
        Assert.Contains("private", nodes.Single(n => n.Name == "r_").Modifiers);
        Assert.Contains("override", nodes.Single(n => n.Name == "area" && n.ParentId == circle.Id).Modifiers);
        Assert.Contains("virtual", nodes.Single(n => n.Name == "~Shape").Modifiers);
    }

    [Fact]
    public void Template_function_signature_carries_its_template_head()
    {
        Assert.Equal("template <typename T> T twice(T value)", Extract().Single(n => n.Name == "twice").Signature);
    }

    [Fact]
    public void Triple_slash_doc_attached_to_class()
    {
        Assert.Contains("A drawable shape", Extract().Single(n => n.Name == "Shape").DocComment);
    }

    [Fact]
    public void Out_of_line_method_yields_range_loop_and_try()
    {
        var perimeter = Extract().Single(n => n.Name == "perimeter");
        Assert.Contains(perimeter.Statements, s => s.Kind == StatementKind.ForOf);
        Assert.Contains(perimeter.Statements, s => s.Kind == StatementKind.TryCatch);
    }

    [Fact]
    public async Task Graph_links_base_class_and_calls()
    {
        var (nodes, edges) = await AdapterHarness.GraphAsync(new CppAdapter(), ("/r/a.cpp", TestFixtures.CppLib));

        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Extends, "Circle", "Shape"));
        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Calls, "perimeter", "twice"));
    }
}
