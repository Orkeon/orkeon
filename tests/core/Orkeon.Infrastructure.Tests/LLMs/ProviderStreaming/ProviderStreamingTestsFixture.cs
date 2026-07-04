using Polly;
using Orkeon.Infrastructure.Tests.Doubles;

#pragma warning disable CS0618 // Testing obsolete APIs

namespace Orkeon.Infrastructure.Tests.LLMs;

public sealed class ProviderStreamingTestsFixture : IDisposable
{
    private readonly MockHttpClientFactory _httpClientFactory = new();
    private readonly IAsyncPolicy<HttpResponseMessage> _noOpPolicy = Policy.NoOpAsync<HttpResponseMessage>();

    public ProviderStreamingTestsFixture()
    {
    }

    public ProviderStreamingTestsFixture WithHttpClientFactory(MockHttpClientFactory value)
    {
        // Configure _httpClientFactory as needed
        return this;
    }

    public ProviderStreamingTestsFixture WithNoOpPolicy(IAsyncPolicy<HttpResponseMessage> value)
    {
        // Configure _noOpPolicy as needed
        return this;
    }

    public MockHttpClientFactory GetHttpClientFactory() => _httpClientFactory;
    public IAsyncPolicy<HttpResponseMessage> GetNoOpPolicy() => _noOpPolicy;

#pragma warning restore CS0618

    public void Dispose()
    {
        _httpClientFactory.Dispose();
        GC.SuppressFinalize(this);
    }
}
