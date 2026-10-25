using Orkeon.Analysis.Adapters;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests;

public class LanguageAdapterFactoryTests
{
    [Theory]
    [InlineData(".ts", "typescript")]
    [InlineData(".tsx", "typescript")]
    [InlineData(".py", "python")]
    [InlineData(".cs", "csharp")]
    [InlineData(".go", "go")]
    [InlineData(".rs", "rust")]
    [InlineData(".js", "javascript")]
    [InlineData(".mjs", "javascript")]
    [InlineData(".java", "java")]
    [InlineData(".c", "c")]
    [InlineData(".cpp", "cpp")]
    [InlineData(".h", "cpp")]
    [InlineData(".php", "php")]
    [InlineData(".rb", "ruby")]
    [InlineData(".sh", "bash")]
    public void GetByExtension_returns_matching_adapter(string ext, string language)
    {
        var adapter = LanguageAdapterFactory.GetByExtension(ext);
        Assert.NotNull(adapter);
        Assert.Equal(language, adapter!.LanguageName);
    }

    [Fact]
    public void GetByExtension_is_case_insensitive()
    {
        Assert.NotNull(LanguageAdapterFactory.GetByExtension(".TS"));
        Assert.NotNull(LanguageAdapterFactory.GetByExtension(".Cs"));
    }

    [Fact]
    public void GetByExtension_returns_null_for_unknown()
    {
        Assert.Null(LanguageAdapterFactory.GetByExtension(".unknown"));
    }

    [Fact]
    public void GetByExtensionOrThrow_throws_for_unknown()
    {
        Assert.Throws<NotSupportedException>(() => LanguageAdapterFactory.GetByExtensionOrThrow(".unknown"));
    }

    [Fact]
    public void GetAll_returns_at_least_five_adapters()
    {
        var all = LanguageAdapterFactory.GetAll();
        Assert.True(all.Count >= 5, $"Expected >= 5 adapters, got {all.Count}");
        Assert.Contains(all, a => a.LanguageName == "typescript");
        Assert.Contains(all, a => a.LanguageName == "python");
        Assert.Contains(all, a => a.LanguageName == "csharp");
        Assert.Contains(all, a => a.LanguageName == "go");
        Assert.Contains(all, a => a.LanguageName == "rust");
    }

    [Fact]
    public void Every_adapter_claims_the_extensions_the_registry_ships_for_it()
    {
        foreach (var adapter in LanguageAdapterFactory.GetAll())
        {
            var grammar = LanguageRegistry.Shipped.Single(g => g.Language == adapter.LanguageName);
            Assert.Equal(grammar.Extensions.Order(), adapter.FileExtensions.Order());
        }
    }

    [Fact]
    public void Every_adapter_query_compiles_against_its_grammar()
    {
        // The mapper degrades a query that fails to compile to "no captures": only this test sees it.
        var failures = new List<string>();
        foreach (var adapter in LanguageAdapterFactory.GetAll())
        {
            using var language = LanguageRegistry.Create(adapter.LanguageName);
            foreach (var (name, text) in new[]
            {
                ("declarations", adapter.DeclarationQuery), ("imports", adapter.ImportQuery), ("calls", adapter.CallQuery),
                ("inheritance", adapter.InheritanceQuery), ("doc comments", adapter.DocCommentQuery),
                ("decorators", adapter.DecoratorQuery), ("statements", adapter.StatementQuery),
            })
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                try { language.CreateQuery(text).Dispose(); }
                catch (InvalidOperationException ex) { failures.Add($"{adapter.LanguageName} {name}: {ex.Message}"); }
            }
        }

        Assert.Empty(failures);
    }
}
