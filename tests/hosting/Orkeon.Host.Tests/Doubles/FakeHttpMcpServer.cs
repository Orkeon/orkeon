using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Orkeon.Host.Tests.Doubles;

/// <summary>
/// A legacy-era MCP server over HTTP on the loopback, answering the handshake and listing the
/// tools it was given — enough for the host's real MCP client (<c>Transport: Sse</c>) to
/// connect and register them, without a subprocess or the network.
/// </summary>
internal sealed class FakeHttpMcpServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string[] _tools;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public FakeHttpMcpServer(params string[] tools)
    {
        _tools = tools;
        var port = FreePort();
        Url = new Uri($"http://127.0.0.1:{port}/mcp/");
        _listener.Prefixes.Add(Url.ToString());
        _listener.Start();
        _loop = Task.Run(ServeAsync);
    }

    /// <summary>The endpoint to put under <c>MCP:Servers:&lt;id&gt;:Url</c>.</summary>
    public Uri Url { get; }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = JsonNode.Parse(await reader.ReadToEndAsync().ConfigureAwait(false))!.AsObject();
            var answer = Answer(body);

            if (answer is null)
            {
                context.Response.StatusCode = (int)HttpStatusCode.Accepted;
                context.Response.Close();
                continue;
            }

            var bytes = Encoding.UTF8.GetBytes(answer.ToJsonString());
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            context.Response.Close();
        }
    }

    private JsonObject? Answer(JsonObject request)
    {
        // A notification (notifications/initialized) carries no id and gets no body.
        if (!request.TryGetPropertyValue("id", out var id) || id is null)
            return null;

        var method = request["method"]?.GetValue<string>();
        JsonNode? result = method switch
        {
            "initialize" => new JsonObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = true } },
                ["serverInfo"] = new JsonObject { ["name"] = "fake", ["version"] = "1.0" },
            },
            "tools/list" => new JsonObject
            {
                ["tools"] = new JsonArray(_tools.Select(name => (JsonNode)new JsonObject
                {
                    ["name"] = name,
                    ["description"] = $"{name} (fake MCP)",
                    ["inputSchema"] = new JsonObject { ["type"] = "object" },
                }).ToArray()),
            },
            _ => null,
        };

        var response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone() };
        if (result is null)
        {
            // server/discover included: a legacy server does not know it, and the client
            // falls back to the initialize handshake.
            response["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"Method not found: {method}" };
        }
        else
        {
            response["result"] = result;
        }

        return response;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        _listener.Close();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The listener went away under the pending accept: the stop we asked for.
        }

        _stop.Dispose();
    }
}
