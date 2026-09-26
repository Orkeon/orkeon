using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Orkeon.Tools.Email.Auth;

/// <summary>
/// Receives the one browser redirect that ends a loopback authorization (RFC 8252 §7.3). A raw
/// <see cref="TcpListener"/> on 127.0.0.1 rather than <c>HttpListener</c>, which needs a URL
/// reservation on Windows that a non-administrator does not have.
/// </summary>
internal sealed class LoopbackRedirectListener : IDisposable
{
    private const int MaxRequestLineBytes = 8 * 1024;
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);

    private readonly TcpListener _listener;

    private LoopbackRedirectListener(TcpListener listener) => _listener = listener;

    /// <summary>The redirect address to register in the authorization request.</summary>
    public Uri RedirectUri => new(string.Create(
        CultureInfo.InvariantCulture, $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/"));

    /// <summary>Listens on a free loopback port.</summary>
    public static LoopbackRedirectListener Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new LoopbackRedirectListener(listener);
    }

    /// <summary>
    /// Waits for the redirect carrying a code or an error; any other request (a favicon) is
    /// answered 404 and the wait goes on.
    /// </summary>
    public async Task<AuthorizationRedirect> WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connection.CancelAfter(ConnectionTimeout);

            AuthorizationRedirect redirect;
            var stream = client.GetStream();
            try
            {
                var requestLine = await ReadLineAsync(stream, connection.Token).ConfigureAwait(false);
                redirect = ParseRequestLine(requestLine);
                await RespondAsync(stream, redirect.IsOutcome, connection.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A browser pre-connection that never sent a request: drop it and keep listening.
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            if (redirect.IsOutcome)
                return redirect;
        }
    }

    /// <summary>Stops listening.</summary>
    public void Dispose() => _listener.Stop();

    /// <summary>Parses <c>GET /?code=…&amp;state=… HTTP/1.1</c>.</summary>
    internal static AuthorizationRedirect ParseRequestLine(string requestLine)
    {
        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("GET", StringComparison.Ordinal) || !parts[1].StartsWith('/'))
            return new AuthorizationRedirect(null, null, null);

        return AuthorizationRedirect.Parse(new Uri(new Uri("http://127.0.0.1/"), parts[1]));
    }

    private static async Task<string> ReadLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>(256);
        var buffer = new byte[1];
        while (bytes.Count < MaxRequestLineBytes)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0 || buffer[0] == (byte)'\n')
                break;
            if (buffer[0] != (byte)'\r')
                bytes.Add(buffer[0]);
        }

        return Encoding.ASCII.GetString([.. bytes]);
    }

    private static async Task RespondAsync(NetworkStream stream, bool outcome, CancellationToken cancellationToken)
    {
        var body = outcome
            ? "<!doctype html><html><body style=\"font-family:sans-serif\"><p>Orkeon received the authorization. You can close this tab and return to the terminal.</p></body></html>"
            : "<!doctype html><html><body></body></html>";
        var payload = Encoding.UTF8.GetBytes(body);
        var status = outcome ? "200 OK" : "404 Not Found";
        var header = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture,
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n"));
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
