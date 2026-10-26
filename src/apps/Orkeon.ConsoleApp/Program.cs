using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Commands.Scripting.Configuration;
using Orkeon.Cli.Commands.Scripting.DependencyInjection;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.ConsoleApp.Commands;
using Orkeon.ConsoleApp.Commands.Agent;
using Orkeon.ConsoleApp.Commands.Crew;
using Orkeon.ConsoleApp.Commands.Qa;
using Orkeon.ConsoleApp.Commands.Task;
using Orkeon.ConsoleApp.DependencyInjection;
using Orkeon.ConsoleApp.Registries;
using Orkeon.ConsoleApp.Replay;
using Orkeon.ConsoleApp.Runners;
using Orkeon.ConsoleApp.Services;
using static Orkeon.Application.Configuration.SettingsDeclarationExtensions;
using Orkeon.Application.DependencyInjection;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Rag.Onnx.DependencyInjection;
using Orkeon.Tools.Drive.DependencyInjection;
using Orkeon.Tools.Email.DependencyInjection;
using Orkeon.Tools.Abstractions.DependencyInjection;
using Orkeon.Tools.FileSystem.DependencyInjection;
using Orkeon.Tools.Data.DependencyInjection;
using Orkeon.Tools.Web.DependencyInjection;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Tools.Rag.DependencyInjection;
using Orkeon.Tools.Code.DependencyInjection;
using Orkeon.Analysis.DependencyInjection;
using Orkeon.Tools.Analysis.DependencyInjection;
using Orkeon.Tools.Embeddings.Local.DependencyInjection;

namespace Orkeon.ConsoleApp;

static class Program
{
    [Orkeon.Compliance.Vfs.SuppressConsoleCompliance("CONSOLE-SINK: orkeon-repl's entry point hands the process stderr to the startup steps that report before the console opens.")]
    static async Task Main(string[] args)
    {
        var stderr = Console.Error;

        // --replay / --interactive / --log-level: a usage error exits 2, as an invalid --ui does.
        if (ScriptedCommandsCliOptions.Parse(args).UsageError is { } usage)
        {
            await stderr.WriteLineAsync($"orkeon-repl: {usage}");
            Environment.ExitCode = 2;
            return;
        }

        if (!TryResolveUiMode(args, stderr, out var effectiveUi))
            return;

        // --replay alone is a plain shell command: no TUI, no line editor, whatever --ui says.
        if (ScriptedCommandsCliOptions.Parse(args).ReplayNonInteractive)
            effectiveUi = UiMode.Plain;

        if (!TryResolveReplWrap(args, stderr, out var replWordWrap))
            return;

        // GAP-40: the registrations, Logging and the start validation of the runner host — values,
        // keys, names, Orkeon:Rag:LlmProfile among them —, under one barrier: a refused setting is
        // one line and exit 1, before the console opens.
        var launch = ReplStartup.Prepare(args, effectiveUi, replWordWrap, stderr.WriteLine);
        if (launch.Host is not { } host)
        {
            Environment.ExitCode = launch.ExitCode;
            return;
        }

        var scriptedOpts = launch.ScriptedOptions;

        // STUDIO-49: where each LLM key comes from, never the key, and one warning per reference
        // to a variable set nowhere — once, as the runner host says it.
        ConfiguredLlmProviderBootstrapper.ReportApiKeys(
            host.Services.GetRequiredService<IConfiguration>(),
            host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Orkeon.ConsoleApp"));

        await RunHostAsync(host, effectiveUi, scriptedOpts, stderr);
    }

    /// <summary>
    /// Ctrl-C while a replay runs, in the plain console: the command in flight is cancelled and the replay stops
    /// at its line, instead of the process being killed. Without a replay running the key keeps its effect. The
    /// TUI handles the key itself: there, cancelling the command stops the replay.
    /// </summary>
    [Orkeon.Compliance.Vfs.SuppressConsoleCompliance("CONSOLE-SINK: orkeon-repl's entry point owns the process's Ctrl-C while a replay runs in the plain console.")]
    static CancelKeyRegistration HandleReplayInterrupt(ReplayConsoleAdapter replay, Orkeon.Cli.Abstractions.Runners.IInteractiveRunner runner)
    {
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            if (!replay.Interrupt())
                return;
            e.Cancel = true;
            runner.RequestCommandCancellation();
        };
        Console.CancelKeyPress += handler;
        return new CancelKeyRegistration(handler);
    }

