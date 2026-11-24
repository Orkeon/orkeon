using Microsoft.Extensions.Logging;
using Orkeon.Compliance.Vfs;
using Orkeon.Tools.Web.Browser;
using Orkeon.Tools.Web.Firefox.BiDi;

namespace Orkeon.Tools.Web.Chromium;

/// <summary>
/// Starts a Chromium browser on a data folder of its own with a remote-debugging port the system picks, reads
/// the DevTools endpoint it announces on its standard error, and runs the BiDi mapper in it.
/// </summary>
internal sealed class ChromiumEngine : IBrowserEngine
{
    private const string Announcement = "DevTools listening on ";

    private readonly ChromiumVariant _variant;
    private readonly FirefoxToolOptions _options;
    private readonly ILogger? _logger;
    private readonly Func<string, string?> _readEnvironment;

    public ChromiumEngine(
        ChromiumVariant variant, FirefoxToolOptions options, ILogger? logger = null, Func<string, string?>? readEnvironment = null)
    {
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentNullException.ThrowIfNull(options);
        _variant = variant;
        _options = options;
        _logger = logger;
        _readEnvironment = readEnvironment ?? Environment.GetEnvironmentVariable;
    }

    /// <inheritdoc/>
    public string Id => _variant.Id;

    /// <inheritdoc/>
    public string DisplayName => _variant.DisplayName;

    /// <inheritdoc/>
    public BrowserEngineCapabilities Capabilities { get; } = new();

    /// <summary>How long a browser asked to close is given to leave before it is killed.</summary>
    public TimeSpan ExitGrace { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>The arguments the browser is started with. The browser's own sandbox is never turned off.</summary>
    public static IReadOnlyList<string> BuildArguments(string dataFolder, bool disposable, bool headless)
    {
        ArgumentException.ThrowIfNullOrEmpty(dataFolder);
        List<string> arguments =
        [
            // Port 0: the system picks a free one, announced on the standard error.
            "--remote-debugging-port=0",
            "--user-data-dir=" + dataFolder,
            "--no-first-run",
            "--no-default-browser-check",
        ];
        if (disposable)
        {
            // Nothing of a folder about to be removed is worth a keyring prompt, a sync or an update.
            arguments.AddRange(["--disable-sync", "--disable-background-networking", "--disable-component-update", "--password-store=basic", "--use-mock-keychain"]);
        }

        if (headless)
            arguments.Add("--headless=new");
        arguments.Add("about:blank");
        return arguments;
    }

    /// <summary>The DevTools endpoint a line of the standard error announces, or <c>null</c>.</summary>
    public static Uri? ReadEndpoint(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var at = line.IndexOf(Announcement, StringComparison.Ordinal);
        return at >= 0
            && Uri.TryCreate(line[(at + Announcement.Length)..].Trim(), UriKind.Absolute, out var endpoint)
            && endpoint.Scheme == Uri.UriSchemeWs
                ? endpoint
                : null;
    }

    /// <inheritdoc/>
    public async Task<IBrowserProcess> LaunchAsync(BrowserProfileChoice profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var executable = LocateOnHost() ?? throw BrowserHost.Missing(DisplayName, _options);
        var headless = BrowserHost.ResolveHeadless(_options, DisplayName, _readEnvironment, _logger);
        var folder = profile.IsDisposable
            ? ChromiumProfile.Create(ChromiumProfile.ResolveRoot(_variant, _options.ProfileRoot))
            : ChromiumProfile.Open(DisplayName, profile.Name, UserDataFolders());
        var startup = TimeSpan.FromSeconds(_options.StartupTimeoutSeconds);
        return await BrowserHost.LaunchAsync(new BrowserLaunch
        {
            DisplayName = DisplayName,
            Executable = executable,
            Arguments = BuildArguments(folder.Directory, folder.Disposable, headless),
            ReadEndpoint = ReadEndpoint,
            ConnectAsync = (endpoint, ct) => ConnectAsync(endpoint, startup, ct),
            RemoveProfileAsync = folder.RemoveAsync,
            Profile = profile,
            Headless = headless,
            Startup = startup,
            ExitGrace = ExitGrace,
        }, cancellationToken).ConfigureAwait(false);
    }

    [SuppressVfsCompliance("OUT-OF-SCOPE: probes the host for the browser's program, as the docker sandbox probes for docker; not a VFS path.")]
    internal string? LocateOnHost()
    {
        // OUT-OF-SCOPE: binary discovery on the host, not a VFS mount.
        var known = _variant.KnownPrograms(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(),
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ]);
        return BrowserHost.Locate(
            _options.ExecutablePath, _variant.Names, _readEnvironment("PATH"), known, File.Exists, OperatingSystem.IsWindows());
    }

    // The mapper's own start has no command timeout yet: it shares the browser's.
    private static async Task<IBiDiTransport> ConnectAsync(Uri endpoint, TimeSpan startup, CancellationToken cancellationToken)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(startup);
        return await ChromiumBiDiTransport.ConnectAsync(endpoint, bounded.Token).ConfigureAwait(false);
    }

    private IReadOnlyList<string> UserDataFolders() => _variant.UserDataFolders(
        OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
}
