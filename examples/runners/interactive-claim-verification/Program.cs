using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Examples.Interactive.ClaimVerification.Commands;

namespace Orkeon.Examples.Interactive.ClaimVerification;

static class Program
{
    static Task<int> Main(string[] args)
    {
        return Parser.Default.ParseArguments<Options>(args)
            .MapResult(RunAsync, _ => Task.FromResult(1));
    }

    static async Task<int> RunAsync(Options opts)
    {
        var configPath = Path.GetFullPath(opts.ConfigPath);
        if (!File.Exists(configPath))
        {
            await Console.Error.WriteLineAsync($"ERROR: config file not found: {configPath}");
            return 1;
        }

        var claimsRoot = Path.GetFullPath(opts.ClaimsRoot);
        if (!Directory.Exists(claimsRoot))
        {
            await Console.Error.WriteLineAsync($"ERROR: claims root not found: {claimsRoot}");
            return 1;
        }

        var roundsRoot = !string.IsNullOrWhiteSpace(opts.RoundsRoot)
            ? Path.GetFullPath(opts.RoundsRoot)
            : Path.GetFullPath(Path.Combine(claimsRoot, "..", "..", "rounds"));
        Directory.CreateDirectory(roundsRoot);

        var settingsPath = string.IsNullOrWhiteSpace(opts.SettingsPath)
            ? null
            : Path.GetFullPath(opts.SettingsPath);

        UiMode? requestedUi;
        try
        {
            requestedUi = UiFlagParser.ParseValue(opts.Ui);
        }
        catch (ArgumentException ex)
        {
            await Console.Error.WriteLineAsync(ex.Message);
            return 2;
        }
        var effectiveUi = TtyDetector.ResolveEffectiveMode(requestedUi);

        var catalog = new ClaimsCatalog(
            configPath: configPath,
            settingsPath: settingsPath,
            claimsRoot: claimsRoot,
            roundsRoot: roundsRoot,
            verbose: Math.Clamp(opts.Verbose, 0, 2),
            llmLogEnabled: opts.LlmLogEnabled);

        var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(b =>
            {
                if (effectiveUi == UiMode.Tui)
                {
                    // Terminal.Gui owns stdout via the alt-screen buffer; the default Console/Debug
                    // providers would write underneath the UI and corrupt rendering. Strip them and
                    // route logs only through TerminalGuiLoggerProvider (registered below).
                    b.ClearStdoutLoggersForTerminalGui();
                    // In TUI mode let everything reach the provider; the visible filter is owned
                    // by TerminalGuiLoggerProvider (toggled at runtime via F2 in the status bar).
                    // Filtering at the LoggerFactory level here would amputate logs irreversibly.
                    b.SetMinimumLevel(LogLevel.Trace);
                }
                else
                {
                    b.AddSimpleConsole(o =>
                    {
                        o.SingleLine = true;
                        o.TimestampFormat = "HH:mm:ss.fff ";
                    });
                    b.SetMinimumLevel(LogLevel.Warning);
                }
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton(catalog);
                services.AddSingleton<IConsoleAdapter, SystemConsoleAdapter>();
                services.AddSingleton<ConsoleInputService>();
                services.AddOrkeonCli();

                services.AddSingleton<ListCommand>();
                services.AddSingleton<ShowCommand>();
                services.AddSingleton<VerifyCommand>();
                services.AddSingleton<AddClaimCommand>();
                services.AddSingleton<ClaimVerifierCommandRegistry>();
                services.AddSingleton<ClaimVerifierRunner>();

                if (effectiveUi == UiMode.Tui)
                    services.AddOrkeonCliTerminalGui();
            })
            .Build();

        var runner = host.Services.GetRequiredService<ClaimVerifierRunner>();
        using var cts = new CancellationTokenSource();
        // In Plain mode, Ctrl+C cancels the whole runner (standard CLI behaviour).
        // In TUI mode, Terminal.Gui's input loop already routes Ctrl+C to the
        // status bar's "Cancel cmd" shortcut (which calls IInteractiveRunner.RequestCommandCancellation
        // — affecting ONLY the current command). If we ALSO hook Console.CancelKeyPress
        // here, the .NET runtime SIGINT handler races with Terminal.Gui and cancels the
        // entire runner instead of just the command. Skip the hook in TUI mode.
        if (effectiveUi != UiMode.Tui)
        {
            Console.CancelKeyPress += (_, e) =>
            {
                if (cts.IsCancellationRequested) return;
                e.Cancel = true;
                cts.Cancel();
            };
        }

        try
        {
            if (effectiveUi == UiMode.Tui)
            {
                await using var tuiHost = host.Services.GetRequiredService<TerminalGuiHost>();
                // Pass the runner itself so the TUI's status bar can drive cancellation
                // and quit-confirmation against IsCommandRunning / RequestCommandCancellation.
                await tuiHost.RunAsync(runner, cts.Token).ConfigureAwait(false);
            }
            else
            {
                await runner.RunAsync(cts.Token).ConfigureAwait(false);
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
    }
}
