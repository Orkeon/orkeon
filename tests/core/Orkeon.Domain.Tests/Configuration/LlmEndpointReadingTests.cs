using Orkeon.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Tests.Shared.Llm;

namespace Orkeon.Domain.Tests.Configuration;

/// <summary>A model address is read by its host, port and path alone (GAP-53 decisions 1 and 3).</summary>
public class LlmEndpointReadingTests
{
    [Theory]
    [Trait("BR", "CH-08")]
    [MemberData(nameof(LlmEndpointCases.Rows), MemberType = typeof(LlmEndpointCases))]
    public void ShouldReadEachAddressOfTheTable_WhenTheReadingIsAsked(
        string address, string providerKey, string? ollamaRoot, bool isLocal)
    {
        var reading = LlmEndpointReading.Of(address, "gpt-4o", "sk-ignored");

        Assert.Equal(providerKey, reading.ProviderKey);
        Assert.Equal(ollamaRoot, reading.OllamaRoot, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(isLocal, reading.IsLocal);
        Assert.NotEqual(LlmEndpointNotice.Refused, reading.Notice);
    }

    [Theory]
    [Trait("BR", "CH-08")]
    [InlineData("mistral:7b", null, LlmProviderKeys.Ollama)]
    [InlineData("deepseek-flash", null, LlmProviderKeys.DeepSeek)]
    [InlineData(null, "xai-abc123", LlmProviderKeys.Grok)]
    [InlineData(null, null, LlmProviderKeys.OpenAI)]
    public void ShouldReadTheModelThenTheKey_WhenThereIsNoAddress(string? model, string? apiKey, string providerKey)
    {
        var reading = LlmEndpointReading.Of(null, model, apiKey);

        Assert.Equal(providerKey, reading.ProviderKey);
        Assert.Equal(LlmEndpointNotice.None, reading.Notice);
    }

    [Theory]
    [Trait("BR", "CH-09")]
    [InlineData("localhost:11434")]
    [InlineData("ftp://x")]
    [InlineData("https://api.deepseek.com/anthropic")]
    [InlineData("https://api.z.ai/api/anthropic")]
    public void ShouldRefuse_WhenTheAddressIsNotHttpOrAVendorAnthropicPath(string address)
    {
        var reading = LlmEndpointReading.Of(address, "gpt-4o", null);

        Assert.Equal(LlmEndpointNotice.Refused, reading.Notice);
    }

    [Theory]
    [Trait("BR", "CH-09")]
    [InlineData("http://localhost:8080")]
    [InlineData("https://ollama.example.com")]
    public void ShouldWarn_WhenACustomAddressHasNoPath(string address)
    {
        var reading = LlmEndpointReading.Of(address, "gpt-4o", null);

        Assert.Equal(LlmProviderKeys.Custom, reading.ProviderKey);
        Assert.Equal(LlmEndpointNotice.Silent, reading.Notice);
    }
}
