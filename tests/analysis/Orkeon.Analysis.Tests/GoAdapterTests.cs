using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests;

public class GoAdapterTests
{
    private static IReadOnlyList<Abstractions.Models.RaggableNode> Extract(string source, string path = "/r/a.go")
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new GoAdapter(), pool);
        return mapper.ExtractNodes(path, source);
    }

    [Theory]
    [InlineData("function_declaration", UniversalNodeKind.Function)]
    [InlineData("method_declaration", UniversalNodeKind.Method)]
    [InlineData("type_spec", UniversalNodeKind.Type)]
    [InlineData("const_spec", UniversalNodeKind.Constant)]
    [InlineData("var_spec", UniversalNodeKind.Variable)]
    [InlineData("package_clause", UniversalNodeKind.Namespace)]
    [InlineData("random", UniversalNodeKind.Unknown)]
    public void Maps_tree_sitter_types_to_universal_kinds(string tsType, UniversalNodeKind expected)
    {
        Assert.Equal(expected, new GoAdapter().MapNodeKind(tsType));
    }

    [Fact]
    public void Struct_type_refined_from_type()
    {
        var nodes = Extract(TestFixtures.GoService);
        Assert.Contains(nodes, n => n.Name == "Server" && n.Kind == UniversalNodeKind.Struct);
    }

    [Fact]
    public void Interface_type_refined()
    {
        var nodes = Extract(TestFixtures.GoService);
        Assert.Contains(nodes, n => n.Name == "Handler" && n.Kind == UniversalNodeKind.Interface);
    }

    [Fact]
    public void Method_with_receiver_extracted()
    {
        var nodes = Extract(TestFixtures.GoService);
        Assert.Contains(nodes, n => n.Name == "Start" && n.Kind == UniversalNodeKind.Method);
    }

    [Fact]
    public void Uppercase_name_is_public()
    {
        var nodes = Extract(TestFixtures.GoService);
        var start = nodes.First(n => n.Name == "Start");
        Assert.Contains("public", start.Modifiers);
    }

    [Fact]
    public void Lowercase_name_is_private()
    {
        var nodes = Extract(TestFixtures.GoService);
        var priv = nodes.First(n => n.Name == "private");
        Assert.Contains("private", priv.Modifiers);
    }

    [Fact]
    public void Godoc_attached_to_struct()
    {
        var nodes = Extract(TestFixtures.GoService);
        var server = nodes.First(n => n.Name == "Server");
        Assert.Contains("Server handles", server.DocComment);
    }

    [Fact]
    public void Const_is_extracted()
    {
        var nodes = Extract(TestFixtures.GoService);
        Assert.Contains(nodes, n => n.Name == "MaxConnections" && n.Kind == UniversalNodeKind.Constant);
    }

    [Fact]
    public void Package_clause_is_namespace()
    {
        var nodes = Extract(TestFixtures.GoService);
        Assert.Contains(nodes, n => n.Name == "example" && n.Kind == UniversalNodeKind.Namespace);
    }

    [Fact]
    public void ResolveImportPath_returns_null_for_go()
    {
        var adapter = new GoAdapter();
        Assert.Null(adapter.ResolveImportPath("fmt", "/r/a.go"));
    }
}
