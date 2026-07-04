using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Adapters;

namespace Orkeon.Analysis.Tests;

public class TypeScriptAdapterTests
{
    [Theory]
    [InlineData("class_declaration", UniversalNodeKind.Class)]
    [InlineData("interface_declaration", UniversalNodeKind.Interface)]
    [InlineData("type_alias_declaration", UniversalNodeKind.Type)]
    [InlineData("enum_declaration", UniversalNodeKind.Enum)]
    [InlineData("function_declaration", UniversalNodeKind.Function)]
    [InlineData("method_definition", UniversalNodeKind.Method)]
    [InlineData("public_field_definition", UniversalNodeKind.Property)]
    [InlineData("decorator", UniversalNodeKind.Decorator)]
    [InlineData("random", UniversalNodeKind.Unknown)]
    public void Maps_tree_sitter_types_to_universal_kinds(string tsType, UniversalNodeKind expected)
    {
        var adapter = new TypeScriptAdapter();
        Assert.Equal(expected, adapter.MapNodeKind(tsType));
    }

    [Fact]
    public void Resolves_npm_import_to_external_fqn()
    {
        var adapter = new TypeScriptAdapter();
        var result = adapter.ResolveImportPath("lodash", "/repo/src/a.ts");
        Assert.Equal("ext::npm::lodash", result);
    }

    [Fact]
    public void Resolves_scoped_npm_import()
    {
        var adapter = new TypeScriptAdapter();
        var result = adapter.ResolveImportPath("@scope/pkg", "/repo/src/a.ts");
        Assert.Equal("ext::npm::@scope/pkg", result);
    }

    [Fact]
    public void Resolves_relative_import_to_virtual_bare_path()
    {
        // Post VFS-v2.2 fix (P2-RT-FIX-08): the adapter now returns the bare virtual path
        // without extension. Extension selection (.ts/.tsx/index.ts barrels) is done by
        // DependencyGraphBuilder.ResolveInIndex against the in-memory VFS index, which is
        // VFS-safe (no File.Exists on virtual paths).
        var adapter = new TypeScriptAdapter();
        var result = adapter.ResolveImportPath("./utils", "/repo/src/a.ts");
        Assert.NotNull(result);
        // Bare path, no extension appended
        Assert.EndsWith("/repo/src/utils", result);
    }
}
