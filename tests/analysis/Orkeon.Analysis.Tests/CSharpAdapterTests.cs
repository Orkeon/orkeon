using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests;

public class CSharpAdapterTests
{
    private static IReadOnlyList<Abstractions.Models.RaggableNode> Extract(string source, string path = "/r/a.cs")
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new CSharpAdapter(), pool);
        return mapper.ExtractNodes(path, source);
    }

    [Theory]
    [InlineData("class_declaration", UniversalNodeKind.Class)]
    [InlineData("struct_declaration", UniversalNodeKind.Struct)]
    [InlineData("record_declaration", UniversalNodeKind.Record)]
    [InlineData("interface_declaration", UniversalNodeKind.Interface)]
    [InlineData("enum_declaration", UniversalNodeKind.Enum)]
    [InlineData("method_declaration", UniversalNodeKind.Method)]
    [InlineData("constructor_declaration", UniversalNodeKind.Constructor)]
    [InlineData("property_declaration", UniversalNodeKind.Property)]
    [InlineData("field_declaration", UniversalNodeKind.Field)]
    [InlineData("namespace_declaration", UniversalNodeKind.Namespace)]
    [InlineData("random", UniversalNodeKind.Unknown)]
    public void Maps_tree_sitter_types_to_universal_kinds(string tsType, UniversalNodeKind expected)
    {
        Assert.Equal(expected, new CSharpAdapter().MapNodeKind(tsType));
    }

    [Fact]
    public void Extracts_class_methods_record_struct_interface_enum()
    {
        var nodes = Extract(TestFixtures.CSharpController);
        Assert.Contains(nodes, n => n.Name == "ItemsController" && n.Kind == UniversalNodeKind.Class);
        Assert.Contains(nodes, n => n.Name == "ItemDto" && n.Kind == UniversalNodeKind.Record);
        Assert.Contains(nodes, n => n.Name == "Point" && n.Kind == UniversalNodeKind.Struct);
        Assert.Contains(nodes, n => n.Name == "IItems" && n.Kind == UniversalNodeKind.Interface);
        Assert.Contains(nodes, n => n.Name == "Status" && n.Kind == UniversalNodeKind.Enum);
        Assert.Contains(nodes, n => n.Name == "GetAll" && n.Kind == UniversalNodeKind.Method);
    }

    [Fact]
    public void Xml_doc_comment_attached()
    {
        var nodes = Extract(TestFixtures.CSharpController);
        var ctrl = nodes.First(n => n.Name == "ItemsController");
        Assert.Contains("Sample API controller", ctrl.DocComment);
    }

    [Fact]
    public void Http_attributes_captured_as_decorators()
    {
        var nodes = Extract(TestFixtures.CSharpController);
        var getAll = nodes.First(n => n.Name == "GetAll");
        Assert.Contains(getAll.Decorators, d => d.Contains("HttpGet"));
    }

    [Fact]
    public void Multiple_attribute_lists_preserved()
    {
        var nodes = Extract(TestFixtures.CSharpController);
        var create = nodes.First(n => n.Name == "Create");
        Assert.Contains(create.Decorators, d => d.Contains("HttpPost"));
        Assert.Contains(create.Decorators, d => d.Contains("Authorize"));
    }

    [Fact]
    public void Namespace_creates_parent_hierarchy()
    {
        var nodes = Extract(TestFixtures.CSharpController);
        var ns = nodes.First(n => n.Kind == UniversalNodeKind.Namespace);
        Assert.Equal("Example.Api", ns.Name);
        var ctrl = nodes.First(n => n.Name == "ItemsController");
        Assert.Equal(ns.Id, ctrl.ParentId);
    }

    [Fact]
    public void ResolveImportPath_returns_null_for_csharp()
    {
        var adapter = new CSharpAdapter();
        Assert.Null(adapter.ResolveImportPath("System.Text", "/r/a.cs"));
    }
}
