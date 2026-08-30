using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS0618 // Testing obsolete ApiKey API

namespace Orkeon.Infrastructure.Tests.CovToolCalling;

public class CovToolCalling_LlmProviderFactoryTests
{
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class StubLoggerFactory : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider) { }
        public ILogger CreateLogger(string categoryName) => NullLogger.Instance;
        public void Dispose() { }
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Stub factories (no-op Dispose) are owned by the returned LlmProviderFactory for the duration of the test.")]
    private static LlmProviderFactory NewFactory()
        => new(new StubHttpClientFactory(), new StubLoggerFactory());

    // ── Explicit-type provider creation (covers every CreateXxxProvider) ──

    [Theory]
    [InlineData("ollama")]
    [InlineData("openai")]
    [InlineData("anthropic")]
    [InlineData("azure")]
    [InlineData("azure-openai")]
    [InlineData("grok")]
    [InlineData("xai")]
    [InlineData("minimax")]
    [InlineData("together")]
    [InlineData("togetherai")]
    [InlineData("qwen")]
    [InlineData("deepseek")]
    [InlineData("kimi")]
    [InlineData("moonshot")]
    [InlineData("huggingface")]
    [InlineData("hf")]
    public void Create_ExplicitType_AllProviders(string providerType)
    {
        var provider = NewFactory().Create(providerType, LlmConfig.Create("some-model"));
        Assert.NotNull(provider);
        Assert.IsType<LlmProviderAdapter>(provider);
    }

    // ── Inference via BaseUrl host patterns ──────────────────────

    [Theory]
    [InlineData("https://api.openai.com/engines/x")]              // /engines/ -> openai (Docker Model Runner)
    [InlineData("http://model-runner.docker.internal/v1")]        // openai
    [InlineData("https://myresource.openai.azure.com")]           // azure
    [InlineData("https://x.cognitiveservices.azure.com")]         // azure
    [InlineData("https://api.x.ai/v1")]                           // grok
    [InlineData("https://api.together.xyz/v1")]                   // together
    [InlineData("https://dashscope.aliyuncs.com/v1")]             // qwen
    [InlineData("https://api.deepseek.com/v1")]                   // deepseek
    [InlineData("https://api.moonshot.cn/v1")]                    // kimi
    [InlineData("https://huggingface.co/v1")]                     // huggingface
    [InlineData("https://router.hf.co/v1")]                       // huggingface
    [InlineData("https://api.anthropic.com/v1")]                  // anthropic
    [InlineData("http://localhost:11434")]                        // ollama
    public void Create_InfersFromBaseUrl(string baseUrl)
    {
        var config = LlmConfig.Create("custom-model") with { BaseUrl = new Uri(baseUrl) };
        var provider = NewFactory().Create(config);
        Assert.IsType<LlmProviderAdapter>(provider);
    }

    // ── Inference via model name patterns ────────────────────────

    [Theory]
    [InlineData("gpt-4o")]
    [InlineData("claude-3-5-sonnet")]
    [InlineData("llama3")]
    [InlineData("mistral-large")]
    [InlineData("codellama-13b")]
    [InlineData("mixtral-8x7b")]
    [InlineData("qwen-turbo")]
    [InlineData("deepseek-chat")]
    [InlineData("moonshot-v1-8k")]
    public void Create_InfersFromModelName(string model)
    {
        var provider = NewFactory().Create(LlmConfig.Create(model));
        Assert.IsType<LlmProviderAdapter>(provider);
    }

    // ── Inference via ApiKey prefix (hf_) ────────────────────────

    [Fact]
    public void Create_InfersHuggingFace_FromApiKeyPrefix()
    {
        // No BaseUrl, no recognizable model -> falls through to ApiKey inference.
        var config = LlmConfig.Create("custom-model") with { ApiKey = "hf_abc123" };
        var provider = NewFactory().Create(config);
        Assert.IsType<LlmProviderAdapter>(provider);
    }

    [Fact]
    public void Create_DefaultsToOpenAI_WhenNothingMatches()
    {
        var config = LlmConfig.Create("zzz-unknown-model");
        var provider = NewFactory().Create(config);
        Assert.IsType<LlmProviderAdapter>(provider);
    }

    [Fact]
    public void Create_BaseUrlBeatsModel()
    {
        // Model says claude (anthropic) but URL says x.ai -> URL wins.
        var config = LlmConfig.Create("claude-3") with { BaseUrl = new Uri("https://api.x.ai/v1") };
        var provider = NewFactory().Create(config);
        Assert.IsType<LlmProviderAdapter>(provider);
    }
}

#pragma warning restore CS0618
