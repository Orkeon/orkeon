using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests;

public class TreeSitterParserPoolTests
{
    [Fact]
    public void Parses_typescript_class()
    {
        using var pool = new TreeSitterParserPool();
        using var parsed = pool.Parse("class Foo { bar() { return 1; } }", "typescript");

        Assert.Empty(parsed.Diagnostics);
        Assert.Equal("program", parsed.Root.Type);
        Assert.Contains(parsed.Root.Children, c => c.Type == "class_declaration");
    }

    [Fact]
    public void Parses_python_function()
    {
        using var pool = new TreeSitterParserPool();
        using var parsed = pool.Parse("def foo():\n    pass\n", "python");

        Assert.Empty(parsed.Diagnostics);
        Assert.Contains(parsed.Root.Children, c => c.Type == "function_definition");
    }

    [Fact]
    public void Reports_diagnostic_on_unclosed_brace()
    {
        using var pool = new TreeSitterParserPool();
        using var parsed = pool.Parse("class Foo {", "typescript");

        Assert.NotEmpty(parsed.Diagnostics);
    }

    [Fact]
    public async Task Handles_concurrent_parses()
    {
        using var pool = new TreeSitterParserPool();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() =>
        {
            using var parsed = pool.Parse($"class C{i} {{ m() {{ return {i}; }} }}", "typescript");
            return parsed.Diagnostics.Length;
        })));
        Assert.All(results, r => Assert.Equal(0, r));
    }

    [Fact]
    public void CleanText_collapses_whitespace()
    {
        using var pool = new TreeSitterParserPool();
        using var parsed = pool.Parse("class   Foo\n{\n}\n", "typescript");
        var decl = parsed.Root.Children.First(c => c.Type == "class_declaration");
        Assert.Equal("class Foo { }", decl.CleanText());
    }

    [Fact]
    public void DescendantsOfType_enumerates_all_matches()
    {
        using var pool = new TreeSitterParserPool();
        using var parsed = pool.Parse("class A { a() {} } class B { b() {} }", "typescript");
        var classes = parsed.Root.DescendantsOfType("class_declaration").ToList();
        Assert.Equal(2, classes.Count);
    }
}
