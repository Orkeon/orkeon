using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests;

public class RustAdapterTests
{
    private static IReadOnlyList<Abstractions.Models.RaggableNode> Extract(string source, string path = "/r/a.rs")
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new RustAdapter(), pool);
        return mapper.ExtractNodes(path, source);
    }

    [Theory]
    [InlineData("function_item", UniversalNodeKind.Function)]
    [InlineData("struct_item", UniversalNodeKind.Struct)]
    [InlineData("enum_item", UniversalNodeKind.Enum)]
    [InlineData("trait_item", UniversalNodeKind.Trait)]
    [InlineData("type_item", UniversalNodeKind.Type)]
    [InlineData("const_item", UniversalNodeKind.Constant)]
    [InlineData("static_item", UniversalNodeKind.Variable)]
    [InlineData("mod_item", UniversalNodeKind.Namespace)]
    [InlineData("random", UniversalNodeKind.Unknown)]
    public void Maps_tree_sitter_types_to_universal_kinds(string tsType, UniversalNodeKind expected)
    {
        Assert.Equal(expected, new RustAdapter().MapNodeKind(tsType));
    }

    [Fact]
    public void Struct_item_extracted()
    {
        var nodes = Extract(TestFixtures.RustLib);
        Assert.Contains(nodes, n => n.Name == "Point" && n.Kind == UniversalNodeKind.Struct);
    }

    [Fact]
    public void Trait_item_extracted()
    {
        var nodes = Extract(TestFixtures.RustLib);
        Assert.Contains(nodes, n => n.Name == "Drawable" && n.Kind == UniversalNodeKind.Trait);
    }

    [Fact]
    public void Function_inside_impl_is_method()
    {
        var nodes = Extract(TestFixtures.RustLib);
        Assert.Contains(nodes, n => n.Name == "new" && n.Kind == UniversalNodeKind.Method);
    }

    [Fact]
    public void Top_level_function_stays_function()
    {
        var nodes = Extract(TestFixtures.RustLib);
        Assert.Contains(nodes, n => n.Name == "internal_helper" && n.Kind == UniversalNodeKind.Function);
    }

    [Fact]
    public void Pub_visibility_captured_as_modifier()
    {
        var nodes = Extract(TestFixtures.RustLib);
        var newFn = nodes.First(n => n.Name == "new");
        Assert.Contains(newFn.Modifiers, m => m == "pub");
    }

    [Fact]
    public void Pub_crate_visibility_captured()
    {
        var nodes = Extract(TestFixtures.RustLib);
        var fn = nodes.First(n => n.Name == "internal_helper");
        Assert.Contains(fn.Modifiers, m => m.StartsWith("pub(", StringComparison.Ordinal));
    }

    [Fact]
    public void Derive_attribute_captured_as_decorator()
    {
        var nodes = Extract(TestFixtures.RustLib);
        var point = nodes.First(n => n.Name == "Point" && n.Kind == UniversalNodeKind.Struct);
        Assert.Contains(point.Decorators, d => d.Contains("derive"));
    }

    [Fact]
    public void Mod_item_is_namespace()
    {
        var nodes = Extract(TestFixtures.RustLib);
        Assert.Contains(nodes, n => n.Name == "tests" && n.Kind == UniversalNodeKind.Namespace);
    }

    [Fact]
    public void Const_is_extracted()
    {
        var nodes = Extract(TestFixtures.RustLib);
        Assert.Contains(nodes, n => n.Name == "MAX" && n.Kind == UniversalNodeKind.Constant);
    }

    [Fact]
    public void Doc_comment_attached_to_struct()
    {
        var nodes = Extract(TestFixtures.RustLib);
        var point = nodes.First(n => n.Name == "Point" && n.Kind == UniversalNodeKind.Struct);
        Assert.Contains("point in 2D", point.DocComment);
    }
}
