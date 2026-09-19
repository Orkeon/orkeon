using Orkeon.Constants.Configuration;
using Orkeon.Constants.FileSystem;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.EventHub.DependencyInjection;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.Telemetry;
using Orkeon.Infrastructure.Logging;
using Orkeon.Analysis.DependencyInjection;
using Orkeon.Compliance.Vfs;
using Orkeon.Tools.Abstractions.DependencyInjection;
using Orkeon.Tools.Analysis.DependencyInjection;
using Orkeon.Tools.Embeddings.Local.DependencyInjection;
using Orkeon.Tools.Code.DependencyInjection;
using Orkeon.Tools.Data.DependencyInjection;
using Orkeon.Tools.EventHub.DependencyInjection;
using Orkeon.Tools.FileSystem.DependencyInjection;
using Orkeon.Tools.Web.DependencyInjection;

namespace Orkeon.Hosting;

/// <summary>
/// The VFS surface a runner host is built over. One object rather than four loose
/// parameters on <see cref="RunnerHost.Build"/>: the four decide the same thing together —
/// which physical directories the run can reach, under which virtual names, and with which
/// rights — and a caller that sets one almost always has an opinion on its neighbours.
/// </summary>
public sealed record RunnerMountPlan
{
    /// <summary>The agent-facing mounts, typically the CLI's <c>--mount</c> arguments.</summary>
    public IReadOnlyList<string> CliMounts { get; init; } = [];

    /// <summary>
    /// Mounts registered with <c>MountVisibility.Internal</c>: resolvable by the VFS, absent
    /// from <c>GetAvailableMounts()</c> and therefore invisible to agents. Same grammar as
    /// <see cref="CliMounts"/>.
    /// </summary>
    public IReadOnlyList<string> InternalMounts { get; init; } = [];

    /// <summary>
    /// When <see langword="true"/>, mount base paths are added to the PathSecurity whitelist
    /// (<c>AdditionalAllowedDirectories</c>), allowing mounts from directories outside the
    /// workspace root.
    /// </summary>
    public bool AllowExternalMounts { get; init; }

    /// <summary>
    /// When non-null, enables LLM exchange logging under this <b>virtual</b> path — the caller
    /// is responsible for having mounted it (the runners pass
    /// <see cref="RunnerVirtualRoots.LlmLogs"/> in <see cref="InternalMounts"/>). All HTTP
    /// request/response headers and payloads are captured as JSON Lines (.jsonl) files,
    /// written through <c>IFileSystemService</c> like every other file the framework touches.
    /// </summary>
    public string? LlmLogVirtualPath { get; init; }
}

/// <summary>
/// Shared host builder for Orkeon runners and CLIs.
/// Configures LLM, Orkeon services, tools, file system, and tool registry.
/// </summary>
[SuppressVfsCompliance("EXCEPTION-BOOTSTRAP: resolves user-supplied settings paths and provisions VFS mounts before the DI container (and thus IFileSystemService) exists.")]
public static partial class RunnerHost
{
    /// <summary>
    /// WIN-01: the actionable warning emitted once per host build when no <c>Llm</c>
    /// section is configured and the runtime will fall back to the echo provider. Public
    /// because Orkeon Studio reproduces this wording in its own pre-save validation and pins
    /// the two texts together — a UI that warns differently from the runtime is worse than
    /// one that does not warn at all.
    /// </summary>
    public const string LlmNotConfiguredMessage = OperatorMessages.LlmNotConfigured;

