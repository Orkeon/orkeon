using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class OpenAILlmProviderTestsFixture
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<OpenAILlmProvider> _logger;
    private readonly LlmConfig _config;

    public OpenAILlmProviderTestsFixture()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<OpenAILlmProvider>();
        _config = LlmConfig.Create(ModelGpt4, TestApiKey);
    }

    public OpenAILlmProviderTestsFixture WithHttpClientFactory(TestHttpClientFactory value)
    {
        // Configure _httpClientFactory as needed
        return this;
    }

    public OpenAILlmProviderTestsFixture WithLogger(TestLogger<OpenAILlmProvider> value)
    {
        // Configure _logger as needed
        return this;
    }

    public OpenAILlmProviderTestsFixture WithConfig(LlmConfig value)
    {
        // Configure _config as needed
        return this;
    }

    public TestHttpClientFactory GetHttpClientFactory() => _httpClientFactory;
    public TestLogger<OpenAILlmProvider> GetLogger() => _logger;
    public LlmConfig GetConfig() => _config;

}
