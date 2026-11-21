using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>
/// A tool bridge that answers what the test tells it (#445): a hostile or broken bridge for the caller's checks. Each
/// request gets the body of <see cref="Bodies"/> for its last path segment (<c>tools</c>, <c>call</c>, ...), or
/// <c>{}</c>, with status 200.
/// </summary>
public sealed class StubBridgeServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly Task _loop;

    /// <summary>Starts the bridge on a free loopback port.</summary>
    public StubBridgeServer()
    {
        (_listener, Address) = LoopbackPorts.StartListener();
        _loop = ServeAsync();
    }

    /// <summary>The bridge's base address.</summary>
    public Uri Address { get; }

    /// <summary>The body each endpoint answers, by its last path segment.</summary>
    public ConcurrentDictionary<string, string> Bodies { get; } = new(StringComparer.Ordinal);

    /// <summary>The paths requested, in order.</summary>
    public ConcurrentQueue<string> Requested { get; } = new();

    /// <summary>When set, every request is answered 307 towards the same path under this address.</summary>
    public Uri? RedirectTo { get; set; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Close alone, never after Stop (LoopbackPorts.StartListener).
        _listener.Close();
        try
        {
            await _loop;
        }
        catch (HttpListenerException)
        {
            // The listener closed.
        }
        catch (ObjectDisposedException)
        {
            // Same.
        }
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            var context = await _listener.GetContextAsync();
            var path = context.Request.Url?.AbsolutePath ?? "";
            Requested.Enqueue(path);
            if (RedirectTo is { } target)
            {
                context.Response.StatusCode = 307;
                context.Response.RedirectLocation = new Uri(target, path.TrimStart('/')).ToString();
                context.Response.Close();
                continue;
            }

            var segment = path.TrimEnd('/').Split('/')[^1];
            var bytes = Encoding.UTF8.GetBytes(Bodies.GetValueOrDefault(segment, "{}"));
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }
}
