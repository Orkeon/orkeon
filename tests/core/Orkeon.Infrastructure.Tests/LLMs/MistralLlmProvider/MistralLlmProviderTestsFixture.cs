using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class MistralLlmProviderTestsFixture
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<MistralLlmProvider> _logger;
    private readonly LlmConfig _config;

    public MistralLlmProviderTestsFixture()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<MistralLlmProvider>();
        _config = LlmConfig.Create("mistral-large-latest", TestApiKey);
    }

    public MistralLlmProviderTestsFixture WithHttpClientFactory(TestHttpClientFactory value)
    {
        // Configure _httpClientFactory as needed
        return this;
    }

    public MistralLlmProviderTestsFixture WithLogger(TestLogger<MistralLlmProvider> value)
    {
        // Configure _logger as needed
        return this;
    }

    public MistralLlmProviderTestsFixture WithConfig(LlmConfig value)
    {
        // Configure _config as needed
        return this;
    }

    public TestHttpClientFactory GetHttpClientFactory() => _httpClientFactory;
    public TestLogger<MistralLlmProvider> GetLogger() => _logger;
    public LlmConfig GetConfig() => _config;

}
