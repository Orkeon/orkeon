using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for IHttpClientFactory with call tracking and configurable HttpClient instances.
/// </summary>
public sealed class MockHttpClientFactory : IHttpClientFactory, IDisposable
{
    private readonly Dictionary<string, HttpClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private HttpClient? _defaultClient;
    private MockHttpMessageHandler? _defaultHandler;

    // --- Tracking ---
    public int CreateClientCallCount { get; private set; }
    public string? LastClientName { get; private set; }
    public List<string> AllClientNames { get; } = [];

    // --- Configuration ---

    /// <summary>
    /// Sets the default HttpClient returned for any name not explicitly configured.
    /// </summary>
    public void SetDefaultClient(HttpClient client) => _defaultClient = client;

    /// <summary>
    /// Sets up a default handler and returns the HttpClient backed by it.
    /// </summary>
    public MockHttpMessageHandler SetupDefaultHandler()
    {
        _defaultHandler = new MockHttpMessageHandler();
        _defaultClient = new HttpClient(_defaultHandler);
        return _defaultHandler;
    }

    /// <summary>
    /// Registers an HttpClient for a specific named client.
    /// </summary>
    public void AddClient(string name, HttpClient client) => _clients[name] = client;

    /// <summary>
    /// Registers an HttpClient with a MockHttpMessageHandler for a specific named client.
    /// </summary>
    public MockHttpMessageHandler AddClientWithHandler(string name)
    {
        var handler = new MockHttpMessageHandler();
        var client = new HttpClient(handler);
        _clients[name] = client;
        return handler;
    }

    /// <summary>
    /// Gets the default handler if one was set up via SetupDefaultHandler().
    /// </summary>
    public MockHttpMessageHandler? DefaultHandler => _defaultHandler;

    // --- IHttpClientFactory ---
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The handler is owned by the returned HttpClient (disposeHandler: true) whose lifetime belongs to the caller.")]
    public HttpClient CreateClient(string name)
    {
        CreateClientCallCount++;
        LastClientName = name;
        AllClientNames.Add(name);

        if (_clients.TryGetValue(name, out var client))
            return client;

        if (_defaultClient != null)
            return _defaultClient;

        // Return a client backed by a new default handler
        return new HttpClient(new MockHttpMessageHandler());
    }

    public void Dispose()
    {
        _defaultClient?.Dispose();
        _defaultHandler?.Dispose();
        GC.SuppressFinalize(this);
    }
}
