using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Examples.Interactive.InterviewSpecForge.Commands;
using Orkeon.Examples.Interactive.InterviewSpecForge.Corpus;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Infrastructure.Security;
using Orkeon.Tools.Embeddings.Local;
using AnalysisIEmbeddingProvider = Orkeon.Analysis.Abstractions.Interfaces.IEmbeddingProvider;

namespace Orkeon.Examples.Interactive.InterviewSpecForge;

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

        var transcriptsRoot = Path.GetFullPath(opts.TranscriptsRoot);
        if (!Directory.Exists(transcriptsRoot))
        {
            await Console.Error.WriteLineAsync($"ERROR: transcripts root not found: {transcriptsRoot}");
            return 1;
        }

        var experimentRoot = !string.IsNullOrWhiteSpace(opts.ExperimentRoot)
            ? Path.GetFullPath(opts.ExperimentRoot)
            : Path.GetFullPath(Path.Combine(transcriptsRoot, ".."));
        Directory.CreateDirectory(experimentRoot);

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

        var catalog = new TranscriptsCatalog(
            configPath: configPath,
            settingsPath: settingsPath,
            transcriptsRoot: transcriptsRoot,
            experimentRoot: experimentRoot,
            verbose: Math.Clamp(opts.Verbose, 0, 2),
            llmLogEnabled: opts.LlmLogEnabled);

        var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(cfg =>
            {
                // LocalEmbeddingProvider (VFS-compliant) requires IFileSystemService,
                // whose registry demands at least one mount. The REPL host only ever
                // uses the bundled BGE-micro-v2 model, so a read-only mount of the
                // experiment root is sufficient.
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Orkeon:FileSystem:Mounts:0"] = $"{experimentRoot}:/experiment:ro",
                    ["PathSecurity:AdditionalAllowedDirectories:0"] = experimentRoot,
                });
            })
            .ConfigureLogging(b =>
            {
                if (effectiveUi == UiMode.Tui)
                {
                    b.ClearStdoutLoggersForTerminalGui();
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
            .ConfigureServices((context, services) =>
            {
                services.AddOptions<PathSecurityOptions>().BindConfiguration("PathSecurity");
                services.AddSingleton<IPathValidator, PathValidator>();
                services.AddOrkeonFileSystem(context.Configuration);
                services.AddSingleton(catalog);
                services.AddSingleton<IConsoleAdapter, SystemConsoleAdapter>();
                services.AddSingleton<ConsoleInputService>();
                services.AddOrkeonCli();

                services.AddSingleton<ListCommand>();
                services.AddSingleton<ShowCommand>();
                services.AddSingleton<ForgeCommand>();
                services.AddSingleton<StatusCommand>();
                services.AddSingleton<TopicsCommand>();
                services.AddSingleton<TasksCommand>();
                services.AddSingleton<GlossaryCommand>();
                services.AddSingleton<ReplayCommand>();
                services.AddSingleton<TestEditCommand>();

                // Semantic corpus search (BGE-micro-v2, offline)
                services.AddSingleton<AnalysisIEmbeddingProvider, LocalEmbeddingProvider>();
                services.AddSingleton<CorpusSearchIndex>();
                services.AddSingleton<SearchCommand>();

                services.AddSingleton<InterviewSpecForgeCommandRegistry>();
                services.AddSingleton<InterviewSpecForgeRunner>();

                if (effectiveUi == UiMode.Tui)
                    services.AddOrkeonCliTerminalGui();
            })
            .Build();

        var runner = host.Services.GetRequiredService<InterviewSpecForgeRunner>();
        using var cts = new CancellationTokenSource();
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
