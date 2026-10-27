using Orkeon.Constants.Llm;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// The catalogue is what the documentation counts: a provider added to the factory and missing
/// here, or listed in the wrong category, makes the published counts wrong.
/// </summary>
public sealed class ModelProviderCatalogTests
{
    [Fact]
    public void ShouldListTheFactoryKeysInOrder_WhenGenerationCategory()
    {
        Assert.Equal(
            LlmProviderKeys.All,
            ModelProviderCatalog.InCategory(ModelProviderCategory.Generation).Select(p => p.Key).ToList());
    }

    [Fact]
    public void ShouldListTheDecisionKeys_WhenDecisionCategory()
    {
        Assert.Equal(
            DecisionProviderKeys.All,
            ModelProviderCatalog.InCategory(ModelProviderCategory.Decision).Select(p => p.Key).ToList());
    }

    [Fact]
    public void ShouldNotServeGeneration_WhenDecisionProvider()
    {
        Assert.All(
            ModelProviderCatalog.InCategory(ModelProviderCategory.Decision),
            p => Assert.False(p.Serves(ModelProviderCategory.Generation)));
    }

    [Fact]
    public void ShouldHaveUniqueKeysAndACategory_WhenAnyProvider()
    {
        Assert.Equal(
            ModelProviderCatalog.All.Count,
            ModelProviderCatalog.All.Select(p => p.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ModelProviderCatalog.All, p => Assert.NotEmpty(p.Categories));
    }

    [Theory]
    [InlineData("google", LlmProviderKeys.Gemini)]
    [InlineData(" XAI ", LlmProviderKeys.Grok)]
    [InlineData("typesafe", DecisionProviderKeys.TypeSafe)]
    public void ShouldResolveAliasesAndCase_WhenFind(string key, string expected)
    {
        Assert.Equal(expected, ModelProviderCatalog.Find(key)?.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("custom")]
    public void ShouldReturnNull_WhenFindNoProvider(string? key)
    {
        Assert.Null(ModelProviderCatalog.Find(key));
    }

    [Fact]
    public void ShouldListKeyVariableAndEndpoint_WhenTypeSafe()
    {
        var typeSafe = ModelProviderCatalog.Find(DecisionProviderKeys.TypeSafe);

        Assert.NotNull(typeSafe);
        Assert.Equal("TYPESAFE_API_KEY", typeSafe.ApiKeyVariable);
        Assert.Equal("https://api.typesafe.ai/v1", typeSafe.DefaultBaseUrl);
    }
}
