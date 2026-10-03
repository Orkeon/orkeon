using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Compliance.Vfs;
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
using Orkeon.ConsoleApp.Runners;
using Orkeon.ConsoleApp.Services;
using Orkeon.Application.DependencyInjection;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Rag.Onnx.DependencyInjection;
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
    static async Task Main(string[] args)
    {
        if (!TryResolveUiMode(args, out var effectiveUi))
            return;

        if (!TryResolveReplWrap(args, out var replWordWrap))
            return;

        var scriptedOpts = ScriptedCommandsCliOptions.Parse(args);
        var globalSettingsPath = GlobalSettingsPathOrNull();

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((_, builder) => ConfigureAppConfiguration(builder, scriptedOpts, globalSettingsPath))
            .ConfigureServices((context, services) =>
                ConfigureServices(context, services, effectiveUi, scriptedOpts, replWordWrap))
            .Build();

        // GAP-19: a RAG LLM profile (Orkeon:Rag:LlmProfile) the host does not offer refuses the
        // start, listing the known ones — as the runner host does. The name alone is checked.
        RagLlm.EnsureProfileIsKnown(
            host.Services.GetRequiredService<IConfiguration>(),
            host.Services.GetService<Orkeon.Application.Interfaces.Ports.ILlmProfileRegistry>());

        // STUDIO-49: where each LLM key comes from, never the key, and one warning per reference
        // to a variable set nowhere — once, as the runner host says it.
        ConfiguredLlmProviderBootstrapper.ReportApiKeys(
            host.Services.GetRequiredService<IConfiguration>(),
            host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Orkeon.ConsoleApp"));

        await RunHostAsync(host, effectiveUi, scriptedOpts);
    }

    /// <summary>
    /// Parses and resolves the requested UI mode. Returns <c>false</c> (after exiting the
    /// process) when the CLI flags are invalid.
    /// </summary>
    static bool TryResolveUiMode(string[] args, out UiMode effectiveUi)
    {
        UiMode? requestedUi;
        try
        {
            requestedUi = UiFlagParser.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
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
    static bool TryResolveReplWrap(string[] args, out bool replWordWrap)
    {
        try
        {
            replWordWrap = ReplWrapFlagParser.Parse(args) ?? true;
            return true;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Environment.Exit(2);
            replWordWrap = default;
            return false;
        }
    }

    /// <summary>
    /// Layers the settings the runners read over the app's own configuration (STUDIO-49, decision
    /// 10): the <c>--settings &lt;path&gt;</c> JSON files — or, when none is named, the global file
    /// <c>orkeon init</c> writes, as a runner falls back to it —, then the <c>ORKEON_</c>
    /// environment variables, prefix removed, in the order of the runner host. Turns off
    /// <c>reloadOnChange</c> on every file-backed source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>External settings.</b> The host has no built-in way to point at a config file outside
    /// the content root (unlike <c>Scripting.Cli</c>'s <c>-s</c> flag), so an experiment can't
    /// reuse e.g. its <c>appsettings.local.json</c> (LLM model, rate limits, Jint limits) without
    /// duplicating every value as an environment variable. We add the requested files just before
    /// the environment-variable source: they override the app's bundled <c>appsettings.json</c>,
    /// while env vars (notably the LLM API key, which must stay out of committed files) still win.
    /// A file named here replaces the global file rather than layering over it, as for the
    /// runners: the global file's <c>Llm:ApiKeyEnvVar</c> must not send its key to another file's
    /// endpoint.
    /// </para>
    /// <para>
    /// <b>The <c>ORKEON_</c> layer.</b> The default host reads the environment variables without a
    /// prefix only: <c>ORKEON_Llm__ApiKey</c>, the variable every runner and the documentation name,
    /// never reached the REPL as <c>Llm:ApiKey</c>. The layer goes after the unprefixed variables
    /// — it wins over them — and before the command line, which stays last.
    /// </para>
    /// <para>
    /// <b>No reload watcher.</b> The default host enables a recursive <c>FileSystemWatcher</c> over
    /// the content-root tree to hot-reload <c>appsettings.json</c>. On large or slow filesystems
    /// (notably WSL2, where the content root may be a huge tree with <c>.git</c>/<c>bin</c>/
    /// <c>obj</c>/<c>node_modules</c>), <c>StartRaisingEvents()</c> blocks indefinitely registering
    /// inotify watches, hanging boot before the REPL banner is printed. A CLI REPL needs no config
    /// hot-reload, so we disable the watcher on every file source. Mutating the sources here is
    /// safe: the callback runs before <c>HostBuilder.Build()</c> materializes the providers.
    /// </para>
    /// </remarks>
    [SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: --settings paths are resolved to absolute for a JSON config source during host build, before DI/VFS exist. Used as a config-file path, not direct framework I/O.")]
    internal static void ConfigureAppConfiguration(
        IConfigurationBuilder builder,
        ScriptedCommandsCliOptions scriptedOpts,
        string? globalSettingsPath)
    {
        // The named files, else the global one — optional: a fresh machine has none yet.
        var files = scriptedOpts.SettingsFiles.Count > 0
            ? scriptedOpts.SettingsFiles.Select(path => (Path: System.IO.Path.GetFullPath(path), Optional: false)).ToList()
            : globalSettingsPath is not null ? [(globalSettingsPath, true)] : [];

        // Insert before the first env-var source so files override appsettings.json but lose
        // to env vars; if none is present yet, append at the end.
        var insertAt = builder.Sources.Count;
        for (var i = 0; i < builder.Sources.Count; i++)
        {
            if (builder.Sources[i] is EnvironmentVariablesConfigurationSource)
            {
                insertAt = i;
                break;
            }
        }

        foreach (var (path, optional) in files)
        {
            // EXCEPTION-BOOTSTRAP: config-source path resolution runs before DI/VFS exist.
            var source = new JsonConfigurationSource
            {
                Path = path,
                Optional = optional,
                ReloadOnChange = false,
            };
            source.ResolveFileProvider();
            builder.Sources.Insert(insertAt++, source);
        }

        // The ORKEON_ layer, after the last env-var source: it wins over the unprefixed variables
        // and loses to the command line, which the default host adds last.
        var orkeonAt = builder.Sources.Count;
        for (var i = builder.Sources.Count - 1; i >= 0; i--)
        {
            if (builder.Sources[i] is EnvironmentVariablesConfigurationSource)
            {
                orkeonAt = i + 1;
                break;
            }
        }

        builder.Sources.Insert(orkeonAt, new EnvironmentVariablesConfigurationSource { Prefix = OrkeonEnvironmentPrefix });

        foreach (var source in builder.Sources.OfType<FileConfigurationSource>())
            source.ReloadOnChange = false;
    }

    /// <summary>The prefix of the environment variables every runner reads as configuration.</summary>
    internal const string OrkeonEnvironmentPrefix = "ORKEON_";

    /// <summary>
    /// The global settings file <c>orkeon init</c> writes, resolved as the runners resolve it;
    /// null where no per-user directory exists (a bare container without <c>HOME</c>).
    /// </summary>
    static string? GlobalSettingsPathOrNull()
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
        ConfigureLogging(services, effectiveUi);

        services.AddOrkeonApplication();
        // Register the real LLM provider from the `Llm` config section BEFORE the infrastructure
        // defaults: AddOrkeonInfrastructure() only TryAdds a provider built from LlmConfig.OnProfile()
        // (no key, default OpenAI endpoint), which makes ctx.llm.act silently no-op. Binding the
        // configured provider here means the crew runtime actually reaches the model.
        services.AddConfiguredLlmProvider(context.Configuration);
        services.AddOrkeonInfrastructure();
        services.AddOrkeonFileSystem(context.Configuration);
        // Append any --mount specs (e.g. the agent's /workspace) on top of the configured mounts.
        services.AddCliWorkspaceMounts(scriptedOpts.Mounts);

        services.AddOrkeonFileSystemTools();
        services.AddOrkeonDataTools();
        services.AddOrkeonWebTools();
        services.AddOrkeonCodeTools();
        services.AddOrkeonAbstractionTools();
        // E-mail tools (MAIL): password accounts work here; OAuth accounts need the token store
        // the `orkeon` runners mount, and say so when called from the REPL.
        services.AddOrkeonEmailTools(context.Configuration);
        // Session buffer + session_store/session_snip/token_budget tools. The
        // configuration is passed so the session metadata carries `Llm:AvailableModels` —
        // what a scripted /model can offer as a choice.
        services.AddOrkeonSessionTools(context.Configuration);
        // Per-tool-call permission gate — config opt-in
        // (Orkeon:Security:PermissionGate:Enabled = true).
        services.AddOrkeonPermissionGate(context.Configuration);
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
        if (effectiveUi == UiMode.Plain && TtyDetector.IsInteractiveTty())
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
                SpinnerVerbs = context.Configuration.GetSection("Orkeon:Cli:Tui:SpinnerVerbs")
                    .GetChildren().Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v!).ToArray() is { Length: > 0 } verbs ? verbs : null,
            });
        }
    }

    static void ConfigureLogging(IServiceCollection services, UiMode effectiveUi)
    {
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
                builder.AddConsole(); // NOSONAR — standard console logger for development/CLI use
                builder.SetMinimumLevel(LogLevel.Information); // NOSONAR — intentional minimum log level
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

    static async Task RunHostAsync(IHost host, UiMode effectiveUi, ScriptedCommandsCliOptions scriptedOpts)
    {
        // Boot into the scripted-commands REPL directly when the flag is set,
        // otherwise fall back to the main menu.
        Func<CancellationToken, Task> entry = scriptedOpts.BootIntoScriptedRunner
            ? host.Services.GetRequiredService<ScriptedCommandsRunner>().RunAsync
            : host.Services.GetRequiredService<MainMenuRunner>().RunAsync;

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
            await entry(CancellationToken.None);
        }
    }
}
