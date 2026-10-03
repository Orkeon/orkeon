using System.Net;
using System.Text;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// A loopback OpenAI-style catalogue (<c>GET /v1/models</c>) recording the
/// <c>Authorization</c> header of every request: what the probe presented, not whether a
/// vendor accepted it.
/// </summary>
internal sealed class CatalogueServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly List<string> _authorizations = [];
    private readonly Task _pump;

    public CatalogueServer()
    {
        (_listener, var prefix) = LoopbackPorts.StartListener();
        BaseUrl = new Uri(prefix, "v1").ToString();
        _pump = PumpAsync();
    }

    /// <summary>The OpenAI-style base URL a setting names: the catalogue is <c>{BaseUrl}/models</c>.</summary>
    public string BaseUrl { get; }

    public IReadOnlyList<string> Authorizations
    {
        get { lock (_authorizations) { return [.. _authorizations]; } }
    }

    public void Dispose()
    {
        // Close() alone: after Stop(), it would bind the port again for an instant (GAP-41).
        _listener.Close();
        _pump.GetAwaiter().GetResult();
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
