namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written double: an <see cref="IHttpClientFactory"/> serving clients backed
/// by a single test-provided <see cref="HttpMessageHandler"/> (no real network).
/// Records the names of the clients created.
/// </summary>
public sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    /// <summary>Names passed to <see cref="CreateClient"/>, in order.</summary>
    public List<string> CreatedClients { get; } = [];

    public FakeHttpClientFactory(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    public HttpClient CreateClient(string name)
    {
        CreatedClients.Add(name);
        return new HttpClient(_handler, disposeHandler: false);
    }
}
