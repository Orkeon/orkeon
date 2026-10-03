using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// A loopback OpenAI-style catalogue (<c>GET /v1/models</c>) recording the
/// <c>Authorization</c> header of every request: what the probe presented, not whether a
/// vendor accepted it.
/// </summary>
internal sealed class CatalogueServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly List<string> _authorizations = [];
    private readonly Task _pump;

    public CatalogueServer()
    {
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
        }

        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _pump = PumpAsync();
    }

    public int Port { get; }

    public IReadOnlyList<string> Authorizations
    {
        get { lock (_authorizations) { return [.. _authorizations]; } }
    }

    public void Dispose()
    {
        _listener.Stop();
        _pump.GetAwaiter().GetResult();
        _listener.Close();
    }

    private async Task PumpAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            lock (_authorizations)
                _authorizations.Add(context.Request.Headers["Authorization"] ?? "");

            var body = Encoding.UTF8.GetBytes("""{ "data": [ { "id": "test-model" } ] }""");
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(body);
            context.Response.Close();
        }
    }
}
