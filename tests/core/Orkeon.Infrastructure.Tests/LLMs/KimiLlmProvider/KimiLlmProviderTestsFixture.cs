using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class KimiLlmProviderTestsFixture
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<KimiLlmProvider> _logger;
    private readonly LlmConfig _config;

    public KimiLlmProviderTestsFixture()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<KimiLlmProvider>();
        _config = LlmConfig.Create("moonshot-v1-8k", TestApiKey);
    }

    public KimiLlmProviderTestsFixture WithHttpClientFactory(TestHttpClientFactory value)
    {
        // Configure _httpClientFactory as needed
        return this;
    }

    public KimiLlmProviderTestsFixture WithLogger(TestLogger<KimiLlmProvider> value)
    {
        // Configure _logger as needed
        return this;
    }

    public KimiLlmProviderTestsFixture WithConfig(LlmConfig value)
    {
        // Configure _config as needed
        return this;
    }

    public TestHttpClientFactory GetHttpClientFactory() => _httpClientFactory;
    public TestLogger<KimiLlmProvider> GetLogger() => _logger;
    public LlmConfig GetConfig() => _config;

}
