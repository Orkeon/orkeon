using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Constants.Llm;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Llm;

namespace Orkeon.Studio.Core.Tests.Llm;

/// <summary>
/// STUDIO-43 writes the probe's test completion in Studio Core, which cannot reference the
/// providers (see its csproj). The thinking level it reads per provider is a copy of what each
/// provider declares in <see cref="LlmProviderCapabilities.Thinking"/> — pinned here against
/// the real providers, built from the same URL the way the runtime builds them.
/// </summary>
public sealed class LlmProbeDialectDriftTests
{
    private sealed class OneClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    public static TheoryData<string> Endpoints() =>
    [
        LlmProviderEndpoints.OpenAI,
        LlmProviderEndpoints.Anthropic,
        LlmProviderEndpoints.DeepSeek,
        LlmProviderEndpoints.Together,
        LlmProviderEndpoints.Qwen,
        LlmProviderEndpoints.Kimi,
        LlmProviderEndpoints.HuggingFace,
        LlmProviderEndpoints.Mistral,
        LlmProviderEndpoints.Zai,
        LlmProviderEndpoints.Gemini,
        LlmProviderEndpoints.Grok,
        LlmProviderEndpoints.MiniMax,
        LlmProviderEndpoints.OpenRouter,
        LlmProviderEndpoints.Mammouth,
        LlmProviderEndpoints.OllamaDefault,
        "https://llm.example.com/v1",   // custom: the runtime drives it as OpenAI
    ];

    [Theory]
    [MemberData(nameof(Endpoints))]
    public void The_probes_thinking_level_is_the_one_the_provider_declares(string endpoint)
    {
        var factory = new LlmProviderFactory(new OneClientFactory(), NullLoggerFactory.Instance);
        var built = factory.Create(LlmConfig.Create("probe-model", "k") with { BaseUrl = new Uri(endpoint) });
        var declared = built is LlmProviderAdapter adapter
            ? adapter.UnderlyingProvider.Capabilities
            : ((ILlmProvider)built).Capabilities;

        Assert.Equal(declared.Thinking, LlmProbeDialect.ThinkingSupportOf(LlmProviderDetector.Detect(endpoint)));
    }
}
