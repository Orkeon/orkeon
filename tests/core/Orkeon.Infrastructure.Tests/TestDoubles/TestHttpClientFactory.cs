namespace Orkeon.Infrastructure.Tests.TestDoubles;

public class TestHttpClientFactory : IHttpClientFactory
{
    private readonly Dictionary<string, HttpClient> _clients = [];

    public void RegisterClient(string name, HttpClient client)
    {
        _clients[name] = client;
    }

    public HttpClient CreateClient(string name)
    {
        if (_clients.TryGetValue(name, out var client))
        {
            return client;
        }

        // Return a default client if none registered
        return new HttpClient();
    }
}
