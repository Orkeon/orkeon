using System.Net;
using Orkeon.Application.Interfaces.Knowledge;
using Orkeon.Infrastructure.Knowledge.Loaders;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Knowledge.Loaders;

public sealed class WebPageLoaderTestsFixture : IDisposable
{
    private MockHttpMessageHandler? _handler;
    private HttpClient? _httpClient;
    private WebPageLoader? _loader;

    public WebPageLoaderTestsFixture WithResponse(
        string responseContent,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _httpClient?.Dispose();
        _handler = new MockHttpMessageHandler();
        _handler.SetResponse(statusCode, responseContent, "text/html");

        _httpClient = new HttpClient(_handler);
        _loader = new WebPageLoader(_httpClient);
        return this;
    }

    public WebPageLoader GetLoader()
        => _loader ?? throw new InvalidOperationException("Call WithResponse first");

    public MockHttpMessageHandler GetHandler()
        => _handler ?? throw new InvalidOperationException("Call WithResponse first");

    public Task<LoadedDocument> LoadAsync(string url)
        => GetLoader().LoadAsync(url);

    public bool CanLoad(string source)
        => GetLoader().CanLoad(source);

    public void Dispose()
    {
        _httpClient?.Dispose();
        _handler?.Dispose();
        GC.SuppressFinalize(this);
    }
}
