using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces;
using Orkeon.Compliance.Vfs;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Domain.SharedKernel;
using Orkeon.Infrastructure.Crew;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Scripting;
using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Toolchain;

namespace Orkeon.Hosting;

/// <summary>
/// Shared execution glue for Orkeon runners: graceful shutdown (SIGTERM/SIGINT),
/// AutoSummaryWriter wiring on <c>/output:rw</c> mounts, verbosity presets, and the
/// one-shot kickoff and interactive-loop flows used by all runners.
/// </summary>
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: validates user-supplied config/log paths and injects the corresponding VFS mounts before the host (and thus IFileSystemService) is built.")]
public static partial class RunnerExecution
{
    /// <summary>
    /// Bootstrap result shared by <see cref="RunOneShotAsync"/> and
    /// <see cref="RunInteractiveLoopAsync"/>: a fully built host plus the
    /// runner-level metadata both flows need.
    /// </summary>
    internal sealed record HostBootstrap(
        IHost Host,
        ILogger Logger,
        string ConfigPath,
        IReadOnlyList<string> CliMounts);

    /// <summary>
    /// Resolves config/settings paths, validates the external-mounts guard,
    /// auto-injects the configDir and (if any) llmLogPath mounts, builds the
    /// host with verbosity-driven logging, and wires AutoSummaryWriter when an
    /// <c>/output:rw</c> mount is present. Returns an error exit code on
    /// invalid input; the caller must check <paramref name="errorCode"/>.
    /// </summary>
    internal static bool TryBuildHost(
        RunnerOptionsBase opts,
        string loggerCategory,
        Action<HostBuilderContext, IServiceCollection>? configureServices,
        out HostBootstrap? bootstrap,
        out int errorCode)
    {
        ArgumentNullException.ThrowIfNull(opts);
        bootstrap = null;
        errorCode = 0;

        var configPath = Path.GetFullPath(opts.ConfigPath);
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"ERROR: config file not found: {configPath}");
            errorCode = 1;
            return false;
        }

        var configDir = Path.GetDirectoryName(configPath)!;
        var settingsPath = RunnerSettings.ResolveSettingsPath(opts.SettingsPath, configDir);
        if (settingsPath != null)
            Console.Error.WriteLine($"Using settings: {settingsPath}");

        var cliMounts = opts.Mounts.ToList();
        var llmLogPath = opts.ResolvedLlmLogPath;

        // The crew-config loader (YamlCrewDefinitionLoader) reads the YAML through
        // IFileSystemService, so configDir must be visible to the VFS registry.
        // Same rationale applies to the LLM log directory (AppendAllTextAsync) when
        // --llm-log[-path] is set. We inject 1:1 mounts (physical = virtual) so the
        // VFS resolves the absolute paths that framework code already computes.
        var cwd = Directory.GetCurrentDirectory();
        var configOutsideCwd = !configDir.StartsWith(cwd, StringComparison.Ordinal);
        var llmLogOutsideCwd = llmLogPath != null && !llmLogPath.StartsWith(cwd, StringComparison.Ordinal);
        if ((configOutsideCwd || llmLogOutsideCwd) && !opts.AllowExternalMounts)
        {
            // Conservative: external paths require opt-in. Avoids silently widening
            // the VFS surface for users who expect workspace-relative execution.
            Console.Error.WriteLine(
                "ERROR: --allow-external-mounts is required when reading the crew config "
                + "or writing LLM logs outside the current working directory. Add "
                + "--allow-external-mounts to proceed.");
            if (configOutsideCwd)
                Console.Error.WriteLine($"       configDir   : {configDir}");
            if (llmLogOutsideCwd)
                Console.Error.WriteLine($"       llmLogPath  : {llmLogPath}");
            Console.Error.WriteLine($"       cwd         : {cwd}");
            errorCode = 1;
            return false;
        }
        cliMounts.Insert(0, $"{configDir}:{configDir}:ro");
        if (llmLogPath != null)
            cliMounts.Insert(1, $"{llmLogPath}:{llmLogPath}:rw");

        var verbosity = Math.Clamp(opts.Verbose, 0, 2);
        var outputMountPath = DetectOutputMountPath(cliMounts);

        var host = RunnerHost.Build(
            settingsPath, cliMounts,
            allowExternalMounts: opts.AllowExternalMounts,
            llmLogPath: llmLogPath,
            configureLogging: verbosity > 0
                ? (_, b) => ConfigureVerboseLogging(b, verbosity)
                : null,
            configureServices: (ctx, services) =>
            {
                if (outputMountPath != null)
                {
                    services.AddScoped<ICrewExecutionHook>(sp =>
                        new AutoSummaryWriter(
                            sp.GetRequiredService<IFileSystemService>(),
                            outputMountPath,
                            sp.GetRequiredService<ILogger<AutoSummaryWriter>>()));
                }
                // Default human-input wiring: registers the `human_input` tool +
                // AutoApprove provider as a fallback. Interactive runners override
                // the provider (e.g. TerminalGuiHumanInputProvider) via
                // configureServices below.
                services.AddOrkeonHumanInput();
                configureServices?.Invoke(ctx, services);
            });

        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(loggerCategory);

        if (llmLogPath != null)
            LogLlmExchangeLoggingEnabled(logger, llmLogPath);

        bootstrap = new HostBootstrap(host, logger, configPath, cliMounts);
        return true;
    }

    /// <summary>
    /// Runs a one-shot crew kickoff end-to-end. Handles settings resolution,
    /// AutoSummaryWriter registration (when an <c>/output:rw</c> mount is declared),
    /// SIGTERM/SIGINT graceful cancellation, and standard exit codes.
    /// </summary>
    /// <param name="opts">Parsed runner options.</param>
    /// <param name="loggerCategory">Logger category used for runner-level log messages.</param>
    /// <param name="configureServices">Optional hook to register runner-specific services.</param>
    /// <param name="externalCt">
    /// Optional external cancellation token. When the caller is itself running inside
    /// another runner (e.g. <c>VerifyCommand</c> inside the TUI's REPL loop), pass the
    /// command's <see cref="CancellationToken"/> here so the inner CTS is linked to it.
    /// Without this, Ctrl+C inside the TUI cannot reach the inner crew (TUI mode disables
    /// the runtime SIGINT path that <c>RegisterGracefulShutdown</c> relies on).
    /// </param>
    /// <returns>Exit code: 0 = success, 1 = config error, 2 = crew failure, 130 = canceled.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Top-level fault barrier: any crew execution failure is logged and converted to exit code 2 so the runner exits cleanly instead of crashing. Cancellation maps to exit 130.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    public static Task<int> RunOneShotAsync(
        RunnerOptionsBase opts,
        string loggerCategory,
        Action<HostBuilderContext, IServiceCollection>? configureServices = null,
        CancellationToken externalCt = default)
    {
        ArgumentNullException.ThrowIfNull(opts);
        return RunOneShotCoreAsync();

        async Task<int> RunOneShotCoreAsync()
        {
        if (!TryBuildHost(opts, loggerCategory, configureServices, out var bootstrap, out var errorCode))
            return errorCode;

        var (host, logger, configPath, cliMounts) = (bootstrap!.Host, bootstrap.Logger, bootstrap.ConfigPath, bootstrap.CliMounts);

        // Internal CTS linked to the external one (if any). Cancelling either path stops
        // the crew: SIGINT/SIGTERM via RegisterGracefulShutdown, OR caller's externalCt.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        using var shutdown = RegisterGracefulShutdown(cts, logger);

        try
        {
            RunnerLogging.LogMounts(cliMounts, logger);

            LogLoadingCrew(logger, configPath);

            var factory = host.Services.GetRequiredService<ICrewFactory>();
            var crew = IsScriptedCrewDefinition(configPath)
                ? await LoadCrewFromScriptAsync(host, factory, configPath, logger, cts.Token).ConfigureAwait(false)
                : await factory.CreateFromFileAsync(configPath, cts.Token).ConfigureAwait(false);

            var orchestrator = host.Services.GetRequiredService<ICrewOrchestrationService>();

            CrewInput input;
            try
            {
                var vars = opts.ParseVariables();
                input = vars.Count > 0 || !string.IsNullOrEmpty(opts.InitialContext)
                    ? CrewInput.WithStringVariables(opts.InitialContext, vars)
                    : CrewInput.Empty();
            }
            catch (FormatException ex)
            {
                await Console.Error.WriteLineAsync($"ERROR: {ex.Message}").ConfigureAwait(false);
                return 1;
            }

            LogKickingOffCrew(logger, crew.Goal);
            var output = await orchestrator.KickoffAsync(crew.Id, input, cts.Token).ConfigureAwait(false);

            Console.WriteLine();
            Console.WriteLine("=== Crew Output ===");
            Console.WriteLine(output.FinalOutput);
            Console.WriteLine();
            Console.WriteLine($"Duration: {output.Duration}");
            Console.WriteLine(output.TokensUsed is { } usage
                ? $"Tokens used: {usage.TotalTokens}"
                : "Tokens used: (not measured)");

            return 0;
        }
        catch (OperationCanceledException)
        {
            LogCrewExecutionCanceled(logger);
            return 130;
        }
        catch (Exception ex)
        {
            LogCrewExecutionFailed(logger, ex);
            return 2;
        }
        }
    }

    /// <summary>
    /// Runs an interactive question/answer loop. Reuses the same settings/host/mount
    /// auto-injection logic as <see cref="RunOneShotAsync"/>: SIGTERM aborts the loop
    /// (exit 130), per-question SIGINT cancels only the current kickoff, and a stop
    /// word terminates the loop with exit 0. The kickoff body is delegated to
    /// <paramref name="kickoffPerInputAsync"/> so callers control crew lookup and
    /// CrewInput shape.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    public static Task<int> RunInteractiveLoopAsync(
        RunnerOptionsBase opts,
        string loggerCategory,
        IReadOnlySet<string> stopWords,
        Func<IServiceProvider, string, CancellationToken, Task<CrewOutput>> kickoffPerInputAsync,
        Action<int> onSessionStart,
        Action<HostBuilderContext, IServiceCollection>? configureServices = null)
    {
        ArgumentNullException.ThrowIfNull(stopWords);
        ArgumentNullException.ThrowIfNull(kickoffPerInputAsync);
        ArgumentNullException.ThrowIfNull(onSessionStart);
        return RunInteractiveLoopCoreAsync();

        async Task<int> RunInteractiveLoopCoreAsync()
        {
            if (!TryBuildHost(opts, loggerCategory, configureServices, out var bootstrap, out var errorCode))
                return errorCode;

            var (host, logger, _, cliMounts) = (bootstrap!.Host, bootstrap.Logger, bootstrap.ConfigPath, bootstrap.CliMounts);

            using var sessionCts = new CancellationTokenSource();
            using var shutdown = RegisterGracefulShutdown(sessionCts, logger);

            RunnerLogging.LogMounts(cliMounts, logger);

            // Shared handle to the in-flight kickoff's CTS, read by the SIGINT handler and
            // written by the per-question runner. A StrongBox lets both the handler closure
            // and the extracted runner method target the same volatile slot.
            var currentKickoff = new StrongBox<CancellationTokenSource?>(null);
            var perQuestionHandler = CreatePerQuestionCancelHandler(currentKickoff);
            Console.CancelKeyPress += perQuestionHandler;

            try
            {
                onSessionStart(0);
                return await RunQuestionLoopAsync(
                    host, sessionCts, currentKickoff, stopWords, kickoffPerInputAsync, logger).ConfigureAwait(false);
            }
            finally
            {
                Console.CancelKeyPress -= perQuestionHandler;
            }
        }
    }

    /// <summary>
    /// Builds the SIGINT handler for the interactive loop: a per-kickoff cancel that cancels only
    /// the in-flight question (via <paramref name="currentKickoff"/>) and leaves the loop running.
    /// SIGINT outside a kickoff is handled by <see cref="RegisterGracefulShutdown"/> instead.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    private static ConsoleCancelEventHandler CreatePerQuestionCancelHandler(
        StrongBox<CancellationTokenSource?> currentKickoff)
    {
        return (_, e) =>
        {
            var cts = Volatile.Read(ref currentKickoff.Value);
            if (cts is null || cts.IsCancellationRequested) return;
            e.Cancel = true;
            cts.Cancel();
            Console.Error.WriteLine(
                "\n[runner] Canceling current question — type a new question or a stop word to exit.");
        };
    }

    /// <summary>
    /// Reads and dispatches interactive questions until end-of-input, a stop word, or session
    /// cancellation. Returns the loop exit code: 0 on stop word / EOF, 130 when the session was
    /// canceled, or a non-null code propagated from a question run.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    private static async Task<int> RunQuestionLoopAsync(
        IHost host,
        CancellationTokenSource sessionCts,
        StrongBox<CancellationTokenSource?> currentKickoff,
        IReadOnlySet<string> stopWords,
        Func<IServiceProvider, string, CancellationToken, Task<CrewOutput>> kickoffPerInputAsync,
        ILogger logger)
    {
        var questionNumber = 0;
        while (!sessionCts.IsCancellationRequested)
        {
            var input = ReadQuestion();
            if (input is null) break;
            if (string.IsNullOrWhiteSpace(input))
            {
                Console.WriteLine("  (empty input — type a question or a stop word to exit)");
                continue;
            }
            if (IsStopWord(stopWords, input))
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Session ended. {questionNumber} question(s) answered.");
                Console.ResetColor();
                return 0;
            }

            questionNumber++;
            LogQuestion(logger, questionNumber, input);

            var earlyExit = await RunScopedQuestionAsync(
                host, input, questionNumber, sessionCts, currentKickoff, kickoffPerInputAsync, logger)
                .ConfigureAwait(false);
            if (earlyExit is { } code)
                return code;
        }

        return sessionCts.IsCancellationRequested ? 130 : 0;
    }

    /// <summary>
    /// Runs a single interactive question inside its own DI scope and linked kickoff CTS,
    /// publishing that CTS to <paramref name="currentKickoff"/> for the duration so the SIGINT
    /// handler can cancel just this question. Returns the same <c>int?</c> contract as
    /// <see cref="RunSingleQuestionAsync"/>: non-null only when the loop must terminate.
    /// </summary>
    private static async Task<int?> RunScopedQuestionAsync(
        IHost host,
        string input,
        int questionNumber,
        CancellationTokenSource sessionCts,
        StrongBox<CancellationTokenSource?> currentKickoff,
        Func<IServiceProvider, string, CancellationToken, Task<CrewOutput>> kickoffPerInputAsync,
        ILogger logger)
    {
        using var scope = host.Services.CreateScope();
        using var kickoffCts = CancellationTokenSource.CreateLinkedTokenSource(sessionCts.Token);
        Volatile.Write(ref currentKickoff.Value, kickoffCts);

        try
        {
            return await RunSingleQuestionAsync(
                scope.ServiceProvider, input, questionNumber,
                sessionCts, kickoffPerInputAsync, logger, kickoffCts.Token).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref currentKickoff.Value, null);
        }
    }

    /// <summary>
    /// Prompts for and reads a single interactive question line, returning the trimmed input
    /// or <c>null</c> when end-of-input is reached (Ctrl+D / closed stdin).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    private static string? ReadQuestion()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("\n[Question] > ");
        Console.ResetColor();
        return Console.ReadLine()?.Trim();
    }

    private static bool IsStopWord(IReadOnlySet<string> stopWords, string input)
    {
#pragma warning disable CA1308 // matched against a caller-provided set whose comparer/casing is unknown here; lowercasing preserves the existing lookup semantics
        return stopWords.Contains(input.ToLowerInvariant());
#pragma warning restore CA1308
    }

    /// <summary>
    /// Runs one kickoff for an interactive question and renders the answer. Returns a non-null
    /// exit code only when the loop must terminate (session canceled → 130); returns <c>null</c>
    /// to continue with the next question (success, per-question cancel, or per-question error).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Per-question fault barrier: any kickoff failure is logged and reported, then the loop continues so one failed question cannot terminate the interactive session. Cancellation is handled by dedicated catches.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    private static async Task<int?> RunSingleQuestionAsync(
        IServiceProvider services,
        string input,
        int questionNumber,
        CancellationTokenSource sessionCts,
        Func<IServiceProvider, string, CancellationToken, Task<CrewOutput>> kickoffPerInputAsync,
        ILogger logger,
        CancellationToken kickoffToken)
    {
        try
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  Processing...");
            Console.ResetColor();

            var output = await kickoffPerInputAsync(services, input, kickoffToken).ConfigureAwait(false);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n[Answer]");
            Console.ResetColor();
            Console.WriteLine(output.FinalOutput);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(output.TokensUsed is { } usage
                ? $"  ({output.Duration.TotalSeconds:F1}s — {usage.TotalTokens} tokens)"
                : $"  ({output.Duration.TotalSeconds:F1}s — tokens not measured)");
            Console.ResetColor();
            return null;
        }
        catch (OperationCanceledException) when (sessionCts.IsCancellationRequested)
        {
            LogSessionCanceled(logger, questionNumber);
            return 130;
        }
        catch (OperationCanceledException)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  Question canceled.");
            Console.ResetColor();
            LogQuestionCanceled(logger, questionNumber);
            return null;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n  Error: {ex.Message}");
            Console.ResetColor();
            LogQuestionFailed(logger, ex, questionNumber);
            return null;
        }
    }

    /// <summary>
    /// Registers SIGTERM + SIGINT handlers that cancel the provided token source. Returns
    /// a disposable that unregisters both handlers when disposed.
    /// </summary>
    public static IDisposable RegisterGracefulShutdown(CancellationTokenSource cts, ILogger logger)
    {
        var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
        {
            Console.Error.WriteLine("[runner] SIGTERM received — canceling crew for graceful shutdown...");
            ctx.Cancel = true;
            cts.Cancel();
        });
        var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx =>
        {
            Console.Error.WriteLine("[runner] SIGINT received — canceling crew for graceful shutdown...");
            ctx.Cancel = true;
            cts.Cancel();
        });
        return new CompositeDisposable(sigterm, sigint);
    }

    /// <summary>
    /// Inspects CLI mount strings and returns the virtual path of the first writable mount
    /// whose virtual root starts with <c>/output</c>, or <c>null</c> if none is found.
    /// </summary>
    public static string? DetectOutputMountPath(IEnumerable<string> cliMounts)
    {
        ArgumentNullException.ThrowIfNull(cliMounts);
        foreach (var raw in cliMounts)
        {
            try
            {
                var mount = FileSystemMount.Parse(raw);
                if (mount.VirtualPath.StartsWith("/output", StringComparison.OrdinalIgnoreCase)
                    && (mount.DefaultRights & FileAccessRights.Write) != 0)
                {
                    return mount.VirtualPath.TrimEnd('/');
                }
            }
            catch (FormatException)
            {
                // Ignore malformed mount strings.
            }
        }
        return null;
    }

    /// <summary>
    /// Applies a verbosity preset shared by all runners.
    /// 1 = warnings only except Orkeon Application/Tools/Infrastructure at Information.
    /// 2 = Debug everywhere, Infrastructure capped at Information.
    /// </summary>
    public static void ConfigureVerboseLogging(ILoggingBuilder b, int verbosity)
    {
        // If an outer host (typically a TUI) advertised an AmbientLoggerProvider, route
        // logs there INSTEAD of the standard Console provider — otherwise SimpleConsole
        // would write to System.Console.Out (which an outer TUI has commandeered) and
        // pollute the alt-screen / leak as a dump on shutdown.
        // Resolved by reflection so this shared helper doesn't take a hard dependency
        // on Orkeon.Cli.Abstractions (avoid coupling examples runners to CLI projects).
        var ambient = TryResolveAmbientLoggerProvider();
        if (ambient is not null)
        {
            b.ClearProviders();
            b.AddProvider(ambient);
        }
        else
        {
            b.AddSimpleConsole(options =>
            {
                options.TimestampFormat = "HH:mm:ss.fff ";
                options.SingleLine = true;
            });
        }

        switch (verbosity)
        {
            case 1:
                b.SetMinimumLevel(LogLevel.Warning);
                b.AddFilter("Orkeon.Application.Crew.ExecutionOrchestrator", LogLevel.Information);
                b.AddFilter("Orkeon.Application.Agent", LogLevel.Information);
                b.AddFilter("Orkeon.Tools", LogLevel.Information);
                b.AddFilter("Orkeon.Infrastructure.Crew", LogLevel.Information);
                b.AddFilter("Orkeon.Infrastructure.Orchestration", LogLevel.Information);
                b.AddFilter("Orkeon.Infrastructure.Logging", LogLevel.Information);
                break;
            default:
                b.SetMinimumLevel(LogLevel.Debug);
                b.AddFilter("Orkeon.Infrastructure", LogLevel.Information);
                break;
        }
    }

    /// <summary>
    /// True when <paramref name="configPath"/> looks like a <c>.ork.ts</c> Orkéon Scripting
    /// DSL source rather than a YAML crew definition. The runner dispatches on this so the
    /// same <c>-c/--config</c> CLI flag accepts both formats.
    /// </summary>
    public static bool IsScriptedCrewDefinition(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        // Accept both ".ork.ts" (the conventional extension shipped in examples) and the
        // shorter ".ork.js" for already-transpiled fixtures used in tests.
        return configPath.EndsWith(".ork.ts", StringComparison.OrdinalIgnoreCase)
            || configPath.EndsWith(".ork.js", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Loads a <c>.ork.ts</c> crew definition through <see cref="ScriptHost"/>, then runs
    /// it through <see cref="JsCrewConfigurationAdapter"/> + <see cref="ICrewFactory"/>
    /// so it joins the same orchestration pipeline as YAML crews (deliverable resolvers,
    /// AutoSummaryWriter, telemetry).
    /// </summary>
    private static async Task<Domain.Crew.Crew> LoadCrewFromScriptAsync(
        IHost host,
        ICrewFactory factory,
        string configPath,
        ILogger logger,
        CancellationToken ct)
    {
        var sp = host.Services;
        var fileSystem = sp.GetRequiredService<IFileSystemService>();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var configuration = sp.GetRequiredService<IConfiguration>();
        var tools = sp.GetServices<IBaseTool>().ToList();
        var llmProvider = sp.GetService<ILlmProvider>();

        var engineFactory = new JsEngineFactory(
            loggerFactory: loggerFactory,
            configuration: configuration,
            builtInTools: tools,
            llmProvider: llmProvider,
            permissionGate: sp.GetService<Orkeon.Application.Interfaces.Security.IPermissionGate>(),
            deltaSink: sp.GetService<Orkeon.Application.Interfaces.Ports.ILlmDeltaSink>());

        // Always bundle through esbuild so relative imports + TS-only syntax in the
        // script resolve consistently across .ork.ts authors. EsbuildNotFoundException
        // surfaces upstream and is caught by RunOneShotAsync's exception handler with
        // the standard exit code 2 (crew failure); operators can pre-install the
        // toolchain via the MSBuild bootstrap target on src/scripting/Orkeon.Scripting/.
        using var transpiler = new EsbuildTranspiler();
        var scriptHost = new ScriptHost(
            fileSystem,
            transpiler,
            engineFactory,
            loggerFactory.CreateLogger<ScriptHost>());

        // configDir is already mounted 1:1 in TryBuildHost so virtualPath == physical path
        // resolves through the VFS without any prefix gymnastics.
        var physicalPath = configPath;
        var virtualPath = configPath;

        LogScriptedCrewDetected(logger);
        var jsCrew = await scriptHost.LoadCrewFromFileAsync(physicalPath, virtualPath, ct).ConfigureAwait(false);
        var crewConfig = JsCrewConfigurationAdapter.ToConfiguration(jsCrew);
        LogAdaptedJsCrew(logger, jsCrew.name, crewConfig.Agents.Count, crewConfig.Tasks.Count);
        return await factory.CreateFromConfigAsync(crewConfig, ct).ConfigureAwait(false);
    }

    private static ILoggerProvider? TryResolveAmbientLoggerProvider()
    {
        // Late-bound lookup of Orkeon.Cli.Abstractions.Logging.AmbientLoggerProvider.Current.
        // Returns null if the assembly isn't loaded (no TUI active) or the property is null.
        var t = Type.GetType("Orkeon.Cli.Abstractions.Logging.AmbientLoggerProvider, Orkeon.Cli.Abstractions");
        var prop = t?.GetProperty("Current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        return prop?.GetValue(null) as ILoggerProvider;
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "LLM exchange logging enabled → {LogDir}/llm-exchanges-*.jsonl")]
    private static partial void LogLlmExchangeLoggingEnabled(ILogger logger, string logDir);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Loading crew from {ConfigPath}...")]
    private static partial void LogLoadingCrew(ILogger logger, string configPath);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Kicking off crew '{CrewGoal}'...")]
    private static partial void LogKickingOffCrew(ILogger logger, string crewGoal);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Crew execution was canceled (SIGTERM/SIGINT); hook should have written AUTO_SUMMARY.md")]
    private static partial void LogCrewExecutionCanceled(ILogger logger);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Crew execution failed")]
    private static partial void LogCrewExecutionFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Question #{Number}: {Question}")]
    private static partial void LogQuestion(ILogger logger, int number, string question);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "Session canceled (SIGTERM/SIGINT) during question #{Number}")]
    private static partial void LogSessionCanceled(ILogger logger, int number);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Question #{Number} was canceled")]
    private static partial void LogQuestionCanceled(ILogger logger, int number);

    [LoggerMessage(EventId = 9, Level = LogLevel.Error, Message = "Failed to process question #{Number}")]
    private static partial void LogQuestionFailed(ILogger logger, Exception ex, int number);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Crew definition detected as Orkéon Scripting DSL — loading via ScriptHost.")]
    private static partial void LogScriptedCrewDetected(ILogger logger);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "Adapted JsCrew '{CrewName}' to CrewConfiguration ({AgentCount} agent(s), {TaskCount} task(s)).")]
    private static partial void LogAdaptedJsCrew(ILogger logger, string crewName, int agentCount, int taskCount);

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IDisposable[] _disposables;
        public CompositeDisposable(params IDisposable[] disposables) => _disposables = disposables;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Best-effort cleanup: a failed Dispose must not mask sibling disposals or the primary result.")]
        public void Dispose()
        {
            foreach (var d in _disposables)
            {
                try { d.Dispose(); } catch { /* best-effort */ }
            }
        }
    }
}
