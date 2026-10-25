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

    /// <summary>One valid snippet per grammar the registry loads beyond the ones covered above.</summary>
    public static TheoryData<string, string> GrammarSnippets { get; } = new()
    {
        { "tsx", "export const A = () => <div>{1}</div>;\n" },
        { "javascript", "export function f(a) { return a + 1; }\n" },
        { "java", "class A { int f() { return 1; } }\n" },
        { "c", "int f(int a) { return a; }\n" },
        { "cpp", "namespace n { class A { public: int f(); }; }\n" },
        { "php", "<?php function f($a) { return $a; }\n" },
        { "ruby", "class A\n  def f; 1; end\nend\n" },
        { "bash", "f() { echo \"$1\"; }\n" },
        { "agda", "module A where\n" },
        { "css", "a { color: red; }\n" },
        { "embedded-template", "<% if x %>y<% end %>\n" },
        { "haskell", "f :: Int -> Int\nf x = x\n" },
        { "html", "<p>hi</p>\n" },
        { "jsdoc", "/**\n * Adds one.\n * @param {number} a\n */" },
        { "json", "{\"a\": [1, true, null]}\n" },
        { "julia", "function f(x)\n  x + 1\nend\n" },
        { "ql", "select 1\n" },
        { "razor", "<p>@Model.Name</p>\n" },
        { "scala", "object A { def f(x: Int): Int = x }\n" },
        { "verilog", "module m; endmodule\n" },
    };

    [Theory]
    [MemberData(nameof(GrammarSnippets))]
    public void Parses_every_registered_grammar(string language, string source)
    {
        using var pool = new TreeSitterParserPool();
        using var parsed = pool.Parse(source, language);

        Assert.Empty(parsed.Diagnostics);
        Assert.NotEmpty(parsed.Root.Children);
    }

    [Fact]
    public void Every_registered_grammar_has_a_parse_case()
    {
        var covered = GrammarSnippets.Select(row => row.Data.Item1)
            .Concat(["typescript", "python", "csharp", "go", "rust"]);

        Assert.Empty(LanguageRegistry.Supported.Except(covered));
    }

    [Fact]
    public void Parses_with_the_requested_grammar_when_grammars_carry_no_name()
    {
        // scala and cpp both report a null grammar name: a returned scala parser must not serve cpp.
        using var pool = new TreeSitterParserPool();
        using (pool.Parse("object A\n", "scala")) { }
        using var parsed = pool.Parse("int f();\n", "cpp");

        Assert.Equal("translation_unit", parsed.Root.Type);
    }

    [Fact]
    public void Keeps_every_grammar_apart_across_reuse()
    {
        using var pool = new TreeSitterParserPool();
        var rows = GrammarSnippets.Select(row => row.Data).ToList();
        var roots = rows.ToDictionary(r => r.Item1, r => { using var p = pool.Parse(r.Item2, r.Item1); return p.Root.Type; });

        foreach (var (language, source) in Enumerable.Reverse(rows))
        {
            using var again = pool.Parse(source, language);
            Assert.True(roots[language] == again.Root.Type, $"{language}: {again.Root.Type} instead of {roots[language]}");
        }
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