    private sealed class CancelKeyRegistration(ConsoleCancelEventHandler handler) : IDisposable
    {
        [Orkeon.Compliance.Vfs.SuppressConsoleCompliance("CONSOLE-SINK: removes the Ctrl-C handler HandleReplayInterrupt added.")]
        public void Dispose() => Console.CancelKeyPress -= handler;
    }

    /// <summary>
    /// Parses and resolves the requested UI mode. Returns <c>false</c> (after exiting the
    /// process) when the CLI flags are invalid.
    /// </summary>
    static bool TryResolveUiMode(string[] args, TextWriter stderr, out UiMode effectiveUi)
    {
        UiMode? requestedUi;
        try
        {
            requestedUi = UiFlagParser.Parse(args);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine(ex.Message);
            Environment.Exit(2);
            effectiveUi = default;
            return false;
        }

        effectiveUi = TtyDetector.ResolveEffectiveMode(requestedUi);
        return true;
    }

    /// <summary>
    /// Resolves the <c>--repl-wrap [on|off]</c> flag. Defaults to <c>true</c> (wrap on, no horizontal
    /// scrollbar) when absent. Returns <c>false</c> (after exiting the process) on an invalid value.
    /// </summary>
    static bool TryResolveReplWrap(string[] args, TextWriter stderr, out bool replWordWrap)
    {
        try
        {
            replWordWrap = ReplWrapFlagParser.Parse(args) ?? true;
            return true;
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine(ex.Message);
            Environment.Exit(2);
            replWordWrap = default;
            return false;
        }
    }

    /// <summary>
    /// Composes the REPL's configuration as every runner composes its own (STUDIO-49, decision 10;
    /// GAP-36, decision 5): <see cref="Orkeon.Hosting.RunnerSettings.ComposeSources(IConfigurationBuilder, IEnumerable{string}, bool)"/>
    /// — the environment variables without a prefix, the settings files, then the <c>ORKEON_</c>
    /// variables, prefix removed — over the <c>--settings &lt;path&gt;</c> files or, when none is
    /// named, the global file <c>orkeon init</c> writes, as a runner falls back to it; then the
    /// command line the default host parsed, which stays last.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Settings files.</b> A file named here replaces the global file rather than layering over it,
    /// as for the runners: the global file's <c>Llm:ApiKeyEnvVar</c> must not send its key to another
    /// file's endpoint. A named file must exist; the global one may not, on a fresh machine.
    /// </para>
    /// <para>
    /// <b>Nothing else.</b> The default host laid the <c>appsettings.json</c> and
    /// <c>appsettings.{Environment}.json</c> of the current directory under the files — the REPL
    /// project's own when started from its folder, any other project's elsewhere; since .NET 10,
    /// <c>&lt;application&gt;.settings.json</c> too — and, in
    /// <c>Development</c>, its user secrets; and the files went under the bare variables, where the
    /// runners lay them over. A file the REPL did not name configures it no more, and the order is the
    /// runners'.
    /// </para>
    /// <para>
    /// <b>No reload watcher.</b> The default host watched the current directory's tree to hot-reload
    /// its <c>appsettings.json</c>: on large or slow filesystems (WSL2, a tree with <c>.git</c>,
    /// <c>bin</c>, <c>node_modules</c>), registering the watches blocked the boot before the banner.
    /// No source of the composition watches its file.
    /// </para>
    /// <para>
    /// <b>The mounts.</b> Over those layers, the <c>--mount</c> arguments are placed as a runner places
    /// its own (GAP-49, decision 3, <see cref="Orkeon.Hosting.RunnerHost.ConfigureMounts"/>): a
    /// <c>--mount</c> on a declared root takes the place of its entries for the session, one on a new
    /// root is appended after the last; the declared entries in force are readable outside the working
    /// directory, a <c>--mount</c> under <c>--allow-external-mounts</c>. What the placement decided is
    /// logged once the host exists.
    /// </para>
    /// </remarks>
    internal static void ConfigureAppConfiguration(
        IConfigurationBuilder builder,
        ScriptedCommandsCliOptions scriptedOpts,
        string? globalSettingsPath,
        Orkeon.Hosting.RunnerHost.MountDecisions? decisions = null)
    {
        ComposeSettingsLayers(builder, scriptedOpts, globalSettingsPath);

        Orkeon.Hosting.RunnerHost.ConfigureMounts(
            builder,
            new Orkeon.Hosting.RunnerMountPlan
            {
                CliMounts = CliWorkspaceMountBootstrapper.ResolvePhysicalSegments(scriptedOpts.Mounts),
                // The exchange log (--llm-log), as orkeon run mounts it: reachable by the VFS, invisible to agents.
                InternalMounts = Orkeon.Hosting.RunnerLlmLog.CreateMounts(scriptedOpts.ResolvedLlmLogPath),
                InputDirectories = [.. scriptedOpts.CommandDirs, .. scriptedOpts.CrewDirs],
                AllowExternalMounts = scriptedOpts.EffectiveAllowExternalMounts,
            },
            SettingsLabel(scriptedOpts, globalSettingsPath),
            decisions ?? new Orkeon.Hosting.RunnerHost.MountDecisions());
    }

