using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orkeon.Tools.Web.Firefox.BiDi;

namespace Orkeon.Tools.Web.Firefox;

/// <summary>
/// One running Firefox: its process, its WebDriver BiDi channel, and the sessions attached to it. A
/// session drives tabs of its own in the browser; the browser hands the events of the channel to every
/// session, and closes once the last session has left it - or hands itself to <see cref="Released"/>
/// when an owner keeps it between sessions.
/// </summary>
internal sealed class FirefoxBrowser : IAsyncDisposable
{
    private readonly IFirefoxBrowser _process;
    private readonly BiDiConnection _connection;
    private readonly FirefoxSessionTimings _timings;
    private readonly Lock _sync = new();
    private readonly List<FirefoxSession> _sessions = [];
    private string? _firstTab;
    private bool _closing;
    private int _disposed;

    private FirefoxBrowser(IFirefoxBrowser process, BiDiConnection connection, FirefoxSessionTimings timings)
    {
        _process = process;
        _connection = connection;
        _timings = timings;
    }

    /// <summary><c>false</c> once the browser has gone: closed by the user, crashed, or disposed.</summary>
    public bool IsAlive => _disposed == 0 && !_connection.IsClosed;

    /// <summary><c>true</c> when the browser runs without a window.</summary>
    public bool Headless => _process.Headless;

    /// <summary>The profile the browser runs with.</summary>
    public FirefoxProfileChoice Profile => _process.Profile;

    /// <summary>
    /// What happens when the last session leaves: the browser closes, unless an owner set this to keep
    /// it (a shared browser) and close it itself.
    /// </summary>
    public Func<FirefoxBrowser, ValueTask>? Released { get; set; }

    /// <summary>How long a session waits for a page to react.</summary>
    public FirefoxSessionTimings Timings => _timings;

    /// <summary>How many sessions drive tabs in the browser now.</summary>
    public int SessionCount
    {
        get
        {
            lock (_sync)
                return _sessions.Count;
        }
    }

    /// <summary>Starts a browser on <paramref name="profile"/> and opens the protocol session on its channel.</summary>
    /// <exception cref="FirefoxException">Firefox is not installed, did not start, or the profile is missing or held.</exception>
    /// <exception cref="BiDiException">The browser refused the session.</exception>
    [SuppressMessage("Reliability", "CA2000", Justification = "The connection is handed to the browser returned, which disposes it; a start that fails disposes the browser.")]
    public static async Task<FirefoxBrowser> OpenAsync(
        IFirefoxLauncher launcher, FirefoxProfileChoice profile, FirefoxToolOptions options, FirefoxSessionTimings? timings, ILogger? logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(options);

        var process = await launcher.LaunchAsync(profile, cancellationToken).ConfigureAwait(false);
        var connection = new BiDiConnection(process.Transport, TimeSpan.FromSeconds(options.CommandTimeoutSeconds), logger);
        var browser = new FirefoxBrowser(process, connection, timings ?? FirefoxSessionTimings.Default);
        try
        {
            await browser.StartAsync(cancellationToken).ConfigureAwait(false);
            return browser;
        }
        catch
        {
            await browser.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Sends a command on the channel and returns its result.</summary>
    public Task<JsonObject> SendAsync(string method, JsonObject? parameters, CancellationToken cancellationToken) =>
        _connection.SendAsync(method, parameters, cancellationToken);

    /// <summary>The tab Firefox opened at start, handed to the first session that asks; <c>null</c> after that.</summary>
    public string? TakeFirstTab()
    {
        lock (_sync)
        {
            var tab = _firstTab;
            _firstTab = null;
            return tab;
        }
    }

    /// <summary>Opens a tab, shown in front of the others, and returns its context id.</summary>
    public async Task<string> CreateTabAsync(CancellationToken cancellationToken)
    {
        var created = await SendAsync("browsingContext.create", new JsonObject
        {
            ["type"] = "tab",
            ["background"] = false,
        }, cancellationToken).ConfigureAwait(false);
        return created["context"]?.GetValue<string>()
            ?? throw new FirefoxException("The browser opened a tab without saying which.");
    }

    /// <summary>Closes a tab; a tab already gone is no error.</summary>
    public async Task CloseTabAsync(string context, CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync("browsingContext.close", new JsonObject { ["context"] = context }, cancellationToken).ConfigureAwait(false);
        }
        catch (BiDiException ex) when (ex.Code == BiDiException.NoSuchFrame)
        {
            // Closed by the user already.
        }
    }

    /// <summary>Brings a tab in front of the others.</summary>
    public Task ActivateAsync(string context, CancellationToken cancellationToken) =>
        SendAsync("browsingContext.activate", new JsonObject { ["context"] = context }, cancellationToken);

    /// <summary>Registers a session: it receives the events of the channel from now on.</summary>
    /// <exception cref="FirefoxException">The browser is closing: its last session left, or it was disposed.</exception>
    public void Attach(FirefoxSession session)
    {
        lock (_sync)
        {
            if (_closing || _disposed != 0)
                throw new FirefoxException("The browser is closing.");
            _sessions.Add(session);
        }
    }

    /// <summary>
    /// Unregisters a session. The last one to leave closes the browser, or hands it to
    /// <see cref="Released"/>, and gets <c>true</c>: its tabs went with the browser. A session that never
    /// attached changes nothing.
    /// </summary>
    public async ValueTask<bool> ReleaseAsync(FirefoxSession session)
    {
        bool last;
        lock (_sync)
        {
            last = _sessions.Remove(session) && _sessions.Count == 0;
            if (last && Released is null)
                _closing = true;
        }

        if (!last)
            return false;
        if (Released is { } released)
            await released(this).ConfigureAwait(false);
        else
            await DisposeAsync().ConfigureAwait(false);
        return true;
    }

    /// <summary>Asks the browser to close, then ends its process and removes its profile.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (!_connection.IsClosed)
        {
            try
            {
                // A browser asked to close leaves by itself; one that does not answer is killed below.
                await _connection.SendAsync("browser.close", null, CancellationToken.None)
                    .WaitAsync(_timings.CloseWait).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is BiDiException or TimeoutException)
            {
                // Gone already, or deaf.
            }
        }

        await _connection.DisposeAsync().ConfigureAwait(false);
        await _process.DisposeAsync().ConfigureAwait(false);
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        await SendAsync("session.new", new JsonObject
        {
            ["capabilities"] = new JsonObject { ["alwaysMatch"] = new JsonObject { ["acceptInsecureCerts"] = false } },
        }, cancellationToken).ConfigureAwait(false);

        // A named profile may restore the tabs of its last session: the blank one Firefox was started
        // on is the one to drive, the others are the user's.
        var tree = await SendAsync("browsingContext.getTree", new JsonObject { ["maxDepth"] = 0 }, cancellationToken).ConfigureAwait(false);
        var contexts = (tree["contexts"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        var blank = contexts.FirstOrDefault(context => context["url"]?.GetValue<string>() == "about:blank");
        _firstTab = (blank ?? contexts.FirstOrDefault())?["context"]?.GetValue<string>()
            ?? throw new FirefoxException("Firefox started without a tab to drive.");

        _connection.EventReceived = OnEvent;
        await SendAsync("session.subscribe", new JsonObject
        {
            ["events"] = new JsonArray("browsingContext"),
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs on the receive loop of the connection: every session sees every event, and never waits for a command.</summary>
    private void OnEvent(string method, JsonObject parameters)
    {
        FirefoxSession[] sessions;
        lock (_sync)
            sessions = [.. _sessions];
        foreach (var session in sessions)
            session.OnEvent(method, parameters);
    }
}
