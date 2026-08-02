using Orkeon.Scripting.Cli.Commands;
using Orkeon.Scripting.Cli.Tests.Doubles;

namespace Orkeon.Scripting.Cli.Tests.Commands;

/// <summary>
/// Catalogue discovery behind <c>orkeon llm models</c>, which is what lets a campaign say
/// "every <c>gpt-5.6-*</c>" instead of pinning a list that goes stale.
/// </summary>
/// <remarks>
/// A hand-maintained model list is exactly what produced the four blocking gaps of the
/// 2026-07-27 audit, so the dialect handling here is worth pinning: four shapes, and one
/// provider that honestly has no catalogue at all.
/// </remarks>
public sealed class LlmCatalogClientTests
{
    private const string OpenAiBody =
        """{"object":"list","data":[{"id":"gpt-5.6-sol"},{"id":"gpt-5.6-luna"},{"id":"gpt-4"}]}""";

    private const string AnthropicBody =
        """{"data":[{"id":"claude-sonnet-5"},{"id":"claude-opus-5"}]}""";

    private const string OllamaBody =
        """{"models":[{"name":"llama3.2:latest"},{"name":"qwen3:8b"}]}""";

    private static async Task<IReadOnlyList<string>> ListAsync(
        StubHttpMessageHandler handler, string provider, string? apiKey = "unused-in-tests")
    {
        using var client = new HttpClient(handler);
        var baseUrl = LlmCatalogClient.ResolveBaseUrl(provider, overrideUrl: null);
        return await LlmCatalogClient.ListAsync(
            client, provider, baseUrl, apiKey, TestContext.Current.CancellationToken);
    }

    // ── Dialects ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ShouldReadTheOpenAiCatalogue_FromTheDataArray()
    {
        using var handler = new StubHttpMessageHandler(OpenAiBody);

        var models = await ListAsync(handler, "openai");

        Assert.Equal(["gpt-4", "gpt-5.6-luna", "gpt-5.6-sol"], models);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.openai.com/v1/models", request.RequestUri?.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
    }

    /// <summary>Anthropic authenticates by header, not by bearer token, and pins an API version.</summary>
    [Fact]
    public async Task ShouldReadTheAnthropicCatalogue_WithItsOwnAuthHeaders()
    {
        using var handler = new StubHttpMessageHandler(AnthropicBody);

        var models = await ListAsync(handler, "anthropic");

        Assert.Equal(["claude-opus-5", "claude-sonnet-5"], models);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.anthropic.com/v1/models", request.RequestUri?.ToString());
        Assert.True(request.Headers.Contains("x-api-key"));
        Assert.Contains("2023-06-01", request.Headers.GetValues("anthropic-version"));
    }

    /// <summary>Ollama is local, unauthenticated, and names its endpoint and its field differently.</summary>
    [Fact]
    public async Task ShouldReadTheOllamaCatalogue_FromItsTagsEndpointWithoutAuth()
    {
        using var handler = new StubHttpMessageHandler(OllamaBody);

        var models = await ListAsync(handler, "ollama", apiKey: null);

        Assert.Equal(["llama3.2:latest", "qwen3:8b"], models);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://localhost:11434/api/tags", request.RequestUri?.ToString());
        Assert.Null(request.Headers.Authorization);
    }

    /// <summary>
    /// Azure serves deployments an operator named, not a public catalogue. Returning an empty
    /// list would read as "this provider serves nothing"; the refusal has to name the reason
    /// and point at the fallback, because the campaign scripts rely on that fallback.
    /// </summary>
    [Theory]
    [InlineData("azure")]
    [InlineData("azure-openai")]
    public async Task ShouldRefuseTheAzureCatalogue_WithAReasonAndAWayForward(string provider)
    {
        using var handler = new StubHttpMessageHandler("{}");
        using var client = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => LlmCatalogClient.ListAsync(
            client, provider, "https://example.openai.azure.com", "k",
            TestContext.Current.CancellationToken));

        Assert.Contains("deployment", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ShouldSurfaceTheStatusAndBody_WhenTheCatalogueRefusesTheRequest()
    {
        using var handler = new StubHttpMessageHandler(
            """{"error":"invalid api key"}""", System.Net.HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => ListAsync(handler, "openai"));

        Assert.Contains("401", ex.Message, StringComparison.Ordinal);
        Assert.Contains("invalid api key", ex.Message, StringComparison.Ordinal);
    }

    // ── Base URL resolution ─────────────────────────────────────────────────

    [Fact]
    public void ShouldPreferAnExplicitBaseUrl_OverTheProviderDefault()
    {
        var resolved = LlmCatalogClient.ResolveBaseUrl("kimi", "https://api.moonshot.cn/v1/");

        Assert.Equal("https://api.moonshot.cn/v1", resolved);
    }

    [Fact]
    public void ShouldRefuseToGuess_WhenTheProviderHasNoDefaultBaseUrl()
    {
        var ex = Assert.Throws<NotSupportedException>(
            () => LlmCatalogClient.ResolveBaseUrl("azure", overrideUrl: null));

        Assert.Contains("--base-url", ex.Message, StringComparison.Ordinal);
    }

    // ── Glob filtering ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("gpt-5.6-*", new[] { "gpt-5.6-luna", "gpt-5.6-sol" })]
    [InlineData("*flash*", new string[0])]
    [InlineData("gpt-?", new[] { "gpt-4" })]
    [InlineData("gpt-4", new[] { "gpt-4" })]
    public void ShouldFilterByGlob(string pattern, string[] expected)
    {
        string[] candidates = ["gpt-4", "gpt-5.6-luna", "gpt-5.6-sol"];

        Assert.Equal(expected, LlmCatalogClient.Filter(candidates, pattern));
    }

    /// <summary>
    /// A literal is not a filter. Passing one through unchanged is what lets the campaign
    /// scripts hand the same value to the filter whether the operator typed a glob or a name.
    /// </summary>
    [Fact]
    public void ShouldKeepEverything_WhenThePatternIsAbsent()
    {
        string[] candidates = ["a", "b"];

        Assert.Equal(candidates, LlmCatalogClient.Filter(candidates, pattern: null));
    }

    [Theory]
    [InlineData("gpt-5.6-*", true)]
    [InlineData("gpt-?", true)]
    [InlineData("gpt-5.6-sol", false)]
    [InlineData(null, false)]
    public void ShouldRecogniseAGlob(string? value, bool expected) =>
        Assert.Equal(expected, LlmCatalogClient.IsGlob(value));
}
