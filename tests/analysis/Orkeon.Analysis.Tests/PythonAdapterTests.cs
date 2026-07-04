using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.Core;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests;

public class PythonAdapterTests
{
    private static IReadOnlyList<Abstractions.Models.RaggableNode> Extract(string source, string path = "/r/a.py")
    {
        using var pool = new TreeSitterParserPool();
        var mapper = new UniversalSemanticMapper(new PythonAdapter(), pool);
        return mapper.ExtractNodes(path, source);
    }

    [Theory]
    [InlineData("class_definition", UniversalNodeKind.Class)]
    [InlineData("function_definition", UniversalNodeKind.Function)]
    [InlineData("assignment", UniversalNodeKind.Variable)]
    [InlineData("decorator", UniversalNodeKind.Decorator)]
    [InlineData("random", UniversalNodeKind.Unknown)]
    public void Maps_tree_sitter_types_to_universal_kinds(string tsType, UniversalNodeKind expected)
    {
        Assert.Equal(expected, new PythonAdapter().MapNodeKind(tsType));
    }

    [Fact]
    public void Class_with_init_maps_to_constructor()
    {
        var nodes = Extract(TestFixtures.PythonSimpleClass);
        Assert.Contains(nodes, n => n.Name == "__init__" && n.Kind == UniversalNodeKind.Constructor);
    }

    [Fact]
    public void Class_with_del_maps_to_destructor()
    {
        var nodes = Extract(TestFixtures.PythonSimpleClass);
        Assert.Contains(nodes, n => n.Name == "__del__" && n.Kind == UniversalNodeKind.Destructor);
    }

    [Fact]
    public void Dunder_eq_maps_to_operator()
    {
        var nodes = Extract(TestFixtures.PythonSimpleClass);
        Assert.Contains(nodes, n => n.Name == "__eq__" && n.Kind == UniversalNodeKind.Operator);
    }

    [Fact]
    public void Class_method_maps_to_method()
    {
        var nodes = Extract(TestFixtures.PythonSimpleClass);
        Assert.Contains(nodes, n => n.Name == "create" && n.Kind == UniversalNodeKind.Method);
    }

    [Fact]
    public void Top_level_function_maps_to_function()
    {
        var nodes = Extract(TestFixtures.PythonSimpleClass);
        Assert.Contains(nodes, n => n.Name == "helper" && n.Kind == UniversalNodeKind.Function);
    }

    [Fact]
    public void Property_decorator_promotes_method_to_property()
    {
        var nodes = Extract(TestFixtures.PythonSimpleClass);
        Assert.Contains(nodes, n => n.Name == "connection" && n.Kind == UniversalNodeKind.Property);
    }

    [Fact]
    public void Abstract_class_maps_to_interface()
    {
        var nodes = Extract(TestFixtures.PythonSimpleClass);
        Assert.Contains(nodes, n => n.Name == "IRepository" && n.Kind == UniversalNodeKind.Interface);
    }

    [Fact]
    public void Top_level_caps_assignment_maps_to_constant()
    {
        var nodes = Extract(TestFixtures.PythonSimpleClass);
        Assert.Contains(nodes, n => n.Name == "MAX_RETRIES" && n.Kind == UniversalNodeKind.Constant);
    }

    [Fact]
    public void Top_level_lower_assignment_maps_to_variable()
    {
        var nodes = Extract(TestFixtures.PythonSimpleClass);
        Assert.Contains(nodes, n => n.Name == "debug_mode" && n.Kind == UniversalNodeKind.Variable);
    }

    [Fact]
    public void Docstring_attached_to_class()
    {
        var nodes = Extract(TestFixtures.PythonSimpleClass);
        var cls = nodes.First(n => n.Name == "UserService");
        Assert.Contains("Service for user", cls.DocComment);
    }

    [Fact]
    public void Docstring_attached_to_method()
    {
        var nodes = Extract(TestFixtures.PythonSimpleClass);
        var m = nodes.First(n => n.Name == "create");
        Assert.Contains("Create a user", m.DocComment);
    }

    [Fact]
    public void Pip_import_resolves_to_external()
    {
        var adapter = new PythonAdapter();
        var result = adapter.ResolveImportPath("numpy", "/r/a.py");
        Assert.Equal("ext::pypi::numpy", result);
    }

    [Fact]
    public void Namespaced_pip_import_uses_root_package()
    {
        var adapter = new PythonAdapter();
        var result = adapter.ResolveImportPath("numpy.linalg", "/r/a.py");
        Assert.Equal("ext::pypi::numpy", result);
    }

    [Fact]
    public void Relative_import_resolves_to_file()
    {
        var adapter = new PythonAdapter();
        var result = adapter.ResolveImportPath(".utils", "/r/pkg/a.py");
        Assert.NotNull(result);
        Assert.EndsWith("/r/pkg/utils.py", result);
    }
}
