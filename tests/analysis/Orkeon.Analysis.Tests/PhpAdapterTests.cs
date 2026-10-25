using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Adapters;

namespace Orkeon.Analysis.Tests;

public class PhpAdapterTests
{
    private static IReadOnlyList<Abstractions.Models.RaggableNode> Extract() => AdapterHarness.Extract(new PhpAdapter(), "/r/Circle.php", TestFixtures.PhpLib);

    [Theory]
    [InlineData("App\\Models", UniversalNodeKind.Namespace)]
    [InlineData("Circle", UniversalNodeKind.Class)]
    [InlineData("Named", UniversalNodeKind.Interface)]
    [InlineData("Greets", UniversalNodeKind.Trait)]
    [InlineData("Suit", UniversalNodeKind.Enum)]
    [InlineData("Hearts", UniversalNodeKind.Constant)]
    [InlineData("PI", UniversalNodeKind.Constant)]
    [InlineData("radius", UniversalNodeKind.Property)]
    [InlineData("__construct", UniversalNodeKind.Constructor)]
    [InlineData("area", UniversalNodeKind.Method)]
    [InlineData("helper", UniversalNodeKind.Function)]
    public void Declarations_get_their_kind(string name, UniversalNodeKind kind)
    {
        Assert.Contains(Extract(), n => n.Name == name && n.Kind == kind);
    }

    [Fact]
    public void Visibility_is_a_modifier()
    {
        Assert.Contains("private", Extract().Single(n => n.Name == "radius").Modifiers);
    }

    [Fact]
    public void Attribute_and_doc_comment_attached_to_class()
    {
        var circle = Extract().Single(n => n.Name == "Circle");
        Assert.Contains(circle.Decorators, d => d.Contains("Entity", StringComparison.Ordinal));
        Assert.Contains("A circle", circle.DocComment);
    }

    [Fact]
    public void Method_body_yields_foreach_and_try()
    {
        var area = Extract().Single(n => n.Name == "area");
        Assert.Contains(area.Statements, s => s.Kind == StatementKind.ForOf);
        Assert.Contains(area.Statements, s => s.Kind == StatementKind.TryCatch);
    }

    [Theory]
    [InlineData("require __DIR__ . '/lib/base.php';")]
    [InlineData("include \"lib/base.php\";")]
    public async Task Graph_links_dir_relative_and_double_quoted_requires(string line)
    {
        var (nodes, edges) = await AdapterHarness.GraphAsync(new PhpAdapter(),
            ("/r/a.php", "<?php\n" + line + "\n"),
            ("/r/lib/base.php", "<?php class Base {}\n"));

        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Imports, "/r/a.php", "/r/lib/base.php"));
    }

    [Fact]
    public async Task Graph_links_require_extends_implements_and_calls()
    {
        const string source = """
            <?php
            require_once 'lib/base.php';
            interface Shape {}
            class Square extends Base implements Shape
            {
                public function area() { return $this->unit(); }
            }
            """;
        var (nodes, edges) = await AdapterHarness.GraphAsync(new PhpAdapter(),
            ("/r/Square.php", source),
            ("/r/lib/base.php", "<?php class Base { public function unit() { return 1; } }\n"));

        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Imports, "/r/Square.php", "/r/lib/base.php"));
        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Extends, "Square", "Base"));
        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Implements, "Square", "Shape"));
        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Calls, "area", "unit"));
    }
}