    /// <summary>
    /// Builds a fully-configured host with all Orkeon services.
    /// </summary>
    /// <param name="settingsPath">Resolved appsettings.json path (nullable).</param>
    /// <param name="mounts">The VFS surface the host is built over — see <see cref="RunnerMountPlan"/>.</param>
    /// <param name="configureLogging">Optional callback to customize logging (default: a single-line console at Warning level; --verbose raises it).</param>
    /// <param name="configureServices">Optional callback to register additional services.</param>
    /// <param name="configureBuilder">
    /// Optional callback on the builder itself, before it is built. The service host uses it
    /// for <c>UseSystemd()</c> and <c>UseWindowsService()</c>: a long-lived daemon needs to
    /// tell its supervisor it is up, and that is decided on the builder, not in the services.
    /// </param>
    public static IHost Build(
        string? settingsPath,
        RunnerMountPlan mounts,
        Action<HostBuilderContext, ILoggingBuilder>? configureLogging = null,
        Action<HostBuilderContext, IServiceCollection>? configureServices = null,
        Action<IHostBuilder>? configureBuilder = null)
    {
        ArgumentNullException.ThrowIfNull(mounts);

        // Collected while the configuration is composed, logged once the host exists: no
        // logger is available inside ConfigureAppConfiguration, and the replacement of a
        // settings entry by a --mount is exactly the kind of decision an operator reading the
        // log must be able to see (STUDIO-15 D-01).
        var replacedRoots = new List<string>();
        var builder = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, b) =>
                ConfigureAppConfiguration(
                    b, settingsPath, mounts.CliMounts, mounts.InternalMounts, mounts.AllowExternalMounts, replacedRoots))
            .ConfigureServices((context, services) =>
                ConfigureRunnerServices(context, services, mounts.LlmLogVirtualPath, configureLogging, configureServices));

        configureBuilder?.Invoke(builder);

        var host = builder.Build();

        LogMountReplacements(host, replacedRoots);
        WarnIfLlmNotConfigured(host);
        ActivateTelemetry(host);
        return host;
    }

    /// <summary>
    /// One <c>Information</c> line per settings entry a <c>--mount</c> took the place of. The
    /// run's own intent won over the machine's default, and the log says so by root name —
    /// the same words for every runner, the daemon included.
    /// </summary>
    private static void LogMountReplacements(IHost host, List<string> replacedRoots)
    {
        if (replacedRoots.Count == 0)
            return;

        var logger = host.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Orkeon.Hosting.RunnerHost");
        foreach (var root in replacedRoots)
            LogMountReplacesSettingsEntry(logger, root);
    }

    /// <summary>
    /// OpenTelemetry creates its tracer and meter providers in a hosted service, and the
    /// runners never start the host -- they resolve services and run one command. Resolving
    /// the two providers here is what creates them: the ActivitySource listeners come alive,
    /// the exporters (OTLP from the environment or the settings, console) attach, and the
    /// container disposes them with the host, which flushes the last batch.
    /// </summary>
    private static void ActivateTelemetry(IHost host)
    {
        _ = host.Services.GetService<OpenTelemetry.Trace.TracerProvider>();
        _ = host.Services.GetService<OpenTelemetry.Metrics.MeterProvider>();
    }

    /// <summary>
    /// WIN-01: when <see cref="RegisterLlmProvider"/> found no <c>Llm</c> section, the
    /// runtime silently degrades to the <c>&lt;undefined-llm&gt;</c> echo provider — the
    /// number one onboarding trap on a fresh install. Emit one actionable warning per host
    /// build: on the host logger AND on stderr (the host may reconfigure the logger below
    /// Warning, so the stderr line is the guarantee). Runs after <c>Build()</c> because no
    /// logger exists yet at service-registration time.
    /// </summary>
    private static void WarnIfLlmNotConfigured(IHost host)
    {
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        var logger = host.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Orkeon.Hosting.RunnerHost");

        var llmSection = configuration.GetSection(ConfigurationKeys.LlmSection);
        if (llmSection.Exists())
        {
            // One line of truth about what was actually resolved (file + ORKEON_ overlay):
            // when a run behaves as if a setting never arrived — a timeout still at its
            // default, a temperature the vendor rejects — this line settles where the
            // chain broke. Never the API key.
            LogLlmResolved(
                logger,
                llmSection["Model"] ?? "(default)",
                llmSection["BaseUrl"] ?? "(provider default)",
                llmSection["Temperature"] ?? "(default)",
                llmSection["TimeoutSeconds"] ?? "(default 30)");
            return;
        }

        Console.Error.WriteLine("WARNING: " + LlmNotConfiguredMessage);
        LogLlmNotConfigured(logger);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = LlmNotConfiguredMessage)]
    private static partial void LogLlmNotConfigured(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message =
        "LLM resolved: model={Model} baseUrl={BaseUrl} temperature={Temperature} timeoutSeconds={TimeoutSeconds}")]
    private static partial void LogLlmResolved(
        ILogger logger, string model, string baseUrl, string temperature, string timeoutSeconds);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message =
        "mount {VirtualPath}: --mount replaces the settings entry")]
    private static partial void LogMountReplacesSettingsEntry(ILogger logger, string virtualPath);

    /// <summary>The configuration section <c>--allow-external-mounts</c> extends.</summary>
    private const string PathSecurityWhitelistSection = "PathSecurity:AdditionalAllowedDirectories";

    /// <summary>
    /// Composes the configuration a runner host reads its mounts from: the settings file, the
    /// <c>ORKEON_</c> environment, then this method's own in-memory source, which is added
    /// last and therefore wins on an identical key.
    /// <para>
    /// A <c>--mount</c> is placed <b>by virtual root</b> (STUDIO-15 D-01). On a root the
    /// declared array — settings file AND environment, read from the builder's own snapshot —
    /// already holds, it is written at that entry's index: the mount of this run replaces the
    /// machine's default for that root, which is the most specific intent there is. On a new
    /// root it is appended after the highest declared index, so nothing the operator declared
    /// is lost. Two earlier shapes of this method each got one of those halves wrong: writing
    /// from index 0 replaced the operator's first entry on every run (the crew mount is always
    /// there), and appending unconditionally produced "Duplicate virtual paths" out of a DI
    /// factory the moment a settings entry and a <c>--mount</c> named the same root — which
    /// is exactly what Studio's team flow produces by design, since a team associates a folder
    /// the settings already declare.
    /// </para>
    /// <para>
    /// <c>InternalMounts</c> keep appending: they ride their own key so they can carry
    /// Internal visibility (the mount-string grammar has no room for it), and configuration
    /// rather than a hosted service because the runners never start the host — an
    /// IHostedService would silently never fire under <c>--validate</c> or <c>--list-tools</c>.
    /// </para>
    /// <para>
    /// A mount the settings declare is the machine owner's explicit intent, so its base path
    /// is always whitelisted for <c>PathValidator</c> (STUDIO-12 C4): until then such a folder
    /// was mounted and every access to it refused as "outside the workspace root", with no
    /// flag able to rescue it — <c>--allow-external-mounts</c> only ever whitelisted the
    /// <c>--mount</c> arguments, and still does exactly that, and only that.
    /// </para>
    /// </summary>
    /// <param name="builder">The host's configuration builder.</param>
    /// <param name="settingsPath">Resolved appsettings.json path, or null.</param>
    /// <param name="cliMounts">The agent-facing mounts, the crew mount included.</param>
    /// <param name="internalMounts">The runner's own Internal mounts.</param>
    /// <param name="allowExternalMounts">Whether <c>--allow-external-mounts</c> was given.</param>
    /// <param name="replacedRoots">Receives the virtual root of every settings entry a
    /// <c>--mount</c> took the place of, for the caller to log once a logger exists.</param>
    private static void ConfigureAppConfiguration(
        IConfigurationBuilder builder,
        string? settingsPath,
        IReadOnlyList<string> cliMounts,
        IReadOnlyList<string> internalMounts,
        bool allowExternalMounts,
        List<string> replacedRoots)
    {
        if (settingsPath != null && File.Exists(settingsPath))
            builder.AddJsonFile(settingsPath, optional: true);

        builder.AddEnvironmentVariables("ORKEON_");

        var overrides = new Dictionary<string, string?>();
        using var declared = DeclaredConfiguration.Snapshot(builder);

        var declaredMounts = declared.Entries(ConfigurationKeys.FileSystemMounts);
        var replacedIndices = new HashSet<int>();
        var nextMountIndex = NextIndex(declaredMounts);
        foreach (var mount in cliMounts)
        {
            var root = TryGetVirtualRoot(mount);
            var declaredIndex = root is null ? null : IndexOfRoot(declaredMounts, root);
            if (declaredIndex is { } index)
            {
                overrides[$"{ConfigurationKeys.FileSystemMounts}:{index}"] = mount;
                if (replacedIndices.Add(index))
                    replacedRoots.Add(root!);
            }
            else
            {
                overrides[$"{ConfigurationKeys.FileSystemMounts}:{nextMountIndex++}"] = mount;
            }
        }

        var declaredInternal = declared.Entries(ConfigurationKeys.FileSystemInternalMounts);
        var nextInternalIndex = NextIndex(declaredInternal);
        foreach (var mount in internalMounts)
            overrides[$"{ConfigurationKeys.FileSystemInternalMounts}:{nextInternalIndex++}"] = mount;

        // The whitelist "additionally" extends what the settings declare (the flag's own
        // documented wording), so every entry continues AFTER the declared ones: starting the
        // count at 0 silently replaced the operator's first allowed directory instead of
        // adding to it.
        var nextWhitelistIndex = NextIndex(declared.Entries(PathSecurityWhitelistSection));
        foreach (var (index, value) in declaredMounts)
        {
            // A declared entry a --mount replaced is no longer mounted; whitelisting its base
            // would open a folder nothing reaches, so only the entries still in force count.
            if (!replacedIndices.Contains(index) && TryExtractMountBasePath(value) is { } declaredBase)
                overrides[$"{PathSecurityWhitelistSection}:{nextWhitelistIndex++}"] = declaredBase;
        }

        foreach (var (_, value) in declaredInternal)
        {
            if (TryExtractMountBasePath(value) is { } declaredBase)
                overrides[$"{PathSecurityWhitelistSection}:{nextWhitelistIndex++}"] = declaredBase;
        }

        // --allow-external-mounts: the --mount arguments (and the runner's own internal
        // mounts) may point outside the working directory. Unchanged: a folder named on the
        // command line is still gated behind the explicit opt-in.
        if (allowExternalMounts)
        {
            foreach (var mount in cliMounts.Concat(internalMounts))
            {
                if (TryExtractMountBasePath(mount) is { } basePath)
                    overrides[$"{PathSecurityWhitelistSection}:{nextWhitelistIndex++}"] = basePath;
            }
        }

        if (overrides.Count > 0)
            builder.AddInMemoryCollection(overrides);
    }

    /// <summary>
    /// The index a new entry of an indexed section lands on: one past the highest declared
    /// index, not the count. Configuration is a sparse key/value space and an operator may
    /// legitimately have declared 0 and 2.
    /// </summary>
    private static int NextIndex(List<(int Index, string? Value)> entries) =>
        entries.Count == 0 ? 0 : entries.Max(entry => entry.Index) + 1;

    /// <summary>
    /// The lowest declared index whose entry claims <paramref name="root"/>, or null. A root
    /// declared twice is refused before any host by <c>RunnerExecution.EnsureVirtualRootsAreUnique</c>;
    /// here the first declaration is the one a <c>--mount</c> replaces, so the answer is
    /// deterministic for the callers that build a host without that guard.
    /// </summary>
    private static int? IndexOfRoot(List<(int Index, string? Value)> entries, string root)
    {
        foreach (var (index, value) in entries)
        {
            if (value is not null && string.Equals(TryGetVirtualRoot(value), root, StringComparison.Ordinal))
                return index;
        }

        return null;
    }

    /// <summary>
    /// The virtual root a mount string claims, without its trailing slash, or null when the
    /// string is not a well-formed mount — the parser reports those at host build time with
    /// its own precise message. Ordinal, like the registry's own duplicate check.
    /// </summary>
    private static string? TryGetVirtualRoot(string mountString)
    {
        try
        {
            var virtualPath = FileSystemMount.Parse(mountString).VirtualPath;
            return virtualPath.Length > 1 ? virtualPath.TrimEnd('/') : virtualPath;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The entries the sources added so far declare — settings file and <c>ORKEON_</c>
    /// environment alike — read from one snapshot of the builder. What this class adds must
    /// be placed against what is really declared, never against the file alone: an
    /// <c>ORKEON_Orkeon__FileSystem__Mounts__0</c> is a declared mount too.
    /// </summary>
    private sealed class DeclaredConfiguration : IDisposable
    {
        private readonly IConfigurationRoot? _snapshot;

        private DeclaredConfiguration(IConfigurationRoot? snapshot) => _snapshot = snapshot;

        public void Dispose() => (_snapshot as IDisposable)?.Dispose();

        /// <summary>
        /// Builds the sources added so far. An unreadable settings file yields an empty
        /// snapshot: the real Build() a few lines later reports it properly, and placing
        /// everything from index 0 is correct whenever nothing was declared — which is the
        /// case that just failed to parse.
        /// </summary>
        public static DeclaredConfiguration Snapshot(IConfigurationBuilder builder)
        {
            try
            {
                return new DeclaredConfiguration(builder.Build());
            }
            catch (Exception ex) when (ex is InvalidDataException or FormatException or IOException)
            {
                return new DeclaredConfiguration(null);
            }
        }

        /// <summary>The numerically-keyed children of <paramref name="section"/>, by ascending index.</summary>
        public List<(int Index, string? Value)> Entries(string section)
        {
            if (_snapshot is null)
                return [];

            var entries = new List<(int Index, string? Value)>();
            foreach (var child in _snapshot.GetSection(section).GetChildren())
            {
                if (int.TryParse(child.Key, CultureInfo.InvariantCulture, out var index))
                    entries.Add((index, child.Value));
            }

            entries.Sort((a, b) => a.Index.CompareTo(b.Index));
            return entries;
        }
    }

    private static void ConfigureRunnerServices(
        HostBuilderContext context,
        IServiceCollection services,
        string? llmLogVirtualPath,
        Action<HostBuilderContext, ILoggingBuilder>? configureLogging,
        Action<HostBuilderContext, IServiceCollection>? configureServices)
    {
        ConfigureRunnerLogging(context, services, configureLogging);
        ConfigureLlmExchangeLogging(context, services, llmLogVirtualPath);

        // --- LLM provider from appsettings.json "Llm" section ---
        RegisterLlmProvider(context, services);

        // Core Orkeon services
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        // OpenTelemetry export: the `Telemetry` section of the settings, or the standard
        // OTEL_EXPORTER_OTLP_ENDPOINT environment a .NET Aspire AppHost (or any collector)
        // injects. Until 2026-09-11 the runners registered no exporter at all, so a run
        // launched from Aspire produced spans that went nowhere.
        services.AddOrkeonTelemetry(context.Configuration);

        // Runners load crews from user-authored YAML/TS: a tool referenced by a crew but
        // absent from the registry is almost always a typo or a missing registration, not an
        // intentional degrade. Fail loading with an explicit "unknown tool(s): …; available: …"
        // message rather than silently dropping the tool (the library default stays lenient).
        // A host can still opt back out via "Orkeon:CrewFactory:StrictTools": false.
        var strictTools = context.Configuration.GetValue(
            "Orkeon:CrewFactory:StrictTools", defaultValue: true);
        services.Configure<Orkeon.Infrastructure.Configuration.CrewFactoryOptions>(
            o => o.StrictTools = strictTools);

        // Per-tool-call permission gate — config opt-in:
        // Orkeon:Security:PermissionGate:Enabled = true. No-op otherwise.
        services.AddOrkeonPermissionGate(context.Configuration);

        // Register all standard tools
        services.AddOrkeonFileSystemTools();
        services.AddOrkeonDataTools();
        services.AddOrkeonWebTools();
        services.AddOrkeonCodeTools();
        services.AddOrkeonAbstractionTools();
        // Session primitives (session_store/session_snip/token_budget/
        // memory_store/session_cost/session_stats) — previously REPL-only
        // (ConsoleApp), which made standalone crew runs silently lose the session
        // buffer, auto-compaction and memory. In-memory backing; idempotent.
        services.AddOrkeonSessionTools();

        // EventHub — the in-memory hub plus its seven agent tools (publish_event,
        // post_message, send_request, reply_to, receive_message, wait_for_event,
        // get_last_value). Four+ example crews reference these tools; without the hub
        // singleton the tools can't be constructed, so both must be wired together.
        // Registered under IBaseTool, which is what ServiceProviderToolRegistry enumerates.
        services.AddOrkeonInMemoryEventHub();
        services.AddOrkeonEventHubTools();

        // The ACL rides along, with the permissive default: a crew that declares no links:
        // block behaves exactly as before, and a crew that declares one is actually held to
        // it. Without this line the links: grammar parsed, registered — and guarded nothing,
        // because no composition root ever put the stage on the pipeline. A deployment that
        // wants the closed door swaps the policy in its own configureServices.
        services.AddOrkeonEventHubAcl();

        // Virtual file system mounts (from appsettings + CLI --mount args, plus the
        // infrastructure mounts a runner declares for itself). Either list alone is enough
        // to make the VFS real: --list-tools has only the latter, and without the service
        // the filesystem-backed tools cannot even be constructed.
        if (HasMounts(ConfigurationKeys.FileSystemMounts) || HasMounts(ConfigurationKeys.FileSystemInternalMounts))
            services.AddOrkeonFileSystem(context.Configuration);

        bool HasMounts(string key)
        {
            var section = context.Configuration.GetSection(key);
            return section.Exists() && section.GetChildren().Any();
        }

        // RaggableTree — available by default (crew-driven). Enables the semantic-graph
        // tools (codebase_map, symbol_detail, flow_trace, …) backed by a singleton
        // InMemoryRaggableStore. Analysed languages are auto-detected per index_codebase
        // call, never pinned a priori; a host opts out with "RaggableTree:Enabled": false.
        RegisterRaggableTree(context, services);

        // WebSearch tool — API key resolved at runtime via ISecretProvider
        services.AddOrkeonWebSearchTool();

        // Generic cache_search tool — queries the RAG cache populated by other
        // tools (web_scrape with cached=true, etc). Reuses IEmbeddingService and
        // IMemoryProvider from AddOrkeonInfrastructure() so agents can retrieve
        // chunks semantically instead of pinning full pages in their context.
        services.AddOrkeonCacheSearchTool();

        var braveKey = context.Configuration["BRAVE_API_KEY"]
            ?? Environment.GetEnvironmentVariable("BRAVE_API_KEY");
        if (!string.IsNullOrEmpty(braveKey))
            services.AddOrkeonBraveSearchTool(braveKey);

        // Tool registry from DI
        services.AddSingleton<IToolRegistry, ServiceProviderToolRegistry>();

        // Runner-specific services
        configureServices?.Invoke(context, services);
    }

    private static void ConfigureRunnerLogging(
        HostBuilderContext context,
        IServiceCollection services,
        Action<HostBuilderContext, ILoggingBuilder>? configureLogging)
    {
        // Logging (customizable, sensible default)
        if (configureLogging != null)
        {
            services.AddLogging(b => configureLogging(context, b));
            return;
        }

        // Quiet default — aligned across all runners (RUN-03):
        // single-line console + Warning level. Use --verbose 1 (or 2)
        // to opt into Information/Debug from Orkeon modules via
        // RunnerExecution.ConfigureVerboseLogging.
        services.AddLogging(b =>
        {
            b.AddSimpleConsole(opts =>
            {
                opts.SingleLine = true;
                opts.TimestampFormat = "HH:mm:ss.fff ";
            });
            b.SetMinimumLevel(LogLevel.Warning);
        });
    }

    private static void ConfigureLlmExchangeLogging(
        HostBuilderContext context,
        IServiceCollection services,
        string? llmLogVirtualPath)
    {
        // --- LLM exchange logging (--llm-log) ---
        if (string.IsNullOrEmpty(llmLogVirtualPath))
            return;

        // A virtual path, handed straight to LlmExchangeJsonLogger's logVirtualDir: the
        // logger writes through IFileSystemService. Resolving it to a full physical path
        // here is what used to force the identity mount (ADR-008).
        var logDir = llmLogVirtualPath;
        var llmLogSection = context.Configuration.GetSection("LlmLogging");
        // Bind known properties from the "LlmLogging" config section.
        // The section is optional; absent keys keep their defaults
        // (FullEmbeddingLog=true, LogStreamingExchanges=true,
        // MaxBodyLengthChars=0/no truncation).
        var llmOpts = new LlmLoggingOptions
        {
            FullEmbeddingLog = !bool.TryParse(llmLogSection["FullEmbeddingLog"], out var fullEmbed) || fullEmbed,
            LogStreamingExchanges = !bool.TryParse(llmLogSection["LogStreamingExchanges"], out var logStream) || logStream,
            MaxBodyLengthChars = int.TryParse(llmLogSection["MaxBodyLengthChars"], out var maxLen) ? maxLen : 0,
        };
        services.AddLlmExchangeLogging(logDir, llmOpts);
    }

    /// <summary>
    /// The absolute physical base path a mount string declares, or null when the string is
    /// not a well-formed mount or names a path the platform refuses to resolve — both are
    /// reported by the mount parser or the registry at host build time, with their own
    /// message. The grammar (drive letters, escaped separators) is the domain type's business,
    /// not this file's.
    /// </summary>
    private static string? TryExtractMountBasePath(string? mountString)
    {
        if (mountString is null || FileSystemMount.TryGetBasePath(mountString) is not { } basePath)
            return null;

        try
        {
            return Path.GetFullPath(basePath);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }
    }

    private static void RegisterRaggableTree(
        HostBuilderContext context,
        IServiceCollection services)
    {
        // Available by default; a host opts out with "RaggableTree:Enabled": false. The section
        // is OPTIONAL and only carries infra knobs (embedding backend, index mode, exclude globs).
        // Crucially, the analysed languages are NOT read from config: they are auto-detected from
        // the codebase and scoped per index_codebase call by the crew/agent, never pinned a priori.
        var section = context.Configuration.GetSection("RaggableTree");
        if (section.Exists() && !section.GetValue("Enabled", defaultValue: true))
            return;

        var embeddingSection = section.GetSection("Embedding");
        // Default to on-device local embeddings (zero-config BGE-micro-v2, no network) so
        // codebase_search works out of the box; a host can override to OpenAI/Ollama via config.
        var provider = ParseEnum(embeddingSection["Provider"], EmbeddingProviderKind.LocalSmartComponents);
        var options = new RaggableTreeOptions
        {
            Enabled = true,
            // Languages intentionally omitted — auto-detected, crew-scoped per call.
            Exclude = ReadStringArray(section.GetSection("Exclude"), fallback:
                ["node_modules", "dist", ".git", "bin", "obj"]),
            RootAlias = section["RootAlias"] ?? "",
            IndexMode = ParseEnum(section["IndexMode"], RaggableTreeIndexMode.Frozen),
            EnrichWithLlm = section.GetValue("EnrichWithLlm", defaultValue: false),
            IncludeStatements = section.GetValue("IncludeStatements", defaultValue: false),
            Embedding = new EmbeddingOptions
            {
                Provider = provider,
                // Empty → each provider's own default model (LocalSmartComponents → bge-micro-v2).
                Model = embeddingSection["Model"] ?? "",
                ApiKey = embeddingSection["ApiKey"],
                BaseUrl = embeddingSection["BaseUrl"] is { } embedBaseUrl ? new Uri(embedBaseUrl) : null,
                Dimensions = int.TryParse(embeddingSection["Dimensions"], out var d) ? d : null,
                MaxTextChars = int.TryParse(embeddingSection["MaxTextChars"], out var mc) ? mc : null,
            },
        };

        // Pre-register the on-device provider explicitly when selected, so resolution never relies
        // on the reflection fallback (which can miss a not-yet-loaded assembly).
        if (provider == EmbeddingProviderKind.LocalSmartComponents)
            services.AddOrkeonLocalEmbeddings();

        services.AddRaggableTreeWithLogging(options);
        services.AddRaggableTreeTools();
    }

    private static ImmutableArray<string> ReadStringArray(
        IConfigurationSection section,
        ImmutableArray<string>? fallback = null)
    {
        if (!section.Exists()) return fallback ?? [];
        var items = section.GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToImmutableArray();
        return items.Length > 0 ? items : (fallback ?? []);
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
    }

    private static void RegisterLlmProvider(HostBuilderContext context, IServiceCollection services)
    {
        var llmSection = context.Configuration.GetSection(ConfigurationKeys.LlmSection);
        // No section → no provider registration; the echo fallback is announced once per
        // host build by WarnIfLlmNotConfigured (no logger exists yet at this point).
        if (!llmSection.Exists()) return;

        // The one default, not a literal: LlmConfig, AgentBuilder and Studio's presets all
        // read LlmDefaults.DefaultModelName, so a hardcoded model here gave an appsettings
        // whose Llm section omits Model a different model from every other entry point.
        var llmConfig = LlmConfig.Create(
            llmSection["Model"] ?? LlmDefaults.DefaultModelName) with
        {
            BaseUrl = llmSection["BaseUrl"] is { } llmBaseUrl ? new Uri(llmBaseUrl) : null,
#pragma warning disable CS0618
            ApiKey = llmSection["ApiKey"],
#pragma warning restore CS0618
            // Invariant parse: configuration values are written invariant ("0.7"), and a
            // culture-sensitive read turns that into 7 on a comma-decimal locale (fr-FR).
            Temperature = double.TryParse(llmSection["Temperature"], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : 0.7,
            // Absent = not pinned: the provider sends the model's documented maximum (LLM-10).
            MaxTokens = int.TryParse(llmSection["MaxTokens"], out var m) ? m : null,
            TimeoutSeconds = int.TryParse(llmSection["TimeoutSeconds"], out var ts) ? ts : 30,
            Thinking = ReadThinkingConfig(llmSection),
        };
        if (int.TryParse(llmSection["MaxRetries"], out var maxRetries))
            llmConfig = llmConfig with { MaxRetries = Math.Max(0, maxRetries) };

        services.AddSingleton<IBasicLlmProvider>(sp =>
        {
            var factory = sp.GetRequiredService<ILlmProviderFactory>();
            return factory.Create(llmConfig);
        });

        static LlmThinkingConfig? ReadThinkingConfig(IConfigurationSection llmSection)
        {
            // Llm:Thinking:{Enabled,Effort} — forwarded to thinking-capable providers
            // (DeepSeek, Z.AI GLM) as the `thinking` block + `reasoning_effort` field.
            var thinkingSection = llmSection.GetSection(ConfigurationKeys.ThinkingSection);
            if (!thinkingSection.Exists()) return null;
            return new LlmThinkingConfig
            {
                Enabled = bool.TryParse(thinkingSection["Enabled"], out var enabled) ? enabled : null,
                Effort = thinkingSection["Effort"],
            };
        }
        services.AddSingleton<IChatClient>(sp =>
        {
            var basicProvider = sp.GetRequiredService<IBasicLlmProvider>();
            var textParser = sp.GetService<Application.Interfaces.LLM.IToolCallParser>();
            if (basicProvider is LlmProviderAdapter adapter)
                return new LlmProviderToChatClientAdapter(adapter.UnderlyingProvider, llmConfig, textParser);
            throw new InvalidOperationException(
                $"LLM provider for model '{llmConfig.Model}' does not expose ILlmProvider.");
        });
    }
}
