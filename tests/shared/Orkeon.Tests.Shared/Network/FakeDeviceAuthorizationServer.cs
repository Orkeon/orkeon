using System.Net;
using System.Text;

namespace Orkeon.Tests.Shared.Network;

/// <summary>
/// A loopback identity provider for the device authorization grant: <c>/device</c> hands out a device
/// code (user code <see cref="UserCode"/>), <c>/token</c> refuses the sign-in with <c>access_denied</c>, so a
/// flow that reached it ends at once. A test reads <see cref="DeviceRequests"/> to see that the flow started.
/// </summary>
public sealed class FakeDeviceAuthorizationServer : IAsyncDisposable
{
    /// <summary>The code the person is told to type.</summary>
    public const string UserCode = "WXYZ-1234";

    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private int _deviceRequests;

    /// <summary>Starts the server on a free loopback port.</summary>
    public FakeDeviceAuthorizationServer()
    {
        var (listener, prefix) = LoopbackPorts.StartListener("/");
        _listener = listener;
        Prefix = prefix;
        _loop = Task.Run(ServeAsync);
    }

    /// <summary>The base address.</summary>
    public Uri Prefix { get; }

    /// <summary>The device authorization endpoint.</summary>
    public Uri DeviceUrl => new(Prefix, "device");

    /// <summary>The token endpoint.</summary>
    public Uri TokenUrl => new(Prefix, "token");

    /// <summary>The verification address the person is sent to.</summary>
    public Uri VerificationUrl => new(Prefix, "activate");

    /// <summary>How many device authorizations were asked for.</summary>
    public int DeviceRequests => Volatile.Read(ref _deviceRequests);

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            string body;
            if (context.Request.Url!.AbsolutePath.EndsWith("/device", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _deviceRequests);
                context.Response.StatusCode = 200;
                body = $$"""{"device_code":"dev-1","user_code":"{{UserCode}}","verification_uri":"{{VerificationUrl}}","expires_in":60,"interval":1}""";
            }
            else
            {
                context.Response.StatusCode = 400;
                body = """{"error":"access_denied","error_description":"declined"}""";
            }

            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, _stop.Token).ConfigureAwait(false);
            context.Response.Close();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or ObjectDisposedException)
        {
            // The server is down.
        }

        _stop.Dispose();
    }
}
