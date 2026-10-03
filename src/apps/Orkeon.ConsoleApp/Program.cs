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
    /// </remarks>
    internal static void ConfigureAppConfiguration(
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
    }

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
