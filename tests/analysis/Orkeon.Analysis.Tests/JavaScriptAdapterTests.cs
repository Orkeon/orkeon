using Orkeon.Analysis.Abstractions;
using Orkeon.Analysis.Adapters;

namespace Orkeon.Analysis.Tests;

public class JavaScriptAdapterTests
{
    private static IReadOnlyList<Abstractions.Models.RaggableNode> Extract() => AdapterHarness.Extract(new JavaScriptAdapter(), "/r/a.js", TestFixtures.JsLib);

    [Fact]
    public void Classes_functions_and_methods_are_extracted()
    {
        var nodes = Extract();
        Assert.Contains(nodes, n => n.Name == "Animal" && n.Kind == UniversalNodeKind.Class);
        Assert.Contains(nodes, n => n.Name == "greet" && n.Kind == UniversalNodeKind.Function);
        Assert.Contains(nodes, n => n.Name == "speak" && n.Kind == UniversalNodeKind.Method);
    }

    [Fact]
    public void Arrow_function_bound_to_a_const_is_a_function()
    {
        var shout = Extract().Single(n => n.Name == "shout");
        Assert.Equal(UniversalNodeKind.Function, shout.Kind);
        Assert.Equal("shout = (text) =>", shout.Signature);
    }

    [Fact]
    public void Constructor_method_is_a_constructor()
    {
        Assert.Contains(Extract(), n => n.Name == "constructor" && n.Kind == UniversalNodeKind.Constructor);
    }

    [Fact]
    public void Locals_of_a_function_are_not_symbols()
    {
        var nodes = Extract();
        Assert.Contains(nodes, n => n.Name == "path" && n.Kind == UniversalNodeKind.Variable);
        Assert.DoesNotContain(nodes, n => n.Name == "item");
    }

    [Fact]
    public void Export_static_and_async_are_modifiers()
    {
        var nodes = Extract();
        Assert.Contains("export", nodes.Single(n => n.Name == "greet").Modifiers);
        Assert.Contains("static", nodes.Single(n => n.Name == "create").Modifiers);
        Assert.Contains("async", nodes.Single(n => n.Name == "fetch").Modifiers);
    }

    [Fact]
    public void Jsdoc_comment_attached_to_exported_function()
    {
        Assert.Contains("Greets a person", Extract().Single(n => n.Name == "greet").DocComment);
    }

    [Fact]
    public void Method_body_yields_loop_and_try()
    {
        var fetch = Extract().Single(n => n.Name == "fetch");
        Assert.Contains(fetch.Statements, s => s.Kind == StatementKind.ForOf);
        Assert.Contains(fetch.Statements, s => s.Kind == StatementKind.TryCatch);
    }

    [Fact]
    public void Signature_cut_holds_with_non_ascii_text()
    {
        var nodes = AdapterHarness.Extract(new JavaScriptAdapter(), "/r/u.js", "// é\nfunction café(prénom) { return prénom; }\n");
        Assert.Equal("function café(prénom)", nodes.Single(n => n.Name == "café").Signature);
    }

    [Fact]
    public async Task Graph_resolves_an_extensionless_import_of_a_jsx_file()
    {
        var (nodes, edges) = await AdapterHarness.GraphAsync(new JavaScriptAdapter(),
            ("/r/app.js", "import { Button } from './Button';\n"),
            ("/r/Button.jsx", "export function Button() { return null; }\n"));

        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Imports, "/r/app.js", "/r/Button.jsx"));
    }

    [Fact]
    public async Task Graph_links_import_require_call_and_extends()
    {
        var (nodes, edges) = await AdapterHarness.GraphAsync(new JavaScriptAdapter(),
            ("/r/a.js", TestFixtures.JsLib),
            ("/r/io.js", "export function readFile(p) { return p; }\n"),
            ("/r/b.js", "const a = require('./a.js');\n"));

        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Imports, "/r/a.js", "/r/io.js"));
        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Imports, "/r/b.js", "/r/a.js"));
        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Calls, "speak", "greet"));
        Assert.True(AdapterHarness.HasEdge(nodes, edges, EdgeKind.Extends, "Dog", "Animal"));
    }
}
