using System.Net;
using System.Net.Sockets;
using Orkeon.Compliance.Vfs;
using Orkeon.Tools.Web.Browser;
using Orkeon.Tools.Web.Firefox;

namespace Orkeon.Tools.Web.Safari;

/// <summary>Starts the safaridriver of macOS on a free port and opens its session. Safari always shows its window, on a profile of its own.</summary>
internal sealed class SafariEngine : IBrowserEngine
{
    /// <summary>The value of <c>Engine</c> that picks Safari.</summary>
    public const string EngineId = "safari";

    private readonly FirefoxToolOptions _options;
    private readonly Func<string, string?> _readEnvironment;

    public SafariEngine(FirefoxToolOptions options, Func<string, string?>? readEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _readEnvironment = readEnvironment ?? Environment.GetEnvironmentVariable;
    }

    /// <inheritdoc/>
    public string Id => EngineId;

    /// <inheritdoc/>
    public string DisplayName => "Safari";

    /// <inheritdoc/>
    public BrowserEngineCapabilities Capabilities { get; } = new() { Headless = false, NamedProfiles = false };

    /// <inheritdoc/>
    public async Task<IBrowserProcess> LaunchAsync(BrowserProfileChoice profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var executable = LocateOnHost() ?? throw BrowserHost.Missing(DisplayName, _options);
        var port = FreePort();
        var commandTimeout = TimeSpan.FromSeconds(_options.CommandTimeoutSeconds);
        var startup = TimeSpan.FromSeconds(_options.StartupTimeoutSeconds);
        return await BrowserHost.LaunchAsync(new BrowserLaunch
        {
            DisplayName = DisplayName,
            Executable = executable,
            Arguments = ["-p", port.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            Endpoint = new Uri($"http://localhost:{port}/"),
            ReadEndpoint = static _ => null,
            ConnectAsync = async (endpoint, ct) =>
            {
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
                bounded.CancelAfter(startup);
                try
                {
                    return await SafariTransport.ConnectAsync(endpoint, commandTimeout, bounded.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
                {
                    throw new FirefoxException($"safaridriver did not open a session within {startup.TotalSeconds:0.#} s.", ex);
                }
            },
            RemoveProfileAsync = static () => Task.CompletedTask,
            Profile = profile,
            Headless = false,
            Startup = startup,

            // The driver serves until it is killed: there is nothing to wait for.
            ExitGrace = TimeSpan.Zero,
        }, cancellationToken).ConfigureAwait(false);
    }

    [SuppressVfsCompliance("OUT-OF-SCOPE: probes the host for safaridriver, as the docker sandbox probes for docker; not a VFS path.")]
    internal string? LocateOnHost() => BrowserHost.Locate(
        _options.ExecutablePath, ["safaridriver"], _readEnvironment("PATH"), ["/usr/bin/safaridriver"], File.Exists, OperatingSystem.IsWindows());

    // safaridriver picks no port by itself: one the system just found free.
    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
