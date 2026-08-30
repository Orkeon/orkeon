using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// There is no <c>Llm:Provider</c> key: the provider shown by the UIs is inferred
/// from the endpoint.
/// </summary>
public sealed class LlmProviderDetectorTests
{
    [Fact]
    public void The_ollama_default_endpoint_is_detected()
    {
        Assert.Equal(LlmProviderDetector.Ollama, LlmProviderDetector.Detect(LlmEndpoints.OllamaDefault));
        Assert.Equal(LlmProviderDetector.Ollama, LlmProviderDetector.Detect("http://127.0.0.1:11434/v1"));
    }

    [Fact]
    public void The_docker_model_runner_endpoint_is_detected()
    {
        Assert.Equal(
            LlmProviderDetector.DockerModelRunner,
            LlmProviderDetector.Detect("http://localhost:12434/engines/llama.cpp/v1"));
    }

    [Fact]
    public void The_openai_endpoint_is_detected()
    {
        Assert.Equal("openai", LlmProviderDetector.Detect(LlmEndpoints.OpenAI));
    }

    [Theory]
    [InlineData("https://api.anthropic.com", "anthropic")]
    [InlineData("https://api.deepseek.com", "deepseek")]
    [InlineData("https://api.mistral.ai/v1", "mistral")]
    [InlineData("https://api.z.ai/api/paas/v4", "zai")]
    [InlineData("https://api.moonshot.cn/v1", "kimi")]
    [InlineData("https://my-deployment.openai.azure.com/", "azure-openai")]
    public void Known_cloud_hosts_are_detected(string baseUrl, string expected) =>
        Assert.Equal(expected, LlmProviderDetector.Detect(baseUrl));

    [Fact]
    public void An_unknown_endpoint_is_custom()
    {
        Assert.Equal(LlmProviderDetector.Custom, LlmProviderDetector.Detect("https://llm.internal.acme.com/v1"));
        Assert.Equal(LlmProviderDetector.Custom, LlmProviderDetector.Detect("http://localhost:8080/v1"));
    }

    [Fact]
    public void A_half_typed_url_is_custom_rather_than_a_failure()
    {
        Assert.Equal(LlmProviderDetector.Custom, LlmProviderDetector.Detect("http:/"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_endpoint_means_no_provider(string? baseUrl) =>
        Assert.Equal(LlmProviderDetector.None, LlmProviderDetector.Detect(baseUrl));

    [Fact]
    public void The_section_exposes_the_detected_provider_read_only()
    {
        var document = AppSettingsDocument.CreateEmpty();
        document.Llm.BaseUrl = LlmEndpoints.OllamaDefault;

        Assert.Equal(LlmProviderDetector.Ollama, document.Llm.DetectedProvider);
        Assert.False(document.ContainsPath("Llm:Provider"));
    }
}
