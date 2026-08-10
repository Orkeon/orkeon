namespace Orkeon.Scripting.Cli.Commands;

/// <summary>
/// Minimal <see cref="IHttpClientFactory"/> for CLI verbs that talk to a provider outside
/// a host (llm probe, doctor, init): hands out plain clients so the provider's real HTTP
/// behaviour — including its timeouts — is what gets exercised.
/// </summary>
internal sealed class CliHttpClientFactory : IHttpClientFactory, IDisposable
{
    private readonly Dictionary<string, HttpClient> _clients = [];

    // Providers call CreateClient once per request; reusing the instance per name mirrors
    // what the real factory does and keeps a long campaign from opening a socket per mode.
    public HttpClient CreateClient(string name)
    {
        if (_clients.TryGetValue(name, out var existing))
            return existing;

        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        _clients[name] = client;
        return client;
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
            client.Dispose();
        _clients.Clear();
    }
}
