using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class AnthropicLlmProviderTestsFixture
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<AnthropicLlmProvider> _logger;
    private readonly LlmConfig _config;

    public AnthropicLlmProviderTestsFixture()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<AnthropicLlmProvider>();
        _config = LlmConfig.Create(ModelClaude3Opus, TestApiKey) with
        {
            BaseUrl = new Uri("https://api.anthropic.com")
        };
    }

    public AnthropicLlmProviderTestsFixture WithHttpClientFactory(TestHttpClientFactory value)
    {
        // Configure _httpClientFactory as needed
        return this;
    }

    public AnthropicLlmProviderTestsFixture WithLogger(TestLogger<AnthropicLlmProvider> value)
    {
        // Configure _logger as needed
        return this;
    }

    public AnthropicLlmProviderTestsFixture WithConfig(LlmConfig value)
    {
        // Configure _config as needed
        return this;
    }

    public TestHttpClientFactory GetHttpClientFactory() => _httpClientFactory;
    public TestLogger<AnthropicLlmProvider> GetLogger() => _logger;
    public LlmConfig GetConfig() => _config;

}
