namespace Orkeon.Infrastructure.Tests.Resilience;

public sealed class ResiliencePoliciesTestsFixture : IDisposable
{
    private readonly TestLogger _logger;
    private readonly TestHttpMessageHandler _httpHandler;
    private readonly HttpClient _httpClient;

    public ResiliencePoliciesTestsFixture()
    {
        _logger = new TestLogger();
        _httpHandler = new TestHttpMessageHandler();
        _httpClient = new HttpClient(_httpHandler);
    }

    public ResiliencePoliciesTestsFixture WithLogger(TestLogger value)
    {
        // Configure _logger as needed
        return this;
    }

    public ResiliencePoliciesTestsFixture WithHttpHandler(TestHttpMessageHandler value)
    {
        // Configure _httpHandler as needed
        return this;
    }

    public ResiliencePoliciesTestsFixture WithHttpClient(HttpClient value)
    {
        // Configure _httpClient as needed
        return this;
    }

    public TestLogger GetLogger() => _logger;
    public TestHttpMessageHandler GetHttpHandler() => _httpHandler;
    public HttpClient GetHttpClient() => _httpClient;


    public void Dispose()
    {
        _httpClient.Dispose();
        _httpHandler.Dispose();
        GC.SuppressFinalize(this);
    }
}
