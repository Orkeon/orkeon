using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class GroqLlmProviderTestsFixture
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<GroqLlmProvider> _logger;
    private readonly LlmConfig _config;

    public GroqLlmProviderTestsFixture()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<GroqLlmProvider>();
        _config = LlmConfig.Create("llama-3.3-70b-versatile", TestApiKey);
    }

    public GroqLlmProviderTestsFixture WithHttpClientFactory(TestHttpClientFactory value)
    {
        // Configure _httpClientFactory as needed
        return this;
    }

    public GroqLlmProviderTestsFixture WithLogger(TestLogger<GroqLlmProvider> value)
    {
        // Configure _logger as needed
        return this;
    }

    public GroqLlmProviderTestsFixture WithConfig(LlmConfig value)
    {
        // Configure _config as needed
        return this;
    }

    public TestHttpClientFactory GetHttpClientFactory() => _httpClientFactory;
    public TestLogger<GroqLlmProvider> GetLogger() => _logger;
    public LlmConfig GetConfig() => _config;

}
