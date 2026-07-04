using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class OpenAIProviderTestsFixture
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<OpenAIProvider> _logger;
    private readonly LlmConfig _config;

    public OpenAIProviderTestsFixture()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<OpenAIProvider>();
        _config = LlmConfig.Create(ModelGpt4, TestApiKey);
    }

    public OpenAIProviderTestsFixture WithHttpClientFactory(TestHttpClientFactory value)
    {
        // Configure _httpClientFactory as needed
        return this;
    }

    public OpenAIProviderTestsFixture WithLogger(TestLogger<OpenAIProvider> value)
    {
        // Configure _logger as needed
        return this;
    }

    public OpenAIProviderTestsFixture WithConfig(LlmConfig value)
    {
        // Configure _config as needed
        return this;
    }

    public TestHttpClientFactory GetHttpClientFactory() => _httpClientFactory;
    public TestLogger<OpenAIProvider> GetLogger() => _logger;
    public LlmConfig GetConfig() => _config;

}