    /// <summary>
    /// The REPL's settings layers, without its mounts: the environment without a prefix, the
    /// <c>--settings</c> files or the global file, the <c>ORKEON_</c> environment, then the command line
    /// the builder already holds, kept last. What the start guards read, and what the host's
    /// configuration places the <c>--mount</c> arguments over.
    /// </summary>
    /// <param name="builder">A configuration builder holding the command line, if any.</param>
    /// <param name="scriptedOpts">The REPL's options.</param>
    /// <param name="globalSettingsPath">The global file <c>orkeon init</c> writes, or null.</param>
    internal static IConfigurationBuilder ComposeSettingsLayers(
        IConfigurationBuilder builder,
        ScriptedCommandsCliOptions scriptedOpts,
        string? globalSettingsPath)
    {
        // The command line the default host parsed, kept for the end: it stays the last layer.
        var commandLine = builder.Sources.OfType<CommandLineConfigurationSource>().ToList();

        if (scriptedOpts.SettingsFiles.Count > 0)
            Orkeon.Hosting.RunnerSettings.ComposeSources(builder, scriptedOpts.SettingsFiles, optional: false);
        else
            Orkeon.Hosting.RunnerSettings.ComposeSources(builder, globalSettingsPath is null ? [] : [globalSettingsPath], optional: true);

        foreach (var source in commandLine)
            builder.Add(source);
        return builder;
    }

