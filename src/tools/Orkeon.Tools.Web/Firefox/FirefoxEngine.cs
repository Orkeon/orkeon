using Microsoft.Extensions.Logging;
using Orkeon.Compliance.Vfs;
using Orkeon.Tools.Web.Browser;
using Orkeon.Tools.Web.Firefox.BiDi;

namespace Orkeon.Tools.Web.Firefox;

/// <summary>
/// Starts Firefox as a child process with its own profile and a remote-debugging port the system picks,
/// reads the WebDriver BiDi endpoint the browser announces on its standard error, and connects to it.
/// </summary>
internal sealed class FirefoxEngine : IBrowserEngine
{
    /// <summary>The value of <c>Engine</c> that names this engine.</summary>
    public const string EngineId = "firefox";

    private readonly FirefoxToolOptions _options;
    private readonly ILogger? _logger;
    private readonly Func<string, string?> _readEnvironment;

    public FirefoxEngine(
        FirefoxToolOptions options, ILogger? logger = null, Func<string, string?>? readEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _logger = logger;
        _readEnvironment = readEnvironment ?? Environment.GetEnvironmentVariable;
    }

    /// <inheritdoc/>
    public string Id => EngineId;

    /// <inheritdoc/>
    public string DisplayName => "Firefox";

    /// <inheritdoc/>
    public BrowserEngineCapabilities Capabilities { get; } = new();

    /// <summary>How long a browser asked to close is given to leave before it is killed.</summary>
    public TimeSpan ExitGrace { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>The arguments Firefox is started with.</summary>
    public static IReadOnlyList<string> BuildArguments(string profileDirectory, bool headless)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileDirectory);
        List<string> arguments =
        [
            // Port 0: the system picks a free one, announced on the standard error.
            "--remote-debugging-port", "0",
            "--profile", profileDirectory,
            "--no-remote", "--new-instance",
        ];
        if (headless)
            arguments.Add("--headless");
        arguments.Add("about:blank");
        return arguments;
    }

    /// <inheritdoc/>
    public async Task<IBrowserProcess> LaunchAsync(BrowserProfileChoice profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var executable = LocateOnHost()
            ?? throw BrowserHost.Missing(DisplayName, _options);

        var headless = BrowserHost.ResolveHeadless(_options, DisplayName, _readEnvironment, _logger);
        var folder = profile.IsDisposable
            ? FirefoxProfile.Create(FirefoxProfile.ResolveRoot(_options.ProfileRoot))
            : FirefoxProfile.Open(profile.Name);
        return await BrowserHost.LaunchAsync(new BrowserLaunch
        {
            DisplayName = DisplayName,
            Executable = executable,
            Arguments = BuildArguments(folder.Directory, headless),
            Environment = new Dictionary<string, string> { ["MOZ_CRASHREPORTER_DISABLE"] = "1" },
            ReadEndpoint = static line => BiDiEndpoint.TryParse(line, out var endpoint) ? endpoint : null,
            ConnectAsync = static async (endpoint, ct) =>
                await WebSocketBiDiTransport.ConnectAsync(BiDiEndpoint.Session(endpoint), ct).ConfigureAwait(false),
            RemoveProfileAsync = folder.RemoveAsync,
            Profile = profile,
            Headless = headless,
            Startup = TimeSpan.FromSeconds(_options.StartupTimeoutSeconds),
            ExitGrace = ExitGrace,
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string[] KnownLocations()
    {
        if (OperatingSystem.IsWindows())
        {
            return
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mozilla Firefox", "firefox.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Mozilla Firefox", "firefox.exe"),
            ];
        }

        if (OperatingSystem.IsMacOS())
            return ["/Applications/Firefox.app/Contents/MacOS/firefox"];

        return ["/usr/bin/firefox", "/snap/bin/firefox", "/usr/local/bin/firefox"];
    }

    [SuppressVfsCompliance("OUT-OF-SCOPE: probes the host for the Firefox program, as the docker sandbox probes for docker; not a VFS path.")]
    internal string? LocateOnHost()
    {
        // OUT-OF-SCOPE: binary discovery on the host, not a VFS mount.
        var known = KnownLocations();
        return BrowserHost.Locate(_options.ExecutablePath, ["firefox"], _readEnvironment("PATH"), known, File.Exists, OperatingSystem.IsWindows());
    }
}
