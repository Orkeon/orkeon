using Orkeon.Analysis.Adapters;

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
}
