using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Tools.Web.Firefox;

/// <summary>
/// The shared browsers of a host (<c>SharedInstance</c>): one per profile, started when the first
/// session asks for it, closed when the last session leaves it. Every attach and every release goes
/// through one lock, so a session never joins a browser that is closing.
/// </summary>
internal sealed class FirefoxBrowserPool : IAsyncDisposable
{
    private readonly IFirefoxLauncher _launcher;
    private readonly FirefoxToolOptions _options;
    private readonly IFileSystemService _fileSystem;
    private readonly FirefoxSessionTimings? _timings;
    private readonly ILogger? _logger;
    private readonly Lock _sync = new();
    private readonly Dictionary<string, Task<FirefoxBrowser>> _browsers = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public FirefoxBrowserPool(
        IFirefoxLauncher launcher, FirefoxToolOptions options, IFileSystemService fileSystem,
        ILogger? logger, FirefoxSessionTimings? timings)
    {
        _launcher = launcher;
        _options = options;
        _fileSystem = fileSystem;
        _logger = logger;
        _timings = timings;
    }

    /// <summary>How many browsers run now.</summary>
    public int BrowserCount
    {
        get
        {
            lock (_sync)
                return _browsers.Count;
        }
    }

    /// <summary>Opens a session on the shared browser of <paramref name="profile"/>, started first when needed.</summary>
    /// <exception cref="FirefoxException">Firefox is not installed, did not start, or the profile is missing or held.</exception>
    public async Task<FirefoxSession> AttachAsync(FirefoxProfileChoice profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task<FirefoxBrowser> starting;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_browsers.TryGetValue(profile.Name, out starting!))
                {
                    starting = StartAsync(profile);
                    _browsers[profile.Name] = starting;
                }
            }

            FirefoxBrowser browser;
            try
            {
                browser = await starting.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                Forget(profile.Name, starting);
                throw;
            }

            FirefoxSession? session = null;
            lock (_sync)
            {
                if (_browsers.TryGetValue(profile.Name, out var current) && ReferenceEquals(current, starting) && browser.IsAlive)
                    session = FirefoxSession.Attach(browser, _options, _fileSystem, _logger);
            }

            if (session is not null)
            {
                await session.OpenFirstTabAsync(cancellationToken).ConfigureAwait(false);
                return session;
            }

            // The browser closed or died between its start and this attach: the next loop starts another.
            Forget(profile.Name, starting);
            if (browser.SessionCount == 0)
                await browser.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Closes every shared browser; a session still attached fails with its browser.</summary>
    public async ValueTask DisposeAsync()
    {
        List<Task<FirefoxBrowser>> browsers;
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            browsers = [.. _browsers.Values];
            _browsers.Clear();
        }

        foreach (var starting in browsers)
        {
            try
            {
                var browser = await starting.ConfigureAwait(false);
                await browser.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is FirefoxException or Firefox.BiDi.BiDiException or OperationCanceledException)
            {
                // A browser that never started has nothing to close.
            }
        }
    }

    [SuppressMessage("Reliability", "CA2000", Justification = "The browser lives in the pool until its last session leaves, then Released disposes it.")]
    private async Task<FirefoxBrowser> StartAsync(FirefoxProfileChoice profile)
    {
        // Started under no caller's token: a run that gives up waiting leaves the browser to the next one.
        var browser = await FirefoxBrowser.OpenAsync(_launcher, profile, _options, _timings, _logger, CancellationToken.None).ConfigureAwait(false);
        browser.Released = ReleaseAsync;
        return browser;
    }

    /// <summary>Runs when the last session of a browser leaves: closed unless a session joined meanwhile.</summary>
    private async ValueTask ReleaseAsync(FirefoxBrowser browser)
    {
        bool close;
        lock (_sync)
        {
            close = browser.SessionCount == 0;
            if (close)
            {
                var key = _browsers.FirstOrDefault(pair => pair.Value.IsCompletedSuccessfully && ReferenceEquals(pair.Value.Result, browser)).Key;
                if (key is not null)
                    _browsers.Remove(key);
            }
        }

        if (close)
            await browser.DisposeAsync().ConfigureAwait(false);
    }

    private void Forget(string key, Task<FirefoxBrowser> starting)
    {
        lock (_sync)
        {
            if (_browsers.TryGetValue(key, out var current) && ReferenceEquals(current, starting))
                _browsers.Remove(key);
        }
    }
}
