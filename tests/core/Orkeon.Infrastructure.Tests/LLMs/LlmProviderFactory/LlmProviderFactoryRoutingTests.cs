using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Provider routing: which concrete provider the factory picks from a base URL or a model
/// name (LLM-01, gaps G-11 to G-14).
/// </summary>
/// <remarks>
/// Unlike the older <c>ShouldInferCorrectProviderType_WhenCreate</c> theory, these tests
/// assert the provider that was actually built: <see cref="LlmProviderAdapter.Name"/>
/// forwards the underlying provider's own name, so a mis-route fails instead of passing
/// vacuously.
/// </remarks>
public class LlmProviderFactoryRoutingTests
{
    private static LlmProviderFactory CreateFactory() =>
        new(new TestHttpClientFactory(), NullLoggerFactory.Instance);

    private static string RoutedProviderName(string? model, string? baseUrl)
    {
        var config = (model is null ? LlmConfig.Default() : LlmConfig.Create(model)) with
        {
            BaseUrl = baseUrl is null ? null : new Uri(baseUrl),
        };
        return CreateFactory().Create(config).Name;
    }

    // ── G-11 / G-12 / G-13: international hosts ─────────────────────────────

    [Theory]
    // Kimi: mainland host (already routed) and the international host (G-11).
    [InlineData("https://api.moonshot.cn/v1", "kimi")]
    [InlineData("https://api.moonshot.ai/v1", "kimi")]
    // Qwen: mainland DashScope, the international host — which does NOT contain the
    // mainland string — and the per-workspace regional hosts (G-12).
    [InlineData("https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen")]
    [InlineData("https://dashscope-intl.aliyuncs.com/compatible-mode/v1", "qwen")]
    [InlineData("https://ws-123.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1", "qwen")]
    // HuggingFace: the router host is covered by the pre-existing "huggingface.co"
    // substring rule — G-13 was already satisfied. Pinned so it stays that way.
    [InlineData("https://router.huggingface.co/v1", "huggingface")]
    public void ShouldRouteToDedicatedProvider_WhenBaseUrlIsAnInternationalHost(
        string baseUrl, string expectedProvider)
    {
        Assert.Equal(expectedProvider, RoutedProviderName(model: null, baseUrl));
    }

    // ── G-14: the Mistral routing trap ──────────────────────────────────────

    [Theory]
    // The bare name and its Ollama tags designate the locally pulled model. This is the
    // arbitration of MISTRAL-PROVIDER-PLAN §0 and it is preserved.
    [InlineData("mistral", "ollama")]
    [InlineData("mistral:7b", "ollama")]
    [InlineData("mistral:latest", "ollama")]
    // Versioned cloud identifiers used to be sent to localhost:11434 (G-14).
    [InlineData("mistral-medium-2604", "mistral")]
    [InlineData("mistral-small-4-0-26-03", "mistral")]
    [InlineData("mistral-large-3-25-12", "mistral")]
    [InlineData("ministral-3-8b-25-12", "mistral")]
    public void ShouldSeparateLocalMistralFromCloudMistral_WhenNoBaseUrlIsGiven(
        string model, string expectedProvider)
    {
        Assert.Equal(expectedProvider, RoutedProviderName(model, baseUrl: null));
    }

    [Fact]
    public void ShouldPreferBaseUrlOverModelName_ForAHyphenatedLocalMistralTag()
    {
        // The escape hatch for Ollama users running a hyphenated local tag: base-URL
        // inference runs before model-name inference and wins.
        Assert.Equal("ollama", RoutedProviderName("mistral-nemo", "http://localhost:11434"));
    }

    // ── Non-regression on the routes that already worked ────────────────────

    [Theory]
    [InlineData("gpt-5.6-sol", null, "OpenAI")]
    [InlineData("claude-sonnet-5", null, "anthropic")]
    [InlineData("llama3.2", null, "ollama")]
    [InlineData("codellama", null, "ollama")]
    [InlineData("deepseek-v4-flash", null, "deepseek")]
    [InlineData("qwen3.7-plus", null, "qwen")]
    [InlineData("glm-5.2", null, "zai")]
    // Gemini (PUB-15): model-prefix and OpenAI-compatible host routing.
    [InlineData("gemini-3.7-flash", null, "gemini")]
    [InlineData("grok-4.6", null, "grok")]
    [InlineData("unknown-model", null, "OpenAI")]
    [InlineData(null, "https://api.deepseek.com", "deepseek")]
    [InlineData(null, "https://api.z.ai/api/paas/v4", "zai")]
    [InlineData(null, "https://generativelanguage.googleapis.com/v1beta/openai", "gemini")]
    [InlineData(null, "https://api.x.ai/v1", "grok")]
    [InlineData(null, "https://api.mistral.ai/v1", "mistral")]
    [InlineData(null, "https://my-resource.openai.azure.com", "azure-openai")]
    public void ShouldRouteToExpectedProvider(string? model, string? baseUrl, string expectedProvider)
    {
        Assert.Equal(expectedProvider, RoutedProviderName(model, baseUrl));
    }
}
