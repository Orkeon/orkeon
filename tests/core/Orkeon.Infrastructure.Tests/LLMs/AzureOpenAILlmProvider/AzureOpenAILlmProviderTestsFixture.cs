using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class AzureOpenAILlmProviderTestsFixture
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<AzureOpenAILlmProvider> _logger;
    private readonly LlmConfig _config;

    public AzureOpenAILlmProviderTestsFixture()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<AzureOpenAILlmProvider>();
        _config = LlmConfig.Create(ModelGpt4, TestApiKey) with
        {
            BaseUrl = new Uri("https://myinstance.openai.azure.com")
        };
    }

    public AzureOpenAILlmProviderTestsFixture WithHttpClientFactory(TestHttpClientFactory value)
    {
        // Configure _httpClientFactory as needed
        return this;
    }

    public AzureOpenAILlmProviderTestsFixture WithLogger(TestLogger<AzureOpenAILlmProvider> value)
    {
        // Configure _logger as needed
        return this;
    }

    public AzureOpenAILlmProviderTestsFixture WithConfig(LlmConfig value)
    {
        // Configure _config as needed
        return this;
    }

    public TestHttpClientFactory GetHttpClientFactory() => _httpClientFactory;
    public TestLogger<AzureOpenAILlmProvider> GetLogger() => _logger;
    public LlmConfig GetConfig() => _config;

}
