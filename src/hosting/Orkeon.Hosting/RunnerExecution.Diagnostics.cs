using Orkeon.Constants.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.DependencyInjection;

namespace Orkeon.Hosting;

/// <summary>
/// Diagnostic (non-executing) runner modes shared by all runners: <c>--validate</c>
/// (dry-run crew load, no LLM probe / no kickoff) and <c>--list-tools</c> (runtime tool
/// registry dump). Both honour the runner's <c>configureServices</c> hook so runner-specific
/// tools (e.g. <c>semantic_search</c>) appear exactly as at runtime.
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
            // configPath is the operator's own spelling — it is what the VALIDATION OK/FAILED
            // lines echo. The load itself goes through the virtual spelling.
            var (logger, configPath, virtualConfigPath, cliMounts) =
                (bootstrap.Logger, bootstrap.ConfigPath, bootstrap.VirtualConfigPath, bootstrap.CliMounts);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            using var shutdown = RegisterGracefulShutdown(cts, logger);

            try
            {
                RunnerLogging.LogMounts(cliMounts, logger);
                LogValidatingCrew(logger, configPath);

                // Same order as a kickoff (STUDIO-21): the MCP tools exist before the crew
                // resolves its own, so --validate judges the crew a run would actually get.
                await McpStartup.ConnectConfiguredServersAsync(host, cts.Token).ConfigureAwait(false);

                // The factory and the repositories it fills are scoped (GAP-25).
                var scope = host.Services.CreateAsyncScope();
                await using var _ = scope.ConfigureAwait(false);
                var factory = scope.ServiceProvider.GetRequiredService<ICrewFactory>();
                var crew = await LoadCrewAsync(
                    host, factory, virtualConfigPath, logger, cts.Token, bootstrap.IsCrewDirectory)
                    .ConfigureAwait(false);

                // Tools live on the agents, not the crew aggregate (which holds only ids).
                // Re-hydrate the agents and count the distinct tool names actually resolved.
                var agentRepository = scope.ServiceProvider.GetRequiredService<Domain.Agent.IAgentRepository>();
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

            // The tool list does not depend on the model, but an --llm-profile the settings do
            // not define is refused here as on a run, rather than ignored in silence (STUDIO-50).
            if (!EnsureLlmProfileIsKnown(opts.LlmProfile, settingsPath))
                return 1;

            using var host = await BuildToolHostAsync(
                settingsPath,
                cwd,
                opts.Mounts.ToList(),
                opts.MountIds,
                opts.EffectiveAllowExternalMounts,
                (ctx, services) =>
                {
                    // Mirror the crew-execution path (TryBuildHost registers the human_input
                    // tool + AutoApprove provider before the runner's own hook). The manifest
                    // must list exactly the tools a real kickoff exposes — without this,
                    // human_input is silently absent from --list-tools.
                    services.AddOrkeonHumanInput();
                    configureServices?.Invoke(ctx, services);
                },
                CancellationToken.None).ConfigureAwait(false);
            if (host is null)
                return 1;

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
    /// The host <c>--list-tools</c> prints and <c>orkeon mcp serve</c> serves (GAP-24): the runner
    /// host over the settings' mounts and <paramref name="cliMounts"/>, no crew, every log line on
    /// stderr — stdout carries the manifest, or the protocol — and the MCP servers of the settings
    /// connected, so both see the tools a kickoff sees (STUDIO-21).
    /// <para>
    /// The host needs a file system even to list the tools: without one <c>IFileSystemService</c>
    /// is not registered, and the filesystem-backed tools cannot be constructed when the registry
    /// enumerates the DI-provided <c>IBaseTool</c> set. A mount of <paramref name="cliMounts"/> or
    /// of the settings gives it one. Without any, <paramref name="workingDirectory"/> is mounted
    /// internally, read-only — and only then: an internal mount is a boundary in physical space
    /// (<c>FileSystemRegistry</c>), so beside the settings' mounts it would wall off every one
    /// the working directory contains, and a server an MCP client starts in <c>/</c> would reach
    /// no file at all.
    /// </para>
    /// </summary>
    /// <returns>
    /// The host, or <see langword="null"/> when the mounts or the settings are refused — a mount
    /// guard, a mount folder that does not exist, a setting the host build rejects — after one
    /// line on stderr says why, rather than an unhandled exception.
    /// </returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303", Justification = "Framework is not localized; literals are CLI diagnostic/console messages.")]
    internal static async Task<IHost?> BuildToolHostAsync(
        string? settingsPath,
        string workingDirectory,
        IReadOnlyList<string> cliMounts,
        IEnumerable<string> mountIds,
        bool allowExternalMounts,
        Action<HostBuilderContext, IServiceCollection>? configureServices,
        CancellationToken ct)
    {
        if (!EnsureReservedRootsAreFree(cliMounts, settingsPath, RunnerVirtualRoots.Crew, RunnerVirtualRoots.Sandbox)
            || !EnsureMountSelectionIsResolvable(cliMounts, mountIds, CrewMountDeclarations.None, settingsPath, out var selection)
            || !EnsureMountSourcesExist(cliMounts, settingsPath, selection))
        {
            return null;
        }

        var hasFileSystem = cliMounts.Count > 0
            || RunnerSettings.ReadDeclaredAgentFacingMounts(settingsPath).Count > 0
            || RunnerSettings.ReadDeclaredInternalMounts(settingsPath).Count > 0;
        string[] internalMounts = hasFileSystem
            ? []
            : [$"{FileSystemMount.Quote(workingDirectory)}:{RunnerVirtualRoots.Crew}:ro"];

        IHost host;
        try
        {
            host = RunnerHost.Build(
                settingsPath,
                new RunnerMountPlan
                {
                    CliMounts = cliMounts,
                    InternalMounts = internalMounts,
                    SelectedMountIds = selection.SelectedMountIds,
                    CrewMountReferences = selection.CrewMountReferences,
                    AllowExternalMounts = allowExternalMounts,
                },
                configureLogging: (_, b) => ConfigureStderrOnlyLogging(b),
                configureServices: configureServices);
        }
        catch (RunnerSettingsException ex)
        {
            // A setting the host build refuses — a retired key, MCP:EnableServer, an LLM profile
            // it cannot build, a file it cannot read, an address that is none (GAP-35) — is the
            // operator's to fix: the sentence, not a stack trace.
            ReportRefusedSettings(ex);
            return null;
        }

        try
        {
            await McpStartup.ConnectConfiguredServersAsync(host, ct).ConfigureAwait(false);
            return host;
        }
        catch (OperationCanceledException)
        {
            host.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Loads a crew from an Orkeon Scripting (.ork.ts) source, a multi-file crew directory, or a
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
    /// <param name="configPath">
    /// The crew target as a <b>virtual</b> path — a <c>.ork.ts</c> source, a crew directory, or a
    /// YAML file, under whatever root the caller mounted it (the runners use
    /// <see cref="RunnerVirtualRoots.Crew"/>). Since ADR-008 this is never a disk path:
    /// the directory-or-file question is asked of the VFS, not of <c>System.IO</c>.
    /// </param>
    /// <param name="factory">The crew factory.</param>
    /// <param name="logger">Logger for the loading trace.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="targetIsDirectory">
    /// Whether the target is a crew directory, when the caller already knows — every runner in
    /// this repository does, because it is the same answer that decided the virtual spelling.
    /// Left null, the VFS is asked, which cannot see through a symlink: a crew directory reached
    /// by a link (<c>deploy/current → deploy/release-42</c>) reports as
    /// <see cref="VirtualEntryKind.SymLink"/> and would be read as a single file.
    /// </param>
    public static async Task<Domain.Crew.Crew> LoadCrewAsync(
        IHost host,
        ICrewFactory factory,
        string configPath,
        ILogger logger,
        CancellationToken ct,
        bool? targetIsDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        if (IsScriptedCrewDefinition(configPath))
            return await LoadCrewFromScriptAsync(host, factory, configPath, logger, ct).ConfigureAwait(false);

        var isDirectory = targetIsDirectory;
        if (isDirectory is null)
        {
            var fileSystem = host.Services.GetRequiredService<IFileSystemService>();
            isDirectory = await fileSystem.GetEntryKindAsync(configPath, ct).ConfigureAwait(false)
                == VirtualEntryKind.Directory;
        }

        return isDirectory.Value
            ? await factory.CreateFromDirectoryAsync(configPath, ct).ConfigureAwait(false)
            : await factory.CreateFromFileAsync(configPath, ct).ConfigureAwait(false);
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
