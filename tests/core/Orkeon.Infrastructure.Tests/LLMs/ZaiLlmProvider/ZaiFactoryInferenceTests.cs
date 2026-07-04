using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// Factory wiring for the Z.AI (Zhipu GLM) provider: explicit type keys, BaseUrl host
/// inference (api.z.ai + mainland bigmodel.cn twin), and glm-* model-name inference.
/// Guards against the bare "z.ai" substring trap (any *z.ai domain must NOT match).
/// </summary>
public class ZaiFactoryInferenceTests
{
    private static LlmProviderAdapter CreateVia(Func<LlmProviderFactory, LlmProviderAdapter> create)
    {
        var httpClientFactory = new TestHttpClientFactory();
        var factory = new LlmProviderFactory(httpClientFactory, NullLoggerFactory.Instance);
        return create(factory);
    }

    [Theory]
    [InlineData("zai")]
    [InlineData("glm")]
    [InlineData("zhipu")]
    [InlineData("ZAI")]
    public void ShouldCreateZaiProvider_WhenExplicitTypeRequested(string providerType)
    {
        var adapter = CreateVia(f => (LlmProviderAdapter)f.Create(providerType, LlmConfig.Create("glm-5.2")));
        Assert.IsType<ZaiLlmProvider>(adapter.UnderlyingProvider);
        Assert.Equal("zai", adapter.Name);
    }

    [Theory]
    [InlineData("https://api.z.ai/api/paas/v4")]
    [InlineData("https://open.bigmodel.cn/api/paas/v4")]
    public void ShouldInferZaiProvider_WhenBaseUrlMatchesKnownHosts(string baseUrl)
    {
        var config = LlmConfig.Create("custom-model") with { BaseUrl = new Uri(baseUrl) };
        var adapter = CreateVia(f => (LlmProviderAdapter)f.Create(config));
        Assert.IsType<ZaiLlmProvider>(adapter.UnderlyingProvider);
    }

    [Theory]
    [InlineData("glm-5.2")]
    [InlineData("glm-4.7")]
    [InlineData("GLM-5")]
    public void ShouldInferZaiProvider_WhenModelIsGlm(string model)
    {
        var adapter = CreateVia(f => (LlmProviderAdapter)f.Create(LlmConfig.Create(model)));
        Assert.IsType<ZaiLlmProvider>(adapter.UnderlyingProvider);
    }

    [Fact]
    public void ShouldNotInferZai_WhenHostMerelyEndsWithZDotAi()
    {
        // "https://api.xyz.ai" contains the "z.ai" substring but is NOT Z.AI —
        // it must fall through to the OpenAI default.
        var config = LlmConfig.Create("custom-model") with { BaseUrl = new Uri("https://api.xyz.ai/v1") };
        var adapter = CreateVia(f => (LlmProviderAdapter)f.Create(config));
        Assert.IsType<OpenAIProvider>(adapter.UnderlyingProvider);
    }
}
