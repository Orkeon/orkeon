using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Orkeon.Tests.Shared.Network;

/// <summary>
/// The port of a server a test starts on the loopback (GAP-41). A port asked of the system and
/// given back is anyone's until the server binds it: on a machine running several test passes
/// at once, another process can take it in between. A start here therefore tries again on
/// another port when the one it was given is taken — the race costs a retry, never a failure.
/// The retry is a test tool: in production a port is configuration, and a taken one is refused.
/// </summary>
public static class LoopbackPorts
{
    /// <summary>The ports a start tries before it lets the last conflict go.</summary>
    public const int Attempts = 10;

    /// <summary>
    /// The scheme and host a test server listens on and its clients call, without a final slash:
    /// <c>http://127.0.0.1</c>, the address <see cref="Probe"/> checks. Under Linux and macOS,
    /// <see cref="HttpListener"/> binds the first address its host name resolves to — <c>::1</c>
    /// for <c>localhost</c> on a machine that ranks it first, a port the probe never looked at.
    /// Under Windows the listener is HTTP.sys, which routes by host name and binds no address of
    /// its own: there the tests keep the <c>localhost</c> they always listened on.
    /// </summary>
    public static string Host { get; } = OperatingSystem.IsWindows() ? "http://localhost" : "http://127.0.0.1";

    /// <summary>The address of a server listening on <paramref name="port"/>, with a final slash.</summary>
    public static Uri BaseAddress(int port) => new($"{Host}:{port.ToString(CultureInfo.InvariantCulture)}/");

    /// <summary>
    /// A loopback port nobody listens on at this instant: asked of the system (port 0 on
    /// <c>127.0.0.1</c>) and given back, so anyone may take it before it is used. A server is
    /// started with <see cref="StartAsync{T}(Func{int, T}, Func{T, CancellationToken, Task}, CancellationToken)"/>
    /// or <see cref="StartListener"/>, which try again; the probe alone only serves a server
    /// whose start is refused before it binds anything.
    /// </summary>
    public static int Probe()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    /// <summary>
    /// Builds a server on a free loopback port and starts it. When the port turns out to be taken
    /// — an <see cref="HttpListenerException"/>, a <see cref="SocketException"/>
    /// <see cref="SocketError.AddressAlreadyInUse"/>, or one of them inside the exception the
    /// start threw — it disposes what it built and starts again on another port, up to
    /// <see cref="Attempts"/> times, then lets the last conflict go. Any other failure leaves at
    /// the first attempt: a refused configuration stays a refusal.
    /// </summary>
    /// <typeparam name="T">The server: disposed when its start fails, if it is disposable.</typeparam>
    /// <param name="create">Builds the server on the port it is given, listening on <see cref="Host"/>.</param>
    /// <param name="start">Starts it.</param>
    /// <param name="cancellationToken">Passed to <paramref name="start"/>.</param>
    /// <returns>The started server, and its port.</returns>
    public static Task<(T Server, int Port)> StartAsync<T>(
        Func<int, T> create, Func<T, CancellationToken, Task> start, CancellationToken cancellationToken = default) =>
        StartAsync(create, start, Probe, cancellationToken);

    /// <summary>
    /// <see cref="StartAsync{T}(Func{int, T}, Func{T, CancellationToken, Task}, CancellationToken)"/>
    /// on the ports <paramref name="probe"/> proposes — a test of the retry holds one of them.
    /// </summary>
    /// <typeparam name="T">The server: disposed when its start fails, if it is disposable.</typeparam>
    /// <param name="create">Builds the server on the port it is given, listening on <see cref="Host"/>.</param>
    /// <param name="start">Starts it.</param>
    /// <param name="probe">Proposes a port for each attempt.</param>
    /// <param name="cancellationToken">Passed to <paramref name="start"/>.</param>
    /// <returns>The started server, and its port.</returns>
    public static async Task<(T Server, int Port)> StartAsync<T>(
        Func<int, T> create, Func<T, CancellationToken, Task> start, Func<int> probe, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(probe);

        for (var attempt = 1; ; attempt++)
        {
            var port = probe();
            var server = create(port);
            try
            {
                await start(server, cancellationToken).ConfigureAwait(false);
                return (server, port);
            }
            catch (Exception ex) when (IsConflict(ex) && attempt < Attempts)
            {
                await DisposeAsync(server).ConfigureAwait(false);
            }
            catch
            {
                await DisposeAsync(server).ConfigureAwait(false);
                throw;
            }
        }
    }

    /// <summary>
    /// Starts the <see cref="HttpListener"/> of a test double on a free loopback port, with the
    /// same retry as <see cref="StartAsync{T}(Func{int, T}, Func{T, CancellationToken, Task}, CancellationToken)"/>.
    /// The double stops it with <see cref="HttpListener.Close"/> alone: under Linux and macOS,
    /// <c>Close()</c> after <c>Stop()</c> binds the port again for an instant to remove its prefix,
    /// and fails — or makes another server's start fail — if someone took it meanwhile.
    /// </summary>
    /// <param name="path">The path of the prefix, starting and ending with a slash.</param>
    /// <returns>The started listener, and the prefix it serves.</returns>
    public static (HttpListener Listener, Uri Prefix) StartListener(string path = "/")
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        for (var attempt = 1; ; attempt++)
        {
            var prefix = new Uri(BaseAddress(Probe()), path);
            var listener = new HttpListener();
            try
            {
                listener.Prefixes.Add(prefix.ToString());
                listener.Start();
                return (listener, prefix);
            }
            catch (Exception ex) when (IsConflict(ex) && attempt < Attempts)
            {
                ((IDisposable)listener).Dispose();
            }
            catch
            {
                ((IDisposable)listener).Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// A loopback port bound without listening, held until disposed: a connection to it is
    /// refused, and no probe — this process's or another's — can propose it meanwhile.
    /// </summary>
    public static RefusingPort Refusing() => new();

    /// <summary>
    /// Whether <paramref name="exception"/> says the port was taken — itself, or an exception it
    /// carries: <c>orkeon-host</c> refuses a listener the system refused with an exception that
    /// keeps the system's as its inner one.
    /// </summary>
    private static bool IsConflict(Exception? exception) => exception switch
    {
        null => false,
        HttpListenerException => true,
        SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse } => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsConflict),
        _ => IsConflict(exception.InnerException),
    };

    private static async ValueTask DisposeAsync<T>(T server)
    {
        switch (server)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}

/// <summary>A loopback port bound without listening (<see cref="LoopbackPorts.Refusing"/>).</summary>
public sealed class RefusingPort : IDisposable
{
    private readonly Socket _socket;

    internal RefusingPort()
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        }
        catch
        {
            _socket.Dispose();
            throw;
        }

        Port = ((IPEndPoint)_socket.LocalEndPoint!).Port;
    }

    /// <summary>The port, on <c>127.0.0.1</c>.</summary>
    public int Port { get; }

    /// <summary>Gives the port back.</summary>
    public void Dispose() => _socket.Dispose();
}
