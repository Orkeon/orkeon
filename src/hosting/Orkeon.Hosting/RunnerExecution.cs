using Orkeon.Constants.FileSystem;
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
    /// <param name="Host">The built host.</param>
    /// <param name="Logger">The runner-level logger.</param>
    /// <param name="ConfigPath">
    /// The crew target as the operator typed it, resolved to an absolute physical path.
    /// Diagnostics and esbuild's import resolution only — never handed to the VFS.
    /// </param>
    /// <param name="VirtualConfigPath">
    /// The same target spelled for the VFS: <c>/crew</c> for a multi-file crew directory,
    /// <c>/crew/&lt;file&gt;</c> otherwise. This is what the loader is given.
    /// </param>
    /// <param name="IsCrewDirectory">
    /// Whether the target is a multi-file crew directory. Decided once, by
    /// <c>CrewDirectoryLayout.Inspect</c>, and carried rather than re-derived: asking the VFS
    /// again cannot see through a symlinked crew directory.
    /// </param>
    /// <param name="CliMounts">The mount strings the host was built with.</param>
    internal sealed record HostBootstrap(
        IHost Host,
        ILogger Logger,
        string ConfigPath,
        string VirtualConfigPath,
        bool IsCrewDirectory,
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

        if (!TryResolveCrewTarget(opts, out var target))
        {
            errorCode = 1;
            return false;
        }

        var cliMounts = opts.Mounts.ToList();
        var llmLogPath = opts.ResolvedLlmLogPath;

        if (!EnsureExternalMountsAllowed(opts, target.IsScript ? null : target.ConfigDir, llmLogPath)
            || !EnsureReservedRootsAreFree(
                cliMounts, target.SettingsPath,
                target.VirtualRoot, RunnerVirtualRoots.LlmLogs, RunnerVirtualRoots.Sandbox)
            || !EnsureMountSourcesExist(cliMounts, target.SettingsPath))
        {
            errorCode = 1;
            return false;
        }
        cliMounts.Insert(0, $"{FileSystemMount.Quote(target.ConfigDir)}:{target.VirtualRoot}:ro");

        var internalMounts = CreateLlmLogMounts(llmLogPath);
        var verbosity = Math.Clamp(opts.Verbose, 0, 2);
        var outputMountPath = DetectOutputMountPath(cliMounts);

        var host = RunnerHost.Build(
            target.SettingsPath,
            new RunnerMountPlan
            {
                CliMounts = cliMounts,
                InternalMounts = internalMounts,
                AllowExternalMounts = opts.EffectiveAllowExternalMounts,
                LlmLogVirtualPath = llmLogPath != null ? RunnerVirtualRoots.LlmLogs : null,
            },
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

        bootstrap = new HostBootstrap(
            host, logger, target.ConfigPath, target.VirtualConfigPath, target.IsCrewDirectory, cliMounts);
        return true;
    }

    /// <summary>
    /// The crew target, decided once: the physical path the operator named, the directory that
    /// anchors settings and the config mount, the virtual root and spelling the loader is
    /// given, and the settings file that goes with them.
    /// </summary>
    /// <param name="ConfigPath">The target resolved to an absolute physical path.</param>
    /// <param name="ConfigDir">Directory mounted read-only under <paramref name="VirtualRoot"/>.</param>
    /// <param name="VirtualRoot"><c>/script</c> for a scripting entry point, <c>/crew</c> otherwise.</param>
    /// <param name="VirtualConfigPath">The target spelled for the VFS.</param>
    /// <param name="IsCrewDirectory">Whether the target is a multi-file crew directory.</param>
    /// <param name="IsScript">Whether the target is a scripting entry point.</param>
    /// <param name="SettingsPath">Resolved appsettings.json, or <see langword="null"/>.</param>
    private sealed record CrewTarget(
        string ConfigPath,
        string ConfigDir,
        string VirtualRoot,
        string VirtualConfigPath,
        bool IsCrewDirectory,
        bool IsScript,
        string? SettingsPath);

    /// <summary>
    /// Resolves what the operator pointed <c>--config</c> at: presence, single file versus
    /// multi-file crew directory, the anchor directory, the settings file, and the virtual
    /// spelling the loader is handed. Prints its own diagnostic and returns
    /// <see langword="false"/> on invalid input.
    /// </summary>
    private static bool TryResolveCrewTarget(
        RunnerOptionsBase opts,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CrewTarget? target)
    {
        target = null;

        if (string.IsNullOrWhiteSpace(opts.ConfigPath))
        {
            // --config is optional at the parser level so --list-tools can run without it,
            // but every crew-loading mode still needs it — enforce presence here.
            Console.Error.WriteLine("ERROR: --config is required (path to the crew .yaml or .ork.ts).");
            return false;
        }

        var configPath = Path.GetFullPath(opts.ConfigPath);

        // A crew target is either a single file (.yaml / .ork.ts) or a directory holding a
        // multi-file crew (config.yaml + agents/ + tasks/, or the flat legacy triplet). The
        // directory form is classified here so an ambiguous or empty directory reports its own
        // diagnostic instead of the generic "config file not found".
        var inspection = CrewDirectoryLayout.Inspect(configPath);
        if (inspection.Error is not null)
        {
            Console.Error.WriteLine($"ERROR: {inspection.Error}");
            return false;
        }

        if (!inspection.IsCrewDirectory && !File.Exists(configPath))
        {
            Console.Error.WriteLine($"ERROR: config file not found: {configPath}");
            return false;
        }

        // For a crew directory the config dir IS the target: mounting it (rather than its
        // parent) keeps the VFS surface as narrow as it is for a single-file crew, and anchors
        // appsettings resolution inside the crew.
        if (inspection.IsCrewDirectory)
            configPath = Path.TrimEndingDirectorySeparator(configPath);
        var configDir = inspection.IsCrewDirectory ? configPath : Path.GetDirectoryName(configPath)!;
        var settingsPath = RunnerSettings.ResolveSettingsPath(opts.SettingsPath, configDir);
        if (settingsPath != null)
            Console.Error.WriteLine($"Using settings: {settingsPath}");

        // The crew-config loader (YamlCrewDefinitionLoader) reads the YAML through
        // IFileSystemService, so configDir must be visible to the VFS registry — under a
        // NAME (ADR-008), never identity-mapped. An agent asking `list_mounts`, or reading
        // an access-denied message, must never be handed an absolute disk path.
        // A scripting entry point gets /script, a YAML crew gets /crew — the two roots exist
        // because a script's relative imports resolve against its own directory. And a
        // script's directory is the CLI's primary input rather than a user-declared mount, so
        // it is not gated behind --allow-external-mounts, exactly as `orkeon run` argues on
        // its own script path. Both rules used to live only there: `--validate` on a .ork.ts
        // came through here instead, took a /crew topology the real run never uses, and was
        // refused for a script outside the working directory that runs perfectly well. A
        // validation that answers about a different arrangement than the run is worse than no
        // validation.
        var isScript = !inspection.IsCrewDirectory && IsScriptedCrewDefinition(configPath);
        var virtualRoot = isScript ? RunnerVirtualRoots.Script : RunnerVirtualRoots.Crew;
        var virtualConfigPath = inspection.IsCrewDirectory
            ? virtualRoot
            : $"{virtualRoot}/{Path.GetFileName(configPath)}";

        target = new CrewTarget(
            configPath, configDir, virtualRoot, virtualConfigPath,
            inspection.IsCrewDirectory, isScript, settingsPath);
        return true;
    }

    /// <summary>
    /// The internal mount carrying the LLM exchange log, or nothing when logging is off. The
    /// log is infrastructure: the VFS must reach it (AppendAllTextAsync), no agent has any
    /// business addressing it — hence the internal-mount list rather than <c>--mount</c>.
    /// </summary>
    private static IReadOnlyList<string> CreateLlmLogMounts(string? llmLogPath)
    {
        if (llmLogPath is null)
            return [];

        // Mount base paths must exist before FileSystemRegistry is built
        // (FileSystemServiceRegistration throws DirectoryNotFoundException otherwise).
        Directory.CreateDirectory(llmLogPath);
        return [$"{FileSystemMount.Quote(llmLogPath)}:{RunnerVirtualRoots.LlmLogs}:rw"];
    }

    /// <summary>
    /// Validates the external-mounts guard: paths outside the current working directory
    /// require an explicit opt-in (<c>--allow-external-mounts</c>). Conservative by design —
    /// avoids silently widening the VFS surface for users who expect workspace-relative
    /// execution. Returns <see langword="false"/> (after printing the diagnostic) when blocked.
    /// <para>
    /// "Outside" is asked of <see cref="PhysicalPathContainment"/>, the same predicate
    /// <c>PathValidator</c> enforces the rule with. A bare <c>StartsWith</c> answered a
    /// different question here and the two answers only ever differed in the direction that
    /// hurts: a crew in a sibling directory whose name extends the cwd's
    /// (<c>~/proj</c> vs <c>~/proj-old</c>) read as <i>inside</i>, so the opt-in was never
    /// demanded, its base path never whitelisted, and the boundary-safe validator then
    /// refused every file the crew touched — with no mention of the flag that would have
    /// fixed it.
    /// </para>
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    private static bool EnsureExternalMountsAllowed(RunnerOptionsBase opts, string? configDir, string? llmLogPath)
    {
        var cwd = Directory.GetCurrentDirectory();
        var configOutsideCwd = configDir is not null && !PhysicalPathContainment.IsUnder(configDir, cwd);
        var llmLogOutsideCwd = llmLogPath != null && !PhysicalPathContainment.IsUnder(llmLogPath, cwd);
        if (!(configOutsideCwd || llmLogOutsideCwd) || opts.EffectiveAllowExternalMounts)
            return true;

        Console.Error.WriteLine(
            "ERROR: --allow-external-mounts is required when reading the crew config "
            + "or writing LLM logs outside the current working directory. Add "
            + "--allow-external-mounts (or set ORKEON_ALLOW_EXTERNAL_MOUNTS=1) to proceed.");
        if (configOutsideCwd)
            Console.Error.WriteLine($"       configDir   : {configDir}");
        if (llmLogOutsideCwd)
            Console.Error.WriteLine($"       llmLogPath  : {llmLogPath}");
        Console.Error.WriteLine($"       cwd         : {cwd}");
        return false;
    }

    /// <summary>
    /// Refuses a mount that claims a virtual root the runner needs for itself, whether it was
    /// written as a <c>--mount</c> argument or declared in the settings file. Without this the
    /// collision surfaces as a raw <see cref="InvalidOperationException"/> ("Duplicate virtual
    /// paths") thrown out of a DI factory, which reads as a crash rather than as the
    /// configuration mistake it is.
    /// <para>
    /// The settings file is read HERE, from <paramref name="settingsPath"/>, rather than
    /// concatenated by each caller. It was a caller's job briefly, and three of the six
    /// mount-building entry points did not do it - including <c>orkeon-host</c>, the most
    /// settings-driven of them all. A guard whose completeness depends on every future caller
    /// remembering an argument is a guard that will be incomplete again; this signature cannot
    /// be called wrongly.
    /// </para>
    /// </summary>
    /// <param name="userMounts">The user-supplied mount strings, typically <c>--mount</c>.</param>
    /// <param name="settingsPath">Resolved settings file whose declared mounts also count, or
    /// <see langword="null"/> when the command resolves none.</param>
    /// <param name="reserved">The virtual roots this runner keeps for itself.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    public static bool EnsureReservedRootsAreFree(
        IEnumerable<string> userMounts,
        string? settingsPath,
        params string[] reserved)
    {
        ArgumentNullException.ThrowIfNull(userMounts);
        ArgumentNullException.ThrowIfNull(reserved);

        foreach (var mountString in userMounts.Concat(RunnerSettings.ReadDeclaredMounts(settingsPath)))
        {
            string virtualPath;
            try
            {
                virtualPath = FileSystemMount.Parse(mountString).VirtualPath;
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                // Malformed strings — including a blank one, which Parse rejects with an
                // ArgumentException rather than a FormatException — are reported by the mount
                // parser at host build time, with its own precise message. Not this guard's.
                continue;
            }

            var clash = reserved.FirstOrDefault(r =>
                string.Equals(virtualPath.TrimEnd('/'), r, StringComparison.Ordinal));
            if (clash is null)
                continue;

            // The roots differ per entry point — the YAML runner reserves /crew, the forge
            // /workspace, /forge and /output — so the line names what THIS command reserves
            // rather than a fixed list that was false for whoever was not the YAML runner.
            Console.Error.WriteLine(
                $"ERROR: '{clash}' is a virtual root reserved by the runner: this command mounts "
                + $"it for itself (reserved here: {string.Join(", ", reserved)}). Give this mount "
                + "another virtual name.");
            Console.Error.WriteLine($"       mount       : {mountString}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Refuses a mount whose host-side base path does not exist, whether it was written as a
    /// <c>--mount</c> argument or declared in the settings file. Without this the missing
    /// directory surfaces as a raw <see cref="DirectoryNotFoundException"/> thrown out of the
    /// <c>FileSystemRegistry</c> DI factory — a stack trace, where the operator made a simple
    /// configuration mistake that one actionable line (and a <c>mkdir</c>) fixes.
    /// <para>
    /// Same completeness stance as <see cref="EnsureReservedRootsAreFree"/>: the settings file
    /// is read HERE from <paramref name="settingsPath"/>, so no caller can forget it. Probing
    /// the physical path is bootstrap work by definition — the check exists precisely because
    /// the VFS registry (and thus <c>IFileSystemService</c>) cannot be built over it yet.
    /// </para>
    /// </summary>
    /// <param name="userMounts">The user-supplied mount strings, typically <c>--mount</c>.</param>
    /// <param name="settingsPath">Resolved settings file whose declared mounts also count, or
    /// <see langword="null"/> when the command resolves none.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    public static bool EnsureMountSourcesExist(
        IEnumerable<string> userMounts,
        string? settingsPath)
    {
        ArgumentNullException.ThrowIfNull(userMounts);

        foreach (var mountString in userMounts.Concat(RunnerSettings.ReadDeclaredMounts(settingsPath)))
        {
            FileSystemMount mount;
            try
            {
                mount = FileSystemMount.Parse(mountString);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                // Malformed strings are reported by the mount parser at host build time,
                // with its own precise message. Not this guard's.
                continue;
            }

            if (Directory.Exists(mount.BasePath))
                continue;

            Console.Error.WriteLine(
                $"ERROR: mount source directory does not exist: {mount.BasePath}. "
                + "Create it first (mkdir -p) or point the mount at an existing directory.");
            Console.Error.WriteLine($"       mount       : {mountString}");
            return false;
        }

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
        // Diagnostic modes short-circuit the kickoff: --list-tools dumps the runtime tool
        // registry (no crew), --validate loads the crew strictly but never probes the LLM.
        if (opts.ListTools)
            return await RunListToolsAsync(opts, loggerCategory, configureServices).ConfigureAwait(false);
        if (opts.Validate)
            return await RunValidateAsync(opts, loggerCategory, configureServices, externalCt).ConfigureAwait(false);

        if (!TryBuildHost(opts, loggerCategory, configureServices, out var bootstrap, out var errorCode))
            return errorCode;

        // `using`, because the sandbox session directory is deleted by SandboxSession.Dispose,
        // which the container runs on host disposal. Without it every `orkeon run` left a
        // directory under the ephemeral root for a later process's janitor to collect — and
        // the diagnostics flows next door already dispose theirs.
        using var host = bootstrap!.Host;
        var logger = bootstrap.Logger;

        // Internal CTS linked to the external one (if any). Cancelling either path stops
        // the crew: SIGINT/SIGTERM via RegisterGracefulShutdown, OR caller's externalCt.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        using var shutdown = RegisterGracefulShutdown(cts, logger);

        try
        {
            return await KickoffLoadedCrewAsync(host, bootstrap, opts, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LogCrewExecutionCanceled(logger);
            return 130;
        }
        catch (Exception ex) when (IsConnectionRefused(ex))
        {
            // Safety net for the case the pre-kickoff probe let through (e.g. the endpoint died
            // between the probe and kickoff): the raw failure is a "Connection refused"
            // SocketException buried under HTTP retries — surface the actionable one-liner
            // instead of the cryptic stack trace.
            var endpoint = ResolveConfiguredLlmEndpoint(host);
            await Console.Error.WriteLineAsync(BuildUnreachableLlmMessage(endpoint)).ConfigureAwait(false);
            LogLlmEndpointUnreachable(logger, endpoint ?? "(default)");
            return 2;
        }
        catch (Exception ex)
        {
            LogCrewExecutionFailed(logger, ex);
            return 2;
        }
        }
    }

    /// <summary>
    /// The kickoff sequence itself: log the mounts, load the crew, parse the CLI input, probe
    /// the LLM endpoint, run the crew and print its output. Split out of
    /// <see cref="RunOneShotAsync"/> so that method reads as the exit-code fault barrier it is
    /// and this one as the ordered sequence it runs; every failure it does not answer for
    /// itself propagates to that barrier.
    /// </summary>
    /// <returns>Exit code: 0 = success, 1 = malformed <c>--var</c>, 2 = crew load or LLM failure.</returns>
    private static async Task<int> KickoffLoadedCrewAsync(
        IHost host,
        HostBootstrap bootstrap,
        RunnerOptionsBase opts,
        CancellationToken ct)
    {
        var logger = bootstrap.Logger;
        RunnerLogging.LogMounts(bootstrap.CliMounts, logger);

        LogLoadingCrew(logger, bootstrap.ConfigPath);

        var factory = host.Services.GetRequiredService<ICrewFactory>();

        Domain.Crew.Crew crew;
        try
        {
            crew = await LoadCrewAsync(
                host, factory, bootstrap.VirtualConfigPath, logger, ct, bootstrap.IsCrewDirectory).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !IsConnectionRefused(ex))
        {
            // A crew that does not load is a configuration mistake — report it the way
            // --validate does (one actionable line, stack behind -v / ORKEON_DEBUG=1)
            // rather than letting the generic fault barrier above dump the raw exception.
            // A scripted crew can reach the LLM while loading, though: that failure is not
            // a configuration mistake, so it is left to the unreachable-endpoint handler.
            await Console.Error.WriteLineAsync($"ERROR: {ex.Message}").ConfigureAwait(false);
            await ReportCrewConfigurationErrorAsync(logger, ex, opts.Verbose).ConfigureAwait(false);
            return 2;
        }

        var orchestrator = host.Services.GetRequiredService<ICrewOrchestrationService>();

        var input = await TryParseCrewInputAsync(opts).ConfigureAwait(false);
        if (input is null)
            return 1;

        if (!await EnsureLlmEndpointReachableAsync(host, logger, ct).ConfigureAwait(false))
            return 2;

        LogKickingOffCrew(logger, crew.Goal);
        var output = await orchestrator.KickoffAsync(crew.Id, input, ct).ConfigureAwait(false);

        PrintCrewOutput(output, opts.MachineReadableStdout ? Console.Error : Console.Out);
        return 0;
    }

    /// <summary>
    /// Builds the <see cref="CrewInput"/> for a one-shot kickoff from the parsed CLI variables
    /// and initial context. Returns <see langword="null"/> (after printing the diagnostic) when
    /// a <c>--var</c> value is malformed.
    /// </summary>
    private static async Task<CrewInput?> TryParseCrewInputAsync(RunnerOptionsBase opts)
    {
        try
        {
            var vars = opts.ParseVariables();
            return vars.Count > 0 || !string.IsNullOrEmpty(opts.InitialContext)
                ? CrewInput.WithStringVariables(opts.InitialContext, vars)
                : CrewInput.Empty();
        }
        catch (FormatException ex)
        {
            await Console.Error.WriteLineAsync($"ERROR: {ex.Message}").ConfigureAwait(false);
            return null;
        }
    }

    /// <summary>
    /// Pre-kickoff reachability probe. LLM providers wrap connection failures in sanitized
    /// exceptions behind Polly retries, so a dead endpoint otherwise yields an empty crew
    /// output at exit 0 with no hint — the connection-refused catch in the caller never sees it.
    /// A short TCP probe turns "nothing is listening" into a fast, explicit failure. Only an
    /// active refusal returns <see langword="false"/>; ambiguous results (no URL, DNS, TLS,
    /// slow-link timeout) pass so a legitimate run is never blocked by the probe itself.
    /// </summary>
    private static async Task<bool> EnsureLlmEndpointReachableAsync(
        IHost host, ILogger logger, CancellationToken ct)
    {
        var probeEndpoint = ResolveConfiguredLlmEndpoint(host);
        if (probeEndpoint is null
            || await IsLlmEndpointReachableAsync(probeEndpoint, TimeSpan.FromSeconds(2), ct)
                .ConfigureAwait(false))
        {
            return true;
        }

        await Console.Error.WriteLineAsync(BuildUnreachableLlmMessage(probeEndpoint)).ConfigureAwait(false);
        LogLlmEndpointUnreachable(logger, probeEndpoint);
        return false;
    }

    /// <summary>
    /// Renders the one-shot crew result (final output, duration, token usage) to the console.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    private static void PrintCrewOutput(CrewOutput output, TextWriter destination)
    {
        // On an --events run the destination is stderr: stdout is the protocol, and this
        // banner between two JSONL documents was the one non-envelope thing it still carried.
        destination.WriteLine();
        destination.WriteLine("=== Crew Output ===");
        destination.WriteLine(output.FinalOutput);
        destination.WriteLine();
        destination.WriteLine($"Duration: {output.Duration}");
        destination.WriteLine(output.TokensUsed is { } usage
            ? $"Tokens used: {usage.TotalTokens}"
            : "Tokens used: (not measured)");
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="ex"/> or any inner exception is a
    /// "connection refused" socket failure — the signature of an LLM endpoint that is configured
    /// but not listening (e.g. Docker Model Runner is not running).
    /// </summary>
    private static bool IsConnectionRefused(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is System.Net.Sockets.SocketException
                { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused })
                return true;
        }
        return false;
    }

    /// <summary>
    /// Builds the actionable one-line diagnostic shown when the configured LLM endpoint cannot be
    /// reached, pointing at the example profiles, <c>--settings</c>, and the getting-started doc.
    /// </summary>
    private static string BuildUnreachableLlmMessage(string? endpoint)
    {
        var target = endpoint is null ? "the configured LLM endpoint" : $"endpoint {endpoint}";
        return $"ERROR: No reachable LLM endpoint ({target} refused the connection). " +
            "Copy an example profile (examples/appsettings/*.example) and pass it with " +
            "--settings, or start Docker Model Runner. " +
            "See docs/getting-started/run-your-first-example.md.";
    }

    /// <summary>
    /// Probes whether the configured LLM endpoint accepts a TCP connection. Returns
    /// <see langword="false"/> ONLY when the host actively refuses the connection (a fast, reliable
    /// "nothing is listening" signal). Every ambiguous outcome — no/blank URL, unparseable URL, DNS
    /// failure, TLS/other socket error, or a probe timeout on a slow link — returns
    /// <see langword="true"/> so a legitimate run is never blocked by the probe itself.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Probe is intentionally best-effort: any non-refusal outcome (timeout, DNS, TLS, unexpected) is treated as inconclusive and must never block a legitimate run.")]
    internal static async System.Threading.Tasks.Task<bool> IsLlmEndpointReachableAsync(
        string? baseUrl, TimeSpan timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return true;

        var defaultPort = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? 443
            : 80;
        var port = uri.Port > 0 ? uri.Port : defaultPort;

        try
        {
            using var probe = new System.Net.Sockets.TcpClient();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(timeout);
            await probe.ConnectAsync(uri.Host, port, linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            // Only an active refusal is conclusive; other socket errors are inconclusive.
            return ex.SocketErrorCode != System.Net.Sockets.SocketError.ConnectionRefused;
        }
        catch (Exception)
        {
            // Timeout (OperationCanceledException), DNS, etc. — inconclusive; do not block.
            return true;
        }
    }

    /// <summary>
    /// Reads the configured LLM base URL (<c>Llm:BaseUrl</c>) for use in diagnostics, or
    /// <see langword="null"/> when no configuration is available.
    /// </summary>
    private static string? ResolveConfiguredLlmEndpoint(IHost host)
    {
        var url = host.Services.GetService<IConfiguration>()?["Llm:BaseUrl"];
        return string.IsNullOrWhiteSpace(url) ? null : url;
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
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(stopWords);
        ArgumentNullException.ThrowIfNull(kickoffPerInputAsync);
        ArgumentNullException.ThrowIfNull(onSessionStart);
        return RunInteractiveLoopCoreAsync();

        async Task<int> RunInteractiveLoopCoreAsync()
        {
            // Diagnostic modes are shared with the one-shot flow so every runner honours them.
            if (opts.ListTools)
                return await RunListToolsAsync(opts, loggerCategory, configureServices).ConfigureAwait(false);
            if (opts.Validate)
                return await RunValidateAsync(opts, loggerCategory, configureServices, CancellationToken.None).ConfigureAwait(false);

            if (!TryBuildHost(opts, loggerCategory, configureServices, out var bootstrap, out var errorCode))
                return errorCode;

            using var host = bootstrap!.Host;
            var (logger, cliMounts) = (bootstrap.Logger, bootstrap.CliMounts);

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
    /// True when <paramref name="configPath"/> looks like a <c>.ork.ts</c> Orkeon Scripting
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

        // Bind the configured limits rather than letting the all-optional overload apply
        // the 30 s untrusted-script default: omitting this discarded
        // Orkeon:Scripting:Limits entirely, so a .ork.ts crew was silently capped at 30 s
        // of WALL CLOCK — LLM latency included — no matter what the operator configured.
        var scriptLimits = configuration.GetSection(Orkeon.Scripting.Configuration.ScriptingLimitsOptions.SectionName)
            .Get<Orkeon.Scripting.Configuration.ScriptingLimitsOptions>();

        var engineFactory = new JsEngineFactory(
            limits: scriptLimits,
            loggerFactory: loggerFactory,
            configuration: configuration,
            builtInTools: tools,
            llmProvider: llmProvider,
            hostPorts: new ScriptingHostPorts
            {
                PermissionGate = sp.GetService<Orkeon.Application.Interfaces.Security.IPermissionGate>(),
                DeltaSink = sp.GetService<Orkeon.Application.Interfaces.Ports.ILlmDeltaSink>(),
                UsageSink = sp.GetService<Orkeon.Application.Interfaces.Ports.ILlmUsageSink>(),
            });

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

        // The script is addressed virtually like everything else; esbuild is the one consumer
        // that genuinely needs a disk path (it resolves the script's relative imports itself,
        // outside the VFS). Asking the VFS to resolve it is the sanctioned way to obtain one —
        // same stance as SqliteStateStore's Data Source. A denial leaves it null, and
        // ScriptHost documents that as "imports will not resolve".
        var virtualPath = configPath;
        var resolved = fileSystem.ResolveAndValidate(virtualPath, FileAccessRights.Read);
        var physicalPath = resolved.IsAllowed ? resolved.ResolvedPath : null;

        LogScriptedCrewDetected(logger);
        var jsCrew = await scriptHost.LoadCrewFromFileAsync(physicalPath, virtualPath, ct).ConfigureAwait(false);
        var crewConfig = JsCrewConfigurationAdapter.ToConfiguration(jsCrew);

        // Script-defined tools become first-class (EX-01): register the instances with
        // the runtime registry BEFORE the factory's strict resolution runs — the same
        // late-registration path MCP tools use. Shadowing an already-registered name is
        // refused loudly: silently replacing a built-in is exactly the kind of surprise
        // strict resolution exists to prevent.
        var scriptTools = JsCrewConfigurationAdapter.CollectScriptTools(jsCrew);
        if (scriptTools.Count > 0)
        {
            var registry = sp.GetRequiredService<Orkeon.Domain.Tools.IToolRegistry>();
            foreach (var tool in scriptTools)
            {
                var existing = await registry.GetToolByNameAsync(tool.Name).ConfigureAwait(false);
                if (existing is not null && !ReferenceEquals(existing, tool))
                {
                    throw new InvalidOperationException(
                        $"Script tool '{tool.Name}' collides with an already-registered tool of the same name. "
                        + "Rename the script tool — shadowing a registered tool is refused.");
                }
                _ = await registry.RegisterToolAsync(tool).ConfigureAwait(false);
            }
            LogRegisteredScriptTools(logger, scriptTools.Count, jsCrew.name);
        }

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

    [LoggerMessage(EventId = 12, Level = LogLevel.Error, Message = "LLM endpoint unreachable (connection refused): {Endpoint}")]
    private static partial void LogLlmEndpointUnreachable(ILogger logger, string endpoint);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Question #{Number}: {Question}")]
    private static partial void LogQuestion(ILogger logger, int number, string question);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "Session canceled (SIGTERM/SIGINT) during question #{Number}")]
    private static partial void LogSessionCanceled(ILogger logger, int number);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Question #{Number} was canceled")]
    private static partial void LogQuestionCanceled(ILogger logger, int number);

    [LoggerMessage(EventId = 9, Level = LogLevel.Error, Message = "Failed to process question #{Number}")]
    private static partial void LogQuestionFailed(ILogger logger, Exception ex, int number);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information, Message = "Crew definition detected as Orkeon Scripting DSL — loading via ScriptHost.")]
    private static partial void LogScriptedCrewDetected(ILogger logger);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "Adapted JsCrew '{CrewName}' to CrewConfiguration ({AgentCount} agent(s), {TaskCount} task(s)).")]
    private static partial void LogAdaptedJsCrew(ILogger logger, string crewName, int agentCount, int taskCount);

    [LoggerMessage(EventId = 12, Level = LogLevel.Information, Message = "Registered {Count} script-defined tool(s) from crew '{CrewName}' with the runtime tool registry.")]
    private static partial void LogRegisteredScriptTools(ILogger logger, int count, string crewName);

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
