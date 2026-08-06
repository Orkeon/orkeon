using Orkeon.Cli.TerminalGui.Layout;
using Terminal.Gui.App;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Hosting;

/// <summary>
/// Orchestrates the Terminal.Gui application lifecycle and runs the REPL on a background task
/// while the UI event loop owns the main thread.
/// </summary>
public sealed class TerminalGuiHost : IAsyncDisposable
{
    private static readonly bool DiagEnabled =
        string.Equals(Environment.GetEnvironmentVariable("TUI_DIAG"), "1", StringComparison.Ordinal);

    private readonly TerminalGuiOptions _options;
    private readonly TuiIntegration _integration;
    private readonly Orkeon.Cli.TerminalGui.Telemetry.ToolActivityAggregator _toolActivity = new();
    private SplitPaneToplevel? _toplevel;
    private bool _bannerWritten;
    private bool _initialized;
    private bool _ownsApplicationInit;
    private bool? _previousTreatControlCAsInput;

    private static void Diag(string msg)
    {
        if (DiagEnabled) System.Console.Error.WriteLine($"[tui-diag] {msg}");
    }

    public TerminalGuiHost(TerminalGuiOptions options)
        : this(options, new TuiIntegration()) { }

    public TerminalGuiHost(TerminalGuiOptions options, TuiIntegration integration)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _integration = integration ?? throw new ArgumentNullException(nameof(integration));
    }

    /// <summary>The host-populated delegate bag the fidelity views pull from.</summary>
    public TuiIntegration Integration => _integration;

    public SplitPaneToplevel Toplevel
        => _toplevel ?? throw new InvalidOperationException("Call Initialize() first.");

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort console setup: a host that rejects TreatControlCAsInput is non-fatal (Ctrl+C just isn't capturable as a TUI shortcut); the failure is diagnosed, not propagated.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1508:Avoid dead conditional code", Justification = "Terminal.Gui's Application.Driver is a mutable third-party global static; the defensive null-conditional reads in the diagnostic strings guard against its state differing from the analyzer's flow inference.")]
    public void Initialize()
    {
        if (_initialized) return;
        Diag($"Initialize() — TERM={Environment.GetEnvironmentVariable("TERM")} " +
             $"IsInputRedirected={System.Console.IsInputRedirected} IsOutputRedirected={System.Console.IsOutputRedirected}");
        // Skip Application.Init() if Terminal.Gui has already been initialized externally
        // (e.g. by a test fixture). The flag drives the matching DisposeAsync decision.
        if (Application.Driver is null)
        {
            // Driver selection: the default ("") picks ANSI on Linux which emits no output
            // in WSL and many container terminals. DOTNET (uses System.Console) works in
            // every interactive terminal we've tested. Windows still gets the native driver.
            // Override via env var TUI_DRIVER (windows|dotnet|ansi) for debugging.
            var requested = Environment.GetEnvironmentVariable("TUI_DRIVER");
            string driver;
            if (!string.IsNullOrWhiteSpace(requested))
            {
#pragma warning disable CA1308 // produces the driver registry key downstream expects lowercase, not a comparison normalization
                driver = requested.ToLowerInvariant();
#pragma warning restore CA1308
            }
            else
            {
                driver = OperatingSystem.IsWindows()
                    ? Terminal.Gui.Drivers.DriverRegistry.Names.WINDOWS
                    : Terminal.Gui.Drivers.DriverRegistry.Names.DOTNET;
            }
            Diag($"Calling Application.Init('{driver}')");
            try
            {
                Application.Init(driver);
                _ownsApplicationInit = true;
                Diag($"Init OK — Driver={Application.Driver?.GetType().FullName ?? "<null>"}");
            }
            catch (Exception ex)
            {
                Diag($"Init FAILED: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }
        else
        {
            Diag($"Init skipped — Driver already set: {Application.Driver?.GetType().FullName}");
        }
        _toplevel = new SplitPaneToplevel(_options);

        // Tell the .NET runtime to treat Ctrl+C as a regular keystroke instead of
        // converting it to a SIGINT signal. Without this, the OS/runtime fires
        // Console.CancelKeyPress and Terminal.Gui never sees the keystroke — so our
        // status-bar Shortcut for Ctrl+C ("Cancel cmd") never fires.
        // We capture and restore the previous value to avoid surprising other code paths.
        try
        {
            _previousTreatControlCAsInput = System.Console.TreatControlCAsInput;
            System.Console.TreatControlCAsInput = true;
            Diag("Console.TreatControlCAsInput = true (Ctrl+C now reaches Terminal.Gui)");
        }
        catch (Exception ex)
        {
            // Some hosts (older Mono, certain redirected stdin scenarios) reject this.
            // Not fatal — Ctrl+C just won't be capturable as a TUI shortcut.
            Diag($"Could not set TreatControlCAsInput: {ex.Message}");
        }

        // NOTE: do NOT do System.Console.SetOut here. Terminal.Gui v2.0.1's DOTNET driver
        // writes its rendering bytes through System.Console (verified the hard way: the
        // pty redirection blackout in TUI-19). Inner-host log capture is handled in a
        // different way — see AmbientLoggerProvider / runner integration.
        _initialized = true;
        Diag("Initialize() done — toplevel created (status bar attached separately)");
    }

    /// <summary>
    /// Installs the global key bindings (the fidelity layout has no StatusBar widget —
    /// the visible bar is <see cref="Layout.HintBarView"/>, and every shortcut is a
    /// global handler). Called by the <c>TerminalGuiLoggerProvider</c> DI factory once
    /// both Host and Provider exist, so the cyclic dependency stays broken.
    /// </summary>
    public void InstallKeyBindings(
        Orkeon.Cli.TerminalGui.Logging.TerminalGuiLoggerProvider logProvider,
        Layout.FindDialog findDialog)
    {
        if (!_initialized) Initialize();
        Layout.GlobalKeyBindings.Install(_toplevel!, logProvider, findDialog, _integration);
    }

    /// <summary>
    /// Convenience overload that wires the runner into <see cref="SplitPaneToplevel.Runner"/>
    /// before delegating. Status-bar handlers (Ctrl+Q confirmation, Ctrl+C cancel-current-command)
    /// rely on this reference to introspect and control command lifecycle.
    /// </summary>
    public Task RunAsync(Orkeon.Cli.Abstractions.Runners.IInteractiveRunner runner, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(runner);
        return RunRunnerCoreAsync(runner, ct);
    }

    /// <summary>
    /// Runs the REPL on a background task while <c>Application.Run</c> owns the main thread.
    /// Returns when either the REPL completes or the user quits the UI (Ctrl+Q).
    /// </summary>
    public Task RunAsync(Func<CancellationToken, Task> repl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(repl);
        return RunReplCoreAsync(repl, ct);
    }

    private async Task RunRunnerCoreAsync(Orkeon.Cli.Abstractions.Runners.IInteractiveRunner runner, CancellationToken ct)
    {
        if (!_initialized) Initialize();
        _toplevel!.Runner = runner;
        BindFidelityViews(runner);

        // Belt-and-suspenders Ctrl+C handler. The status-bar Shortcut(Key.C.WithCtrl)
        // works in unit tests but is swallowed by focused TextField bindings in real
        // terminals (TUI-20). Subscribing to Application.KeyDown here gives us a
        // global, focus-independent path that fires before any view-level handler.
        // Pattern:
        //   1st Ctrl+C while command running → request cancellation + visible feedback
        //   2nd Ctrl+C within 2s              → force-quit the whole TUI (escape hatch
        //                                       when the command's call chain ignores CT)
        DateTime lastCtrlCAt = DateTime.MinValue;
        EventHandler<Terminal.Gui.Input.Key> ctrlCHandler = (_, key) =>
        {
            var stripped = key.KeyCode & ~(Terminal.Gui.Drivers.KeyCode.CtrlMask
                                         | Terminal.Gui.Drivers.KeyCode.AltMask
                                         | Terminal.Gui.Drivers.KeyCode.ShiftMask);
            if (!key.IsCtrl || stripped != Terminal.Gui.Drivers.KeyCode.C) return;

            key.Handled = true; // always swallow Ctrl+C in TUI mode

            // Copy-on-selection: Ctrl+C with an active text selection in the focused pane copies that
            // selection to the OS clipboard (standard terminal behaviour) instead of cancelling. With
            // no selection it falls through to the cancel-command path below.
            if (Layout.PaneClipboard.CopySelection(Application.Navigation?.GetFocused()))
            {
                Diag("Ctrl+C → copied focused selection to clipboard");
                return;
            }

            Diag($"Ctrl+C captured (running={runner.IsCommandRunning})");

            var now = DateTime.UtcNow;
            var doubleHit = (now - lastCtrlCAt) < TimeSpan.FromSeconds(2);
            lastCtrlCAt = now;

            if (!runner.IsCommandRunning)
            {
                _toplevel!.Repl.AppendOutputLine("⏹  Ctrl+C — no command running.");
                return;
            }

            if (doubleHit)
            {
                _toplevel!.Repl.AppendOutputLine("⏹  Ctrl+C twice — force-quitting the TUI...");
                runner.RequestCommandCancellation();
                Application.Invoke(() => Application.RequestStop(_toplevel!));
                return;
            }

            _toplevel!.Repl.AppendOutputLine(
                "⏹  Cancellation requested — waiting for the command to honour the CancellationToken. "
                + "Press Ctrl+C again within 2s to force-quit.");
            runner.RequestCommandCancellation();
        };
        Application.KeyDown += ctrlCHandler;
        try
        {
            await RunAsync(runner.RunAsync, ct).ConfigureAwait(false);
        }
        finally
        {
            Application.KeyDown -= ctrlCHandler;
        }
    }

    private async Task RunReplCoreAsync(Func<CancellationToken, Task> repl, CancellationToken ct)
    {
        if (!_initialized) Initialize();
        WriteBannerOnce();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var replTask = Task.Run(async () =>
        {
            try
            {
                await repl(linkedCts.Token).ConfigureAwait(false);
            }
            finally
            {
                Application.Invoke(() => Application.RequestStop(_toplevel!));
            }
        }, linkedCts.Token);

        try
        {
            // Blocks the calling thread (the runner's main thread) until RequestStop fires.
            Diag("Application.Run() entering — main UI loop takes over the main thread");
            Application.Run(_toplevel!, errorHandler: null);
            Diag("Application.Run() returned normally");
        }
        catch (Exception ex)
        {
            Diag($"Application.Run() THREW: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            await linkedCts.CancelAsync().ConfigureAwait(false);
            // The runner is likely blocked in one of two places when Ctrl+Q fires:
            //   (a) IConsoleAdapter.ReadLine() — sync-over-async wrapper around
            //       ReplPaneView.ReadLineAsync(CancellationToken.None) → our cancel
            //       above doesn't unblock it. CancelPendingRead() forces it to return null.
            //   (b) Inside a long-running command (e.g. verify) that's awaiting an LLM
            //       HTTP call. If the command honors the CancellationToken, it'll exit
            //       within the timeout below. If it doesn't, we abandon the task rather
            //       than hang the host — the orphaned task finishes in the background
            //       and is harmless (panes are gone, writes via Application.Invoke after
            //       Shutdown are no-ops).
            _toplevel?.Repl.CancelPendingRead();

            var grace = await Task.WhenAny(replTask, Task.Delay(ShutdownGracePeriod, ct)).ConfigureAwait(false);
            if (grace == replTask)
            {
                try { await replTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { /* expected */ }
            }
            else
            {
                Diag($"REPL task did not honor cancellation within {ShutdownGracePeriod.TotalSeconds:F0}s — abandoning (will finish in background).");
            }
        }
    }

    /// <summary>
    /// Wires the fidelity views to their sources: status line (runner + tokens + tool
    /// spans), hint bar (posture + contextual entries), rule/chip and agents rows. The
    /// turn-completion hook flushes the aggregated tool-activity sentence into the
    /// transcript — the reference's "Read 1 file, ran 9 shell commands" line.
    /// </summary>
    private void BindFidelityViews(Orkeon.Cli.Abstractions.Runners.IInteractiveRunner runner)
    {
        var top = _toplevel!;
        top.Repl.StatusLine.Bind(runner, _integration, _toolActivity);
        top.Repl.RuleChip.Bind(_integration);
        top.HintBar.Bind(
            runner,
            _integration,
            agentsAvailable: _integration.AgentRows is not null,
            configUsable: !string.IsNullOrWhiteSpace(_options.Banner?.ModelLine),
            agentsHasRows: () => top.Agents.DesiredRows > 0);
        if (_integration.AgentRows is not null)
            top.Agents.Bind(_integration);

        top.Repl.StatusLine.TurnCompleted += (_, _) =>
        {
            var sentence = _toolActivity.DrainSentence();
            if (sentence.Length > 0)
            {
                var glyphs = Layout.GlyphSet.Resolve(_options.Glyphs, OutputEncodingIsUtf8());
                foreach (var line in Layout.TranscriptModel.Render(glyphs, Layout.TranscriptKind.ToolActivity, sentence))
                    top.Repl.AppendOutputLine(line);
            }
            top.Repl.RuleChip.Refresh();
        };
    }

    /// <summary>Writes the startup banner into the transcript, once (PLAN phase 2).</summary>
    private void WriteBannerOnce()
    {
        if (_bannerWritten || !_options.BannerEnabled) return;
        _bannerWritten = true;
        var info = _options.Banner ?? new BannerInfo
        {
            ProductLine = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "orkeon",
        };
        var glyphs = Layout.GlyphSet.Resolve(_options.Glyphs, OutputEncodingIsUtf8());
        foreach (var line in Layout.BannerComposer.Compose(info, glyphs))
            _toplevel!.Repl.AppendOutputLine(line);
    }

    private static bool OutputEncodingIsUtf8()
    {
        try
        {
            return System.Console.OutputEncoding.CodePage is 65001;
        }
        catch (System.IO.IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Maximum time we'll wait for the runner to exit its REPL loop after the user
    /// presses Ctrl+Q. Past this window we abandon the task to keep the UI responsive.
    /// </summary>
    private static readonly TimeSpan ShutdownGracePeriod = TimeSpan.FromSeconds(2);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup: a failure restoring the previous TreatControlCAsInput value during disposal must not mask shutdown.")]
    public ValueTask DisposeAsync()
    {
        // Clear the ambient registration so child hosts spawned after shutdown
        // don't try to write into a disposed pane.
        Orkeon.Cli.Abstractions.Logging.AmbientLoggerProvider.Reset();
        // Restore the previous Ctrl+C handling so subsequent processes / re-entry
        // don't inherit our TreatControlCAsInput=true.
        if (_previousTreatControlCAsInput is { } previous)
        {
            try { System.Console.TreatControlCAsInput = previous; }
            catch { /* same exception class as set — best effort restore */ }
            _previousTreatControlCAsInput = null;
        }
        if (_initialized && _ownsApplicationInit)
            Application.Shutdown();
        // Dispose the toplevel window after the application loop has shut down so the
        // owned Terminal.Gui view tree (and its disposable children) is released.
        _toplevel?.Dispose();
        _toplevel = null;
        _toolActivity.Dispose();
        return ValueTask.CompletedTask;
    }
}
