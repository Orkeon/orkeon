namespace Orkeon.Scripting.Cli.Commands;

/// <summary>
/// Minimal <see cref="IHttpClientFactory"/> for CLI verbs that talk to a provider outside
/// a host (llm probe, doctor, init): hands out plain clients so the provider's real HTTP
/// behaviour — including its timeouts — is what gets exercised.
/// </summary>
internal sealed class CliHttpClientFactory : IHttpClientFactory, IDisposable
{
    private readonly Dictionary<string, HttpClient> _clients = [];
    private readonly ReasoningWireProbe? _wireProbe;
    private ReasoningWireRecorder? _recorder;

    /// <summary>Initializes a factory handing out plain clients.</summary>
    public CliHttpClientFactory()
    {
    }

    /// <summary>Initializes a factory whose clients record the reasoning fields they send.</summary>
    /// <param name="wireProbe">The probe receiving the recorder; the only verb that passes one is the LLM probe.</param>
    public CliHttpClientFactory(ReasoningWireProbe wireProbe)
    {
        _wireProbe = wireProbe;
    }

    // Providers call CreateClient once per request; reusing the instance per name mirrors
    // what the real factory does and keeps a long campaign from opening a socket per mode.
    public HttpClient CreateClient(string name)
    {
        if (_clients.TryGetValue(name, out var existing))
            return existing;

        HttpClient client;
        if (_wireProbe is null)
        {
            client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        }
        else
        {
            // One recorder for every client, so the probe reads a single last request.
            _recorder ??= new ReasoningWireRecorder();
            _wireProbe.Recorder = _recorder;
            client = new HttpClient(_recorder, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(2) };
        }

        _clients[name] = client;
        return client;
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
            client.Dispose();
        _clients.Clear();
        _recorder?.Dispose();
    }
}
