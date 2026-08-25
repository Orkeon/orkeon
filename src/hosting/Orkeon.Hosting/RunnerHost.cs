using System.Collections.Immutable;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.EventHub.DependencyInjection;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
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
    public const string LlmNotConfiguredMessage =
        "No `Llm` section configured — falling back to the echo provider (`<undefined-llm>`). " +
        "Run `orkeon init` to create a configuration, or set `ORKEON_Llm__BaseUrl` / `ORKEON_Llm__Model`.";

    /// <summary>
    /// Builds a fully-configured host with all Orkeon services.
    /// </summary>
    /// <param name="settingsPath">Resolved appsettings.json path (nullable).</param>
    /// <param name="cliMounts">CLI --mount arguments.</param>
    /// <param name="allowExternalMounts">
    /// When <c>true</c>, mount base paths are added to the PathSecurity whitelist
    /// (<c>AdditionalAllowedDirectories</c>), allowing mounts from directories outside
    /// the workspace root.
    /// </param>
    /// <param name="llmLogVirtualPath">
    /// When non-null, enables LLM exchange logging under this <b>virtual</b> path — the caller
    /// is responsible for having mounted it (the runners pass
    /// <see cref="RunnerMounts.LlmLogVirtualRoot"/> and mount it internally). All HTTP
    /// request/response headers and payloads are captured as JSON Lines (.jsonl) files,
    /// written through <c>IFileSystemService</c> like every other file the framework touches.
    /// </param>
    /// <param name="internalMounts">
    /// Mounts registered with <c>MountVisibility.Internal</c>: resolvable by the VFS, absent
    /// from <c>GetAvailableMounts()</c> and therefore invisible to agents. Same grammar as
    /// <paramref name="cliMounts"/>.
    /// </param>
    /// <param name="configureLogging">Optional callback to customize logging (default: Console + Information).</param>
    /// <param name="configureServices">Optional callback to register additional services.</param>
    /// <param name="configureBuilder">
    /// Optional callback on the builder itself, before it is built. The service host uses it
    /// for <c>UseSystemd()</c> and <c>UseWindowsService()</c>: a long-lived daemon needs to
    /// tell its supervisor it is up, and that is decided on the builder, not in the services.
    /// </param>
    public static IHost Build(
        string? settingsPath,
        IReadOnlyList<string> cliMounts,
        bool allowExternalMounts = false,
        string? llmLogVirtualPath = null,
        IReadOnlyList<string>? internalMounts = null,
        Action<HostBuilderContext, ILoggingBuilder>? configureLogging = null,
        Action<HostBuilderContext, IServiceCollection>? configureServices = null,
        Action<IHostBuilder>? configureBuilder = null)
    {
        var internals = internalMounts ?? [];
        var builder = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, b) =>
                ConfigureAppConfiguration(b, settingsPath, cliMounts, internals, allowExternalMounts))
            .ConfigureServices((context, services) =>
                ConfigureRunnerServices(context, services, llmLogVirtualPath, configureLogging, configureServices));

        configureBuilder?.Invoke(builder);

        var host = builder.Build();

        WarnIfLlmNotConfigured(host);
        return host;
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

        var llmSection = configuration.GetSection("Llm");
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

    private static void ConfigureAppConfiguration(
        IConfigurationBuilder builder,
        string? settingsPath,
        IReadOnlyList<string> cliMounts,
        IReadOnlyList<string> internalMounts,
        bool allowExternalMounts)
    {
        if (settingsPath != null && File.Exists(settingsPath))
            builder.AddJsonFile(settingsPath, optional: true);

        builder.AddEnvironmentVariables("ORKEON_");

        if (cliMounts.Count == 0 && internalMounts.Count == 0)
            return;

        var mountOverrides = new Dictionary<string, string?>();
        for (var i = 0; i < cliMounts.Count; i++)
            mountOverrides[$"Orkeon:FileSystem:Mounts:{i}"] = cliMounts[i];

        // Infrastructure mounts ride their own key so they can carry Internal visibility
        // (the mount-string grammar has no room for it). Configuration rather than a hosted
        // service: the runners never start the host, so an IHostedService would silently
        // never fire under --validate or --list-tools.
        for (var i = 0; i < internalMounts.Count; i++)
            mountOverrides[$"Orkeon:FileSystem:InternalMounts:{i}"] = internalMounts[i];

        // When --allow-external-mounts is set, whitelist each mount's base path
        // in PathSecurity:AdditionalAllowedDirectories so PathValidator accepts them.
        if (allowExternalMounts)
        {
            var whitelisted = 0;
            foreach (var mount in cliMounts.Concat(internalMounts))
            {
                var basePath = ExtractMountBasePath(mount);
                if (basePath != null)
                    mountOverrides[$"PathSecurity:AdditionalAllowedDirectories:{whitelisted++}"] = basePath;
            }
        }

        builder.AddInMemoryCollection(mountOverrides);
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

        // Runners load crews from user-authored YAML/TS: a tool referenced by a crew but
        // absent from the registry is almost always a typo or a missing registration, not an
        // intentional degrade. Fail loading with an explicit "unknown tool(s): …; available: …"
        // message rather than silently dropping the tool (the library default stays lenient).
        // A host can still opt back out via "Orkeon:CrewFactory:StrictTools": false.
        var strictTools = context.Configuration.GetValue(
            "Orkeon:CrewFactory:StrictTools", defaultValue: true);
        services.Configure<Orkeon.Infrastructure.Configuration.CrewFactoryOptions>(
            o => o.StrictTools = strictTools);

        // Per-tool-call permission gate (exp 07 F2) — config opt-in:
        // Orkeon:Security:PermissionGate:Enabled = true. No-op otherwise.
        services.AddOrkeonPermissionGate(context.Configuration);

        // Register all standard tools
        services.AddOrkeonFileSystemTools();
        services.AddOrkeonDataTools();
        services.AddOrkeonWebTools();
        services.AddOrkeonCodeTools();
        services.AddOrkeonAbstractionTools();
        // exp 07 session primitives (session_store/session_snip/token_budget/
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
        if (HasMounts("Orkeon:FileSystem:Mounts") || HasMounts("Orkeon:FileSystem:InternalMounts"))
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
    /// Extracts the physical base path from a mount string in Docker-style format.
    /// Handles Windows drive letters (e.g. <c>C:\Temp\src:/src:ro</c>).
    /// </summary>
    private static string? ExtractMountBasePath(string mountString)
    {
        // Docker-style: <physical>:<virtual>:<rights>
        // Windows drive letter: first colon at index 1 is the drive letter (e.g. C:)
        var parts = mountString.Split(':');

        // Windows: C:\path:/virtual:ro → parts = ["C", "\path", "/virtual", "ro"]
        if (parts.Length >= 3 && parts[0].Length == 1 && char.IsLetter(parts[0][0]))
        {
            var physicalPath = parts[0] + ":" + parts[1]; // Rejoin drive letter
            return Path.GetFullPath(physicalPath);
        }

        // Unix: /physical/path:/virtual:ro → parts = ["/physical/path", "/virtual", "ro"]
        if (parts.Length >= 2)
        {
            return Path.GetFullPath(parts[0]);
        }

        return null;
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
        var llmSection = context.Configuration.GetSection("Llm");
        // No section → no provider registration; the echo fallback is announced once per
        // host build by WarnIfLlmNotConfigured (no logger exists yet at this point).
        if (!llmSection.Exists()) return;

        var llmConfig = LlmConfig.Create(
            llmSection["Model"] ?? "gpt-4") with
        {
            BaseUrl = llmSection["BaseUrl"] is { } llmBaseUrl ? new Uri(llmBaseUrl) : null,
#pragma warning disable CS0618
            ApiKey = llmSection["ApiKey"],
#pragma warning restore CS0618
            // Invariant parse: configuration values are written invariant ("0.7"), and a
            // culture-sensitive read turns that into 7 on a comma-decimal locale (fr-FR).
            Temperature = double.TryParse(llmSection["Temperature"], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : 0.7,
            MaxTokens = int.TryParse(llmSection["MaxTokens"], out var m) ? m : 4096,
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
            var thinkingSection = llmSection.GetSection("Thinking");
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
