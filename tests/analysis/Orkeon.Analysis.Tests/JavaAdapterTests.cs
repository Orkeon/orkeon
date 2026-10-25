using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Adapters;

namespace Orkeon.Analysis.Tests;

public class JavaAdapterTests
{
    private static IReadOnlyList<Abstractions.Models.RaggableNode> Extract() => AdapterHarness.Extract(new JavaAdapter(), "/r/Circle.java", TestFixtures.JavaLib);

    [Theory]
    [InlineData("Shape", UniversalNodeKind.Interface)]
    [InlineData("Color", UniversalNodeKind.Enum)]
    [InlineData("Circle", UniversalNodeKind.Class)]
    [InlineData("Point", UniversalNodeKind.Record)]
    [InlineData("radius", UniversalNodeKind.Field)]
    [InlineData("PI", UniversalNodeKind.Constant)]
    public void Declarations_get_their_kind(string name, UniversalNodeKind kind)
    {
        Assert.Contains(Extract(), n => n.Name == name && n.Kind == kind);
    }

    [Fact]
    public void Constructor_and_methods_sit_under_their_class()
    {
        var nodes = Extract();
        var circle = nodes.Single(n => n.Name == "Circle" && n.Kind == UniversalNodeKind.Class);
        Assert.Contains(nodes, n => n.Name == "Circle" && n.Kind == UniversalNodeKind.Constructor && n.ParentId == circle.Id);
        Assert.Contains(nodes, n => n.Name == "area" && n.Kind == UniversalNodeKind.Method && n.ParentId == circle.Id);
    }

    [Fact]
    public void Modifiers_are_read_from_the_modifiers_node()
    {
        var pi = Extract().Single(n => n.Name == "PI");
        Assert.Equal(["private", "static", "final"], pi.Modifiers);
    }

    [Fact]
    public void Annotation_belongs_to_its_own_declaration_only()
    {
        var nodes = Extract();
        Assert.Contains("@Deprecated", nodes.Single(n => n.Name == "Circle" && n.Kind == UniversalNodeKind.Class).Decorators);
        var area = nodes.Single(n => n.Name == "area" && n.Kind == UniversalNodeKind.Method && n.ParentId != null && n.Modifiers.Contains("public"));
        Assert.Equal(["@Override"], area.Decorators);
        Assert.Empty(nodes.Single(n => n.Name == "radius").Decorators);
    }

    [Fact]
    public void Javadoc_attached_to_interface()
    {
        Assert.Contains("A shape with an area", Extract().Single(n => n.Name == "Shape").DocComment);
    }

    [Fact]
    public void Method_body_yields_for_try_and_return()
    {
        var area = Extract().Single(n => n.Name == "area" && n.Modifiers.Contains("public"));
        Assert.Contains(area.Statements, s => s.Kind == StatementKind.For);
        Assert.Contains(area.Statements, s => s.Kind == StatementKind.TryCatch);
        Assert.Contains(area.Statements, s => s.Kind == StatementKind.Return);
    }

    [Fact]
    public async Task Graph_links_extends_implements_and_calls()
    {
        const string source = """
            interface Shape { double area(); }
            class Base { double unit() { return 1; } }
            class Square extends Base implements Shape {
                public double area() { return unit(); }
            }
            """;
        var (nodes, edges) = await AdapterHarness.GraphAsync(new JavaAdapter(), ("/r/Square.java", source));

        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Extends, "Square", "Base"));
        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Implements, "Square", "Shape"));
        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Calls, "area", "unit"));
    }
}
