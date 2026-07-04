using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.TestDoubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs;

public class QwenLlmProviderTestsFixture
{
    private readonly TestHttpClientFactory _httpClientFactory;
    private readonly TestLogger<QwenLlmProvider> _logger;
    private readonly LlmConfig _config;

    public QwenLlmProviderTestsFixture()
    {
        _httpClientFactory = new TestHttpClientFactory();
        _logger = new TestLogger<QwenLlmProvider>();
        _config = LlmConfig.Create("qwen-plus", TestApiKey);
    }

    public QwenLlmProviderTestsFixture WithHttpClientFactory(TestHttpClientFactory value)
    {
        // Configure _httpClientFactory as needed
        return this;
    }

    public QwenLlmProviderTestsFixture WithLogger(TestLogger<QwenLlmProvider> value)
    {
        // Configure _logger as needed
        return this;
    }

    public QwenLlmProviderTestsFixture WithConfig(LlmConfig value)
    {
        // Configure _config as needed
        return this;
    }

    public TestHttpClientFactory GetHttpClientFactory() => _httpClientFactory;
    public TestLogger<QwenLlmProvider> GetLogger() => _logger;
    public LlmConfig GetConfig() => _config;

}