    /// <summary>
    /// What a mount refusal names the REPL's settings by: its <c>--settings</c> files, separated by a
    /// comma, else the global file when it exists; null when only the environment and the command
    /// line declare anything.
    /// </summary>
    /// <param name="scriptedOpts">The REPL's options.</param>
    /// <param name="globalSettingsPath">The global file <c>orkeon init</c> writes, or null.</param>
    [Orkeon.Compliance.Vfs.SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: names the settings files the REPL reads, before any VFS mount exists.")]
    internal static string? SettingsLabel(ScriptedCommandsCliOptions scriptedOpts, string? globalSettingsPath)
    {
        if (scriptedOpts.SettingsFiles.Count > 0)
            return string.Join(", ", scriptedOpts.SettingsFiles.Select(Path.GetFullPath));

        return globalSettingsPath is not null && File.Exists(globalSettingsPath) ? globalSettingsPath : null;
    }

    /// <summary>
    /// The global settings file <c>orkeon init</c> writes, resolved as the runners resolve it;
    /// null where no per-user directory exists (a bare container without <c>HOME</c>).
    /// </summary>
    internal static string? GlobalSettingsPathOrNull()
    {
        try
        {
            return Orkeon.Hosting.RunnerSettings.GetGlobalSettingsPath();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    internal static void ConfigureServices(
        HostBuilderContext context,
        IServiceCollection services,
        UiMode effectiveUi,
        ScriptedCommandsCliOptions scriptedOpts,
        bool replWordWrap)
    {
        ConfigureLogging(services, effectiveUi, scriptedOpts);

        services.AddOrkeonApplication();
        // Register the real LLM provider from the `Llm` config section BEFORE the infrastructure
        // defaults: AddOrkeonInfrastructure() only TryAdds a provider built from LlmConfig.OnProfile()
        // (no key, default OpenAI endpoint), which makes ctx.llm.act silently no-op. Binding the
        // configured provider here means the crew runtime actually reaches the model.
        services.AddConfiguredLlmProvider(context.Configuration);
        services.AddOrkeonInfrastructure();
        // The --mount specs (e.g. the agent's /workspace) are in the configuration already, placed by
        // root over the declared entries as a runner places them (ConfigureAppConfiguration).
        // MCP:Servers, as the runners read it: registered when servers are declared, connected by the
        // runner before it loads the commands (ScriptedCommandsRunner.OnStartAsync).
        Orkeon.Hosting.RunnerHost.RegisterMcp(context, services);
        // --llm-log / --llm-log-path: the exchange log orkeon run writes, on the folder mounted above.
        if (scriptedOpts.ResolvedLlmLogPath is not null)
            Orkeon.Hosting.RunnerHost.RegisterLlmExchangeLogging(context, services, Orkeon.Constants.FileSystem.RunnerVirtualRoots.LlmLogs);
        services.AddOrkeonFileSystem(context.Configuration);

        services.AddOrkeonFileSystemTools();
        services.AddOrkeonDataTools();
        services.AddOrkeonWebTools();
        services.AddOrkeonCodeTools();
        // Orkeon:Tools:Shell, bound by the code tools, declared here as orkeon run declares it (GAP-40).
        Orkeon.Hosting.RunnerHost.DeclareShellSettings(services);
        services.AddOrkeonAbstractionTools();
        // E-mail tools (MAIL): password accounts work here; OAuth accounts need the token store
        // the `orkeon` runners mount, and say so when called from the REPL.
        services.AddOrkeonEmailTools(context.Configuration);
        // Drive tools (DRIVE): every drive account signs in with OAuth, whose tokens only the
        // `orkeon` runners keep; called from the REPL, a drive tool says so.
        services.AddOrkeonDriveTools(context.Configuration);
        // Session buffer + session_store/session_snip/token_budget tools. The
        // configuration is passed so the session metadata carries `Llm:AvailableModels` —
        // what a scripted /model can offer as a choice.
        services.AddOrkeonSessionTools(context.Configuration);
        // The plugins of the Plugins section: a plugin tool reaches the scripts like a built-in one,
        // and one named like a built-in tool refuses the start (the tool registry names both).
        services.AddReplPlugins(context.Configuration);
        // Per-tool-call permission gate — config opt-in
        // (Orkeon:Security:PermissionGate:Enabled = true).
        services.AddOrkeonPermissionGate(context.Configuration);
        // Its approval channel: a call the gate asks about is put to the operator, whose
        // next line answers it (the read loop hands the line over).
        services.AddConsoleToolApproval();
        // Native incremental rendering of streamed act() output — config
        // opt-in (Orkeon:Cli:ConsoleStreaming:Enabled = true). Registered here (after the
        // IConsoleAdapter choice below is declared later in this method, resolution is
        // lazy) so the REPL streams tokens without scripts passing onDelta.
        services.AddLlmConsoleStreaming(context.Configuration);

        // RaggableTree — semantic codebase index + agent tools (codebase_map/search,
        // symbol_source, flow_trace, impact_analysis, index_codebase, …). On-device embeddings
        // first (zero-config BGE-micro-v2, no network), then the index services, then the tools.
        // Languages are intentionally left empty: the analysed scope is auto-detected from the
        // codebase and refined per index_codebase call by the crew/agent — never pinned a priori.
        services.AddOrkeonLocalEmbeddings();
        // RAG subsystem + agent tools (rag_search / rag_ingest / rag_eval). Tolerates a
        // missing Orkeon:Rag section (profile defaults + in-memory document store); the
        // ambient IMemoryProvider comes from AddOrkeonInfrastructure above. rag_search's
        // "raggable-tree" collection routes to the code index and therefore inherits the
        // hybrid BM25+RRF search and the lazy freshness pass.
        services.AddOrkeonRag(context.Configuration);
        services.AddOrkeonRagTools();
        // The ONNX cross-encoder the balanced and quality profiles rerank with (GAP-25), as
        // orkeon and orkeon-host register it. Embedded weights, loaded on first use.
        services.AddOrkeonOnnxReranker();
        services.AddRaggableTree(new RaggableTreeOptions
        {
            Embedding = new EmbeddingOptions { Provider = EmbeddingProviderKind.LocalSmartComponents },
        });
        services.AddRaggableTreeTools();
        // Route index-build notifications into the CLI progress broker (status-line bar).
        // The broker itself is registered by AddScriptCommands below; resolution is lazy.
        services.AddSingleton<IProgress<Orkeon.Analysis.Abstractions.Models.IndexBuildProgress>>(sp =>
            new Orkeon.ConsoleApp.Services.IndexProgressBrokerAdapter(
                sp.GetRequiredService<Orkeon.Cli.Commands.Scripting.Progress.ProgressBroker>()));

        // Console adapter — replaced by TerminalGuiConsoleAdapter when --ui=tui (see below).
        // In plain mode on a real TTY we use the raw-mode line editor, which adds history,
        // Tab completion and @-path completion. For redirected/CI input we keep the dumb
        // adapter instead, so pipes and tests stay byte-exact.
        // A --replay without --interactive is a plain shell command: the dumb adapter, never the line editor.
        if (effectiveUi == UiMode.Plain && TtyDetector.IsInteractiveTty() && !scriptedOpts.ReplayNonInteractive)
            services.AddSingleton<IConsoleAdapter, LineEditingConsoleAdapter>();
        else
            services.AddSingleton<IConsoleAdapter, SystemConsoleAdapter>();
        services.AddSingleton<ConsoleInputService>();
        // REPL Tab-completion source (/command names + @path from the VFS). Consumed by the
        // Terminal.Gui ReplPaneView; harmless when running in plain mode.
        services.AddSingleton<IReplInputAssist, Orkeon.ConsoleApp.Services.ReplInputAssist>();

        services.AddSingleton<AgentManagementService>();
        services.AddSingleton<CrewManagementService>();
        services.AddSingleton<TaskManagementService>();

        // Orkeon.Cli (default commands + DefaultCommandRegistry)
        services.AddOrkeonCli();

        // Q&A runner + its fallback command
        services.AddSingleton<AskQuestionCommand>();
        services.AddSingleton<QaCommandRegistry>(sp =>
            new QaCommandRegistry(sp.GetRequiredService<AskQuestionCommand>()));
        services.AddSingleton<QaRunner>();

        RegisterMainMenuCommands(services);

        // Scripted commands (Phase 4 demo). PassThroughTranspiler when esbuild isn't available
        // is OK for the bundled fixtures which are plain JS-in-TS-syntax.
        services.AddScriptCommands(context.Configuration, cfg => ApplyScriptedOptions(cfg, scriptedOpts));
        // Bootstrap VFS mounts for --commands-dir paths (option Q7=a — explicit
        // bootstrap via PostConfigure, no in-memory configuration overlay).
        services.AddScriptCommandMounts(scriptedOpts.CommandDirs);
        // Bootstrap VFS mounts + script-host CrewDirectories for --crews-dir paths, so commands
        // (e.g. assistant) can resolve crews by name via runCrewAsync("main-loop").
        services.AddScriptHostCrewMounts(scriptedOpts.CrewDirs);
        services.AddSingleton<ScriptedCommandsRunner>();

        // Orkeon:Cli:Tui, read raw below: its keys are judged at the start (GAP-40).
        services.DeclareSettingsShape(TuiSection, typeof(TuiSettingsShape));
        if (effectiveUi == UiMode.Tui)
        {
            services.AddOrkeonCliTerminalGui(new TerminalGuiOptions
            {
                ReplWordWrap = replWordWrap,
                // Banner content comes from the configuration this host actually loaded —
                // the TUI layer cannot (and must not) probe the LLM section itself.
                Banner = Orkeon.ConsoleApp.Services.TuiFidelityWiring.BuildBannerInfo(context.Configuration),
                // Boot-time spinner verbs (appsettings). The live /config layer, read via
                // TuiIntegration.SpinnerVerbs, wins over this when set.
                SpinnerVerbs = context.Configuration.GetSection($"{TuiSection}:SpinnerVerbs")
                    .GetChildren().Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v!).ToArray() is { Length: > 0 } verbs ? verbs : null,
            });
        }

        // Last, over the console chosen above (the TUI's included): --replay and /replay feed it their lines.
        // The plain prompt is on the line already; the TUI's is a label, so its echo carries a marker.
        services.AddReplReplay(effectiveUi == UiMode.Tui ? "> " : string.Empty);
    }

    /// <summary>The section of the Terminal.Gui console's own settings.</summary>
    const string TuiSection = "Orkeon:Cli:Tui";

    /// <summary>The split-pane console's own settings.</summary>
    /// <remarks>Its property is the key; nothing binds it.</remarks>
    internal sealed class TuiSettingsShape
    {
        /// <summary>
        /// The verbs the status line cycles through while the model works ("Thinking", "Reading"…).
        /// Left out or empty, the console's own list.
        /// </summary>
        public List<string>? SpinnerVerbs { get; set; }
    }

    static void ConfigureLogging(IServiceCollection services, UiMode effectiveUi, ScriptedCommandsCliOptions scriptedOpts)
    {
        // The Logging section, which the generic host builds this console's logger from and the
        // start judges first: read, so the settings catalogue lists it for this binary too.
        services.AddSingleton(Orkeon.Hosting.RunnerHost.LoggingSettings);

        // NOSONAR — console logger only, no remote sinks; no secrets are logged (LLM keys are
        // redacted in the HTTP provider layer before reaching the logger). Safe by review.
        services.AddLogging(builder => // NOSONAR
        {
            if (effectiveUi == UiMode.Tui)
            {
                // Strip console-style providers (they trash the Terminal.Gui alt-screen).
                // Logs flow exclusively through TerminalGuiLoggerProvider.
                builder.ClearStdoutLoggersForTerminalGui();
                // Let everything reach the provider in TUI mode; the visible filter
                // is owned by TerminalGuiLoggerProvider (toggled via F2 at runtime).
                builder.SetMinimumLevel(LogLevel.Trace);
            }
            else
            {
                // Under --replay alone, every log line goes to stderr: stdout carries the replayed session only.
                if (scriptedOpts.ReplayNonInteractive)
                    builder.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace); // NOSONAR — standard console logger, on stderr
                else
                    builder.AddConsole(); // NOSONAR — standard console logger for development/CLI use
                // --log-level, else Information. The Logging section's rules still win, as for every host.
                builder.SetMinimumLevel(scriptedOpts.LogLevel ?? LogLevel.Information); // NOSONAR — intentional minimum log level
            }
        });
    }

    static void RegisterMainMenuCommands(IServiceCollection services)
    {
        // Main menu commands (registered as IMainMenuCommand for auto-discovery)
        services.AddSingleton<IMainMenuCommand, ListAgentsCommand>();
        services.AddSingleton<IMainMenuCommand, CreateAgentCommand>();
        services.AddSingleton<IMainMenuCommand, DeleteAgentCommand>();
        services.AddSingleton<IMainMenuCommand, ListCrewsCommand>();
        services.AddSingleton<IMainMenuCommand, CreateCrewCommand>();
        services.AddSingleton<IMainMenuCommand, AddAgentToCrewCommand>();
        services.AddSingleton<IMainMenuCommand, DeleteCrewCommand>();
        services.AddSingleton<IMainMenuCommand, RunCrewCommand>();
        services.AddSingleton<IMainMenuCommand, ListTasksCommand>();
        services.AddSingleton<IMainMenuCommand, CreateTaskCommand>();
        services.AddSingleton<IMainMenuCommand, AssignTaskCommand>();
        services.AddSingleton<IMainMenuCommand, DeleteTaskCommand>();
        services.AddSingleton<IMainMenuCommand, DemoCommand>();
        services.AddSingleton<IMainMenuCommand, EnterQaModeCommand>();
        services.AddSingleton<IMainMenuCommand, EnterScriptedModeCommand>();

        services.AddSingleton<MainMenuCommandRegistry>();
        services.AddSingleton<MainMenuRunner>();
    }

    static void ApplyScriptedOptions(ScriptCommandsConfiguration cfg, ScriptedCommandsCliOptions scriptedOpts)
    {
        if (scriptedOpts.Disabled) cfg.Enabled = false;
        if (scriptedOpts.Strict)
        {
            cfg.FailFastOnInvalidScript = true;
            cfg.ContinueOnConflict = false;
        }
    }

    static async Task RunHostAsync(IHost host, UiMode effectiveUi, ScriptedCommandsCliOptions scriptedOpts, TextWriter stderr)
    {
        // --replay: the file is read before anything runs; missing or unreadable, exit 2 and the REPL never opens.
        var replay = host.Services.GetRequiredService<ReplayConsoleAdapter>();
        if (scriptedOpts.ReplayFile is { } replayFile)
        {
            var load = await ReplayFile.LoadAsync(
                host.Services.GetRequiredService<Orkeon.Domain.FileSystem.IFileSystemService>(), replayFile, CancellationToken.None);
            if (load.Lines is not { } lines)
            {
                await stderr.WriteLineAsync($"orkeon-repl: {load.Error}");
                Environment.ExitCode = ReplayConsoleAdapter.UnreadableExitCode;
                return;
            }

            replay.TryStart(lines, closeInputAtEnd: !scriptedOpts.ReplayInteractive);
        }

        // The durable session loads for the project (the workspace root), then the start script runs once,
        // and the runner saves the session when the REPL ends. A hook that was not registered (a bare host) is skipped.
        if (host.Services.GetService<Orkeon.Application.Interfaces.Ports.ISessionBufferService>() is Orkeon.Infrastructure.Session.FileSessionBufferService durable)
        {
            // The project key is the physical root the REPL mounted as /workspace, resolved by the VFS (not the
            // virtual "/workspace", identical for every project); the persisted path holds only its name and hash.
            var workspace = host.Services.GetRequiredService<Orkeon.Domain.FileSystem.IFileSystemService>()
                .ResolveAndValidate("/workspace", Orkeon.Domain.FileSystem.FileAccessRights.Read);
            await durable.LoadAsync(workspace.ResolvedPath ?? "/workspace", CancellationToken.None);
        }
        if (host.Services.GetService<Orkeon.ConsoleApp.Services.ReplStartScriptHook>() is { } startHook)
            await startHook.RunOnceAsync(CancellationToken.None);

        // Boot into the scripted-commands REPL directly when the flag is set,
        // otherwise fall back to the main menu.
        Orkeon.Cli.Abstractions.Runners.IInteractiveRunner runner = scriptedOpts.BootIntoScriptedRunner
            ? host.Services.GetRequiredService<ScriptedCommandsRunner>()
            : host.Services.GetRequiredService<MainMenuRunner>();
        Func<CancellationToken, Task> entry = runner.RunAsync;

        if (effectiveUi == UiMode.Tui)
        {
            await using var tuiHost = host.Services.GetRequiredService<TerminalGuiHost>();
            // Close the fidelity delegates over the built provider (session state bag,
            // cost tracker, command registry) — see TuiFidelityWiring.
            Orkeon.ConsoleApp.Services.TuiFidelityWiring.Wire(host.Services);
            // Prefer the runner overload: it binds the status line / hint bar / agents
            // views to the command lifecycle. The Func path never could (it has no
            // IInteractiveRunner), which is why the old tasks bandeau stayed idle in
            // exactly the mode that runs commands.
            if (scriptedOpts.BootIntoScriptedRunner)
                await tuiHost.RunAsync(host.Services.GetRequiredService<ScriptedCommandsRunner>(), CancellationToken.None);
            else
                await tuiHost.RunAsync(entry, CancellationToken.None);
        }
        else
        {
            using var interrupt = HandleReplayInterrupt(replay, runner);
            await entry(CancellationToken.None);
        }

        // --replay alone: 0 when every line passed, 1 when it stopped on an error (said on the console).
        var replayExitCode = replay.Conclude();
        if (scriptedOpts.ReplayNonInteractive)
            Environment.ExitCode = replayExitCode;
    }
}
