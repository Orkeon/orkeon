using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.DependencyInjection;

namespace Orkeon.Hosting;

/// <summary>
/// Diagnostic (non-executing) runner modes shared by all runners: <c>--validate</c>
/// (dry-run crew load, no LLM probe / no kickoff) and <c>--list-tools</c> (runtime tool
/// registry dump). Both honour the runner's <c>configureServices</c> hook so runner-specific
/// tools (e.g. <c>semantic_search</c>, trading tools) appear exactly as at runtime.
/// The class-level VFS-compliance suppression lives on the primary partial
/// (RunnerExecution.cs) and covers this file too.
/// </summary>
public static partial class RunnerExecution
{
    /// <summary>
    /// Dry-run validation of a crew definition: builds the host, resolves settings, loads the
    /// crew with strict tool resolution, and reports the agent/task/tool counts — WITHOUT
    /// probing the LLM endpoint or running any kickoff. Prints
    /// <c>VALIDATION OK: &lt;config&gt; (agents=N, tasks=M, tools resolved=K)</c> to stdout on
    /// success (exit 0); on any load failure prints <c>VALIDATION FAILED: &lt;config&gt;</c> plus
    /// the error to stderr and returns exit 1.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Validation fault barrier: any crew-load failure (unknown tool, malformed YAML, adapter error) is reported as a validation failure with exit code 1 rather than crashing the runner.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    public static Task<int> RunValidateAsync(
        RunnerOptionsBase opts,
        string loggerCategory,
        Action<HostBuilderContext, IServiceCollection>? configureServices = null,
        CancellationToken externalCt = default)
    {
        ArgumentNullException.ThrowIfNull(opts);
        return RunValidateCoreAsync();

        async Task<int> RunValidateCoreAsync()
        {
            // Validation must never touch the RAG index: loading the crew skips the
            // ingestion of rag:-declared collections (CrewFactoryOptions).
            void ConfigureValidationServices(HostBuilderContext context, IServiceCollection services)
            {
                configureServices?.Invoke(context, services);
                services.Configure<Orkeon.Infrastructure.Configuration.CrewFactoryOptions>(
                    o => o.PrepareRagCollections = false);
            }

            if (!TryBuildHost(opts, loggerCategory, ConfigureValidationServices, out var bootstrap, out var errorCode))
                return errorCode;

            using var host = bootstrap!.Host;
            var (logger, configPath, cliMounts) = (bootstrap.Logger, bootstrap.ConfigPath, bootstrap.CliMounts);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            using var shutdown = RegisterGracefulShutdown(cts, logger);

            try
            {
                RunnerLogging.LogMounts(cliMounts, logger);
                LogValidatingCrew(logger, configPath);

                var factory = host.Services.GetRequiredService<ICrewFactory>();
                var crew = await LoadCrewAsync(host, factory, configPath, logger, cts.Token).ConfigureAwait(false);

                // Tools live on the agents, not the crew aggregate (which holds only ids).
                // Re-hydrate the agents and count the distinct tool names actually resolved.
                var agentRepository = host.Services.GetRequiredService<Domain.Agent.IAgentRepository>();
                var agents = await agentRepository.GetByIdsAsync(crew.Agents, cts.Token).ConfigureAwait(false);
                var toolsResolved = agents
                    .SelectMany(a => a.Tools)
                    .Select(t => t.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

                Console.WriteLine(
                    $"VALIDATION OK: {configPath} (agents={crew.Agents.Count}, tasks={crew.Tasks.Count}, tools resolved={toolsResolved})");
                return 0;
            }
            catch (OperationCanceledException)
            {
                LogCrewExecutionCanceled(logger);
                return 130;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"VALIDATION FAILED: {configPath}").ConfigureAwait(false);
                await Console.Error.WriteLineAsync(ex.Message).ConfigureAwait(false);
                await ReportCrewConfigurationErrorAsync(logger, ex, opts.Verbose).ConfigureAwait(false);
                return 1;
            }
        }
    }

    /// <summary>
    /// Builds the host (no crew loaded) and prints the sorted, de-duplicated names of every
    /// tool in the runtime registry — one per line — to stdout, then exits 0. All logs are
    /// routed to stderr so stdout carries ONLY the tool manifest, which the packaging/lint
    /// tooling consumes as the runtime tool contract.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; the emitted lines are tool names, not localizable UI text.")]
    public static Task<int> RunListToolsAsync(
        RunnerOptionsBase opts,
        string loggerCategory,
        Action<HostBuilderContext, IServiceCollection>? configureServices = null)
    {
        ArgumentNullException.ThrowIfNull(opts);
        _ = loggerCategory;
        return RunListToolsCoreAsync();

        async Task<int> RunListToolsCoreAsync()
        {
            var cwd = Directory.GetCurrentDirectory();
            var settingsPath = RunnerSettings.ResolveSettingsPath(opts.SettingsPath, cwd);

            // Mount the cwd 1:1 (read-only) so the FileSystem section is non-empty and
            // IFileSystemService is registered — otherwise the filesystem-backed tools cannot
            // be constructed when the registry enumerates the DI-provided IBaseTool set.
            var cliMounts = opts.Mounts.ToList();
            cliMounts.Insert(0, $"{cwd}:{cwd}:ro");

            using var host = RunnerHost.Build(
                settingsPath, cliMounts,
                allowExternalMounts: opts.EffectiveAllowExternalMounts,
                configureLogging: (_, b) => ConfigureStderrOnlyLogging(b),
                configureServices: (ctx, services) =>
                {
                    // Mirror the crew-execution path (TryBuildHost registers the human_input
                    // tool + AutoApprove provider before the runner's own hook). The manifest
                    // must list exactly the tools a real kickoff exposes — without this,
                    // human_input is silently absent from --list-tools.
                    services.AddOrkeonHumanInput();
                    configureServices?.Invoke(ctx, services);
                });

            var registry = host.Services.GetRequiredService<IToolRegistry>();
            var tools = await registry.GetAllToolsAsync().ConfigureAwait(false);
            var names = tools
                .Select(t => t.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal);

            foreach (var name in names)
                Console.WriteLine(name);

            return 0;
        }
    }

    /// <summary>
    /// Loads a crew from an Orkéon Scripting (.ork.ts) source, a multi-file crew directory, or a
    /// single YAML definition — dispatching on <see cref="IsScriptedCrewDefinition"/> then on the
    /// directory form (already validated by <see cref="TryBuildHost"/> via
    /// <see cref="CrewDirectoryLayout"/>). Shared by the one-shot and validate flows so both
    /// accept the same <c>-c/--config</c> targets identically.
    /// <para>
    /// Public because the service host loads the same targets: a crew hosted by a daemon and a
    /// crew launched from a terminal must be the same crew, and duplicating the dispatch is how
    /// they would quietly stop being.
    /// </para>
    /// </summary>
    /// <param name="host">The built host, for the services a scripted crew needs.</param>
    /// <param name="factory">The crew factory.</param>
    /// <param name="configPath">A <c>.ork.ts</c> source, a crew directory, or a YAML file.</param>
    /// <param name="logger">Logger for the loading trace.</param>
    /// <param name="ct">Cancellation token.</param>
    public static Task<Domain.Crew.Crew> LoadCrewAsync(
        IHost host,
        ICrewFactory factory,
        string configPath,
        ILogger logger,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        if (IsScriptedCrewDefinition(configPath))
            return LoadCrewFromScriptAsync(host, factory, configPath, logger, ct);

        return Directory.Exists(configPath)
            ? factory.CreateFromDirectoryAsync(configPath, ct)
            : factory.CreateFromFileAsync(configPath, ct);
    }

    /// <summary>
    /// Console logging preset that routes every level to stderr. Used by <c>--list-tools</c> so
    /// stdout stays a clean, machine-readable tool manifest while warnings/errors remain visible.
    /// </summary>
    private static void ConfigureStderrOnlyLogging(ILoggingBuilder b)
    {
        b.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss.fff ";
        });
        // LogToStandardErrorThreshold lives on ConsoleLoggerOptions (the provider), not on the
        // Simple formatter options — route every level to stderr so stdout stays clean.
        b.Services.Configure<Microsoft.Extensions.Logging.Console.ConsoleLoggerOptions>(
            o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        b.SetMinimumLevel(LogLevel.Warning);
    }

    /// <summary>
    /// Reports a crew-load failure — a mixed directory layout, malformed YAML, an unknown tool —
    /// as the single actionable line the caller already wrote to stderr. Such a failure is a
    /// configuration mistake, not a runner bug: logging the exception itself renders its type and
    /// stack, which buries the sentence that says what to fix. The failure still reaches the log
    /// stream as a message, and the full dump stays one opt-in away — <c>-v</c> or
    /// <c>ORKEON_DEBUG=1</c>, the same switch the scripting CLI's own fault barrier uses.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    private static async Task ReportCrewConfigurationErrorAsync(ILogger logger, Exception ex, int verbose)
    {
        LogCrewConfigurationError(logger, ex.Message);

        if (verbose > 0 || RunnerEnvironment.DebugDiagnostics)
            await Console.Error.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 13, Level = LogLevel.Information, Message = "Validating crew from {ConfigPath} (dry-run — no LLM probe, no kickoff)...")]
    private static partial void LogValidatingCrew(ILogger logger, string configPath);

    [LoggerMessage(EventId = 14, Level = LogLevel.Error, Message = "Crew configuration error: {Reason}")]
    private static partial void LogCrewConfigurationError(ILogger logger, string reason);
}
