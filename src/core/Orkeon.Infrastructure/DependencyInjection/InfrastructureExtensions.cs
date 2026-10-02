using Microsoft.Extensions.AI;
using Orkeon.Domain.SharedKernel;
using ILlmProvider = Orkeon.Domain.SharedKernel.ILlmProvider;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Memory;
using Orkeon.Domain.Task;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Infrastructure.Serialization;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Tools.Security;
using Orkeon.Domain.FileSystem;
using Orkeon.Application.Memory;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Crew;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Security;
using Orkeon.Infrastructure.Security.Secrets;
using Orkeon.Infrastructure.Security.Sinks;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Infrastructure.Parsing;
using Orkeon.Domain.Crew.Interfaces;
using Orkeon.Infrastructure.OutputParsing;
using Orkeon.Infrastructure.OutputParsing.Validation;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.LLMs.Embeddings;
using Orkeon.Infrastructure.Persistence;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Crew;
using Orkeon.Infrastructure.Persistence.Memory;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Consensus;
using Orkeon.Infrastructure.CostTracking;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Telemetry;
using Orkeon.Infrastructure.Evaluation;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Communication;
using Orkeon.Infrastructure.Sandbox;
using Orkeon.Application.Services.Security;
using Orkeon.Infrastructure.Security.Guards;
using Orkeon.Tools.Abstractions.Security;
using Orkeon.Infrastructure.Checkpointing;
using Orkeon.Infrastructure.DomainEvents;
using Orkeon.Infrastructure.Orchestration;
using Orkeon.Infrastructure.Templating;
using Orkeon.Application.Interfaces.Services;

namespace Orkeon.Infrastructure.DependencyInjection;

/// <summary>
/// Simplified extension methods for registering infrastructure services.
/// </summary>
public static class InfrastructureExtensions
{
    /// <summary>
    /// Adds essential infrastructure services. The body simply chains themed sub-methods
    /// (core plumbing, LLMs + memory, repositories, stubs, security, output validation,
    /// and feature extensions) to keep cognitive complexity low.
    /// </summary>
    public static IServiceCollection AddOrkeonInfrastructure(this IServiceCollection services)
    {
        services.AddOrkeonCorePlumbing();
        services.AddOrkeonLlmAndMemoryBaseline();
        services.AddOrkeonSerializationAndEmbeddings();
        services.AddOrkeonAgentRuntime();
        services.AddOrkeonInterfaceStubs();
        services.AddOrkeonRepositoriesAndStrategies();
        services.AddOrkeonSecurityCore();
        services.AddOrkeonNativeToolCalling();
        services.AddOrkeonOutputValidation();
        services.AddOrkeonFeatureModules();
        return services;
    }

    /// <summary>
    /// Adds Orkeon infrastructure with OpenTelemetry telemetry support.
    /// Reads configuration from the "Telemetry" section.
    /// </summary>
    public static IServiceCollection AddOrkeonInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOrkeonInfrastructure();
        services.AddOrkeonTelemetry(configuration);

        // The evaluation suite is registered by the overload above; this call binds the
        // "Evaluation" section (EnableLlmJudge) and registers nothing twice (GAP-15).
        services.AddOrkeonEvaluation(configuration);

        // === Phase 4: Standards & Interoperability ===
        services.AddOrkeonMcp(configuration);
        services.AddOrkeonVectorSearch(configuration);

        // RAG subsystem (opt-in, RAG-02): AddOrkeonRag (Orkeon.Rag.DependencyInjection)
        // and AddOrkeonRagTools (Orkeon.Tools.Rag) are intentionally NOT registered by
        // default. Hosts that need rag_search / document ingestion enable them explicitly.
        // See docs/reference/opt-in-subsystems.md.

        // === Phase 10: Vector Store Providers (N5) ===
        // The concrete ChromaDB / Pinecone providers stay resolvable when their section exists;
        // they are the factory's shared instances, connected from those sections (GAP-08).
        if (configuration.GetSection(Memory.ChromaDb.ChromaDbOptions.SectionName).Exists())
            services.AddOrkeonChromaDb(configuration);
        if (configuration.GetSection(Memory.Pinecone.PineconeOptions.SectionName).Exists())
            services.AddOrkeonPinecone(configuration);

        // === Execution-state persistence (R3.8) ===
        // Opt-in via configuration: durable persistence of crew execution states
        // (crash recovery) activates only when the section is present and Enabled=true,
        // and requires an IStateStore registration (checkpointing extensions).
        // Default remains in-memory only.
        if (configuration.GetSection("Orkeon:ExecutionState:Persistence").Exists())
            services.AddCrewExecutionStatePersistence(configuration);

        // Opt-in subsystems (R4.9 — MORT-003): the A2A server (AddOrkeonA2A), the
        // monitoring backend (AddOrkeonMonitoring), and multi-modal content validation
        // (AddOrkeonMultiModal) are intentionally NOT registered by default — no
        // production code path consumes them yet. Hosts enable them explicitly.
        // See docs/reference/opt-in-subsystems.md.

        return services;
    }

    /// <summary>
    /// The file system a component that writes to an INTERNAL mount must use. Falls back to
    /// the plain service when no privileged view is registered — a host that wires its own
    /// <see cref="IFileSystemService"/> instead of calling <c>AddOrkeonFileSystem</c> keeps
    /// exactly the behaviour it had.
    /// </summary>
    private static IFileSystemService PrivilegedFileSystem(IServiceProvider sp) =>
        sp.GetService<FileSystem.PrivilegedFileSystemAccess>()?.FileSystem
        ?? sp.GetRequiredService<IFileSystemService>();

    private static IServiceCollection AddOrkeonCorePlumbing(this IServiceCollection services)
    {
        // Add path security options (configurable via appsettings.json "PathSecurity" section)
        // Registered first because IPathValidator is needed by many downstream services.
        services.AddOptions<PathSecurityOptions>()
            .BindConfiguration("PathSecurity");

        // Add path validator — TryAdd so a host override registered before
        // AddOrkeonInfrastructure() is respected regardless of registration order.
        // Orkeon's own default is still registered when the host provides none.
        services.TryAddSingleton<IPathValidator, PathValidator>();

        // Domain event dispatcher — scoped, so the IDomainEventHandler<T> it resolves (registered
        // scoped by AddOrkeonApplication) come from the caller's scope, never the root provider.
        services.AddScoped<Domain.SharedKernel.Events.IDomainEventDispatcher, DomainEventDispatcher>();

        // Orchestration services (moved from Application — R16)
        services.AddScoped<SequentialCrewOrchestrator>();
        services.AddScoped<ICrewOrchestrationService>(sp => sp.GetRequiredService<SequentialCrewOrchestrator>());
        services.AddSingleton<ICrewExecutionStateManager, ScopedCrewExecutionStateManager>();

        // Template engine (moved from Application — R16)
        services.AddSingleton<ITemplateEngine, TemplateEngine>();

        // Add HTTP client factory
        services.AddHttpClient();

        return services;
    }

    private static IServiceCollection AddOrkeonLlmAndMemoryBaseline(this IServiceCollection services)
    {
        // Add LLM providers
        services.AddSingleton<ILlmProviderFactory, LlmProviderFactory>();

        // The host's named LLM profiles (GAP-17): the default provider plus every profile
        // registered by AddOrkeonLlmProfile(s). Always present, so a crew naming a profile the
        // host does not define fails its load with the list of known ones.
        services.TryAddSingleton<ILlmProfileRegistry, LLMs.Profiles.LlmProfileRegistry>();

        // Memory providers (GAP-08). One host section per provider supplies its connection
        // (Orkeon:Redis, Orkeon:Sqlite, Orkeon:ChromaDb, Orkeon:Pinecone, Orkeon:LanceDb); the
        // factory hands out one shared instance per type. Memory:Provider only chooses the TYPE
        // of the application-wide provider (unset → in-memory), exactly like a crew's
        // memoryProvider: and Orkeon:Rag:Provider.
        services.AddOrkeonMemoryProviderFactory();
        services.BindMemorySection<Memory.RedisMemoryOptions>(Memory.RedisMemoryOptions.SectionName);
        services.BindMemorySection<Memory.Sqlite.SqliteMemoryOptions>(Memory.Sqlite.SqliteMemoryOptions.SectionName);
        services.BindMemorySection<Memory.ChromaDb.ChromaDbOptions>(Memory.ChromaDb.ChromaDbOptions.SectionName);
        services.BindMemorySection<Memory.Pinecone.PineconeOptions>(Memory.Pinecone.PineconeOptions.SectionName);
        services.BindMemorySection<Memory.LanceDb.LanceDbOptions>(Memory.LanceDb.LanceDbOptions.SectionName);
        services.AddSingleton<Orkeon.Domain.Memory.IMemoryProvider>(sp =>
            sp.GetRequiredService<Application.Interfaces.Ports.IMemoryProviderFactory>()
                .GetProvider(sp.GetService<IConfiguration>()?[VectorStoreExtensions.MemoryProviderKey] ?? string.Empty));

        return services;
    }

    /// <summary>
    /// Binds a memory provider's section from the container's <see cref="IConfiguration"/> when
    /// one is registered; without configuration the provider keeps its defaults, as the in-memory
    /// baseline always has.
    /// </summary>
    private static void BindMemorySection<TOptions>(this IServiceCollection services, string sectionName)
        where TOptions : class
    {
        services.AddOptions<TOptions>().Configure<IServiceProvider>((options, sp) =>
            sp.GetService<IConfiguration>()?.GetSection(sectionName).Bind(options));
    }

    private static IServiceCollection AddOrkeonSerializationAndEmbeddings(this IServiceCollection services)
    {
        // Add component serializer (typed pipeline Dict↔Model conversion).
        // TryAdd so a serializer registered by the host before AddOrkeonInfrastructure() wins,
        // regardless of registration order. The static Domain default is set non-destructively
        // (TrySetDefaultSerializer) so a serializer already posed by the host is never overwritten.
        services.TryAddSingleton<Domain.Common.IComponentSerializer>(JsonComponentSerializer.Instance);
        Domain.Common.ComponentBase.TrySetDefaultSerializer(JsonComponentSerializer.Instance);

        // Add serialization services (temporary interfaces)
        services.AddSingleton<IYamlSerializer, YamlDotNetSerializer>();
        services.AddSingleton<ICsvSerializer, CsvHelperSerializer>();
        services.AddSingleton<IMarkdownParser, MarkdownParser>();

        // Execution plan parser (extracted from CrewPlanner — AUDIT-P3-04)
        services.TryAddSingleton<IExecutionPlanParser, ExecutionPlanParser>();

        // Application-port embedding service (consumed by EmbeddingBasedSelectionStrategy
        // and search tools). Since RAG-02/C5 the hash-based SimpleEmbeddingService is gone:
        // the default adapts the IEmbeddingProvider port, whose resolution is semantic-first
        // (local BGE → remote Orkeon:Embeddings → fail-fast at first use, RAG-01/C4).
        // TryAdd so a service registered by the host earlier wins.
        services.TryAddSingleton<Application.Interfaces.Ports.IEmbeddingService, EmbeddingProviderServiceAdapter>();

        // Domain-side embedding service: same chain, adapted to the Domain interface.
        services.TryAddSingleton<Domain.Memory.IEmbeddingService>(sp =>
            new DomainEmbeddingServiceAdapter(
                sp.GetRequiredService<Application.Interfaces.Ports.IEmbeddingService>()));

        return services;
    }

    private static IServiceCollection AddOrkeonAgentRuntime(this IServiceCollection services)
    {
        // Add async agent communication service
        services.AddSingleton<Orkeon.Application.Interfaces.Services.IAgentCommunicationService, Communication.AsyncAgentCommunicationService>();

        // Add Manager Agent for hierarchical process
        // Prefer IChatClient when available, fall back to IBasicLlmProvider-only constructor
        services.AddScoped<IManagerAgent>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<LlmBasedManager>>();
            var llmProvider = sp.GetRequiredService<IBasicLlmProvider>();
            var chatClient = sp.GetService<IChatClient>();
            if (chatClient != null)
            {
                return new LlmBasedManager(logger, llmProvider, chatClient);
            }
            return new LlmBasedManager(logger, llmProvider);
        });

        // Add Memory Scope (no-op default — strategies require it)
        services.TryAddScoped<Application.Interfaces.Ports.IMemoryScope>(_ => Application.Context.NullMemoryScope.Instance);

        return services;
    }

    private static IServiceCollection AddOrkeonInterfaceStubs(this IServiceCollection services)
    {
        // === AUDIT-P2-07: Register previously missing critical interfaces ===
        // These registrations prevent NullReferenceException when interfaces are resolved.
        // Stubs log warnings on first use so they're visible in production.

        // IToolRegistry — required by CrewFactory, McpServer, McpToolProvider.
        // Seeded from every IBaseTool in the container, so a host that registers a tool in DI
        // resolves it by name from a YAML crew without a registry of its own (GAP-11).
        services.TryAddSingleton<Domain.Tools.IToolRegistry, Tools.ToolRegistry>();

        // IKnowledgeStore (Domain) — no-op stub; real implementation requires a vector store
        services.TryAddSingleton<Domain.Knowledge.IKnowledgeStore, Stubs.InMemoryKnowledgeStore>();

        // IMemorySystem (Domain) — in-memory agent memory system
        services.TryAddSingleton<Domain.Memory.IMemorySystem, Stubs.InMemoryMemorySystem>();

        // IEmbeddingProvider (Application) — semantic-first default resolution (RAG-01/C4):
        //   1. Analysis-side provider in the container (AddOrkeonLocalEmbeddings → BGE local,
        //      RaggableTree providers) → wrapped in AnalysisEmbeddingProviderAdapter,
        //   2. Orkeon:Embeddings configuration → remote provider (Ollama / OpenAI-compatible),
        //   3. otherwise fail-fast at FIRST USE (UnconfiguredEmbeddingProvider — actionable
        //      InvalidOperationException, never at container build time).
        // The hash stub (Stubs.HashBasedEmbeddingProvider) is no longer resolved implicitly
        // anywhere — it is an explicit, opt-in test double only.
        services.TryAddSingleton<Application.Interfaces.Ports.IEmbeddingProvider>(
            DefaultEmbeddingProviderResolver.Resolve);

        // ILlmCache — no-op cache that always misses
        services.TryAddSingleton<Application.Interfaces.Infrastructure.Caching.ILlmCache, Stubs.NullLlmCache>();

        // IYamlDiffService — no-op YAML diff
        services.TryAddSingleton<Application.Interfaces.Infrastructure.IYamlDiffService, Stubs.NullYamlDiffService>();

        // ITemplateInstantiator — throws NotSupportedException if actually used
        services.TryAddSingleton<Application.Interfaces.Ports.ITemplateInstantiator, Stubs.NullTemplateInstantiator>();

        // === Agent selection (R3.5) ===
        // Register both real selection strategies so they can be resolved by configuration.
        services.TryAddSingleton<Application.Services.AgentSelection.EmbeddingBasedSelectionStrategy>();
        services.TryAddSingleton<Application.Services.AgentSelection.SkillMatchingSelectionStrategy>();

        // IAgentSelectionService — driven by OrkeonApplicationOptions.AgentSelectionStrategy.
        //   Embedding -> EmbeddingBasedSelectionStrategy (semantic; requires a real embedding provider)
        //   Skill     -> SkillMatchingSelectionStrategy (lexical Jaccard)
        //   FirstFit  -> SimpleAgentSelectionService (explicit safe fallback, warn-once)
        services.TryAddSingleton<IAgentSelectionService>(sp =>
        {
            var options = sp.GetService<IOptions<OrkeonApplicationOptions>>();
            var kind = options?.Value?.AgentSelectionStrategy
                ?? Application.Configuration.AgentSelectionStrategyKind.FirstFit;

            switch (kind)
            {
                case Application.Configuration.AgentSelectionStrategyKind.Embedding:
                    return new Application.Services.AgentSelection.StrategyAgentSelectionService(
                        sp.GetRequiredService<Application.Services.AgentSelection.EmbeddingBasedSelectionStrategy>());

                case Application.Configuration.AgentSelectionStrategyKind.Skill:
                    return new Application.Services.AgentSelection.StrategyAgentSelectionService(
                        sp.GetRequiredService<Application.Services.AgentSelection.SkillMatchingSelectionStrategy>());

                default:
                    return new Stubs.SimpleAgentSelectionService(
                        sp.GetRequiredService<ILogger<Stubs.SimpleAgentSelectionService>>());
            }
        });

        // ITaskDelegator — stub that denies all delegation requests
        services.TryAddSingleton<Domain.Delegation.ITaskDelegator, Stubs.NullTaskDelegator>();

        // IAgentExecutionService — stub (consumer should override with real implementation)
        services.TryAddScoped<IAgentExecutionService, Stubs.NullAgentExecutionService>();

        return services;
    }

    private static IServiceCollection AddOrkeonRepositoriesAndStrategies(this IServiceCollection services)
    {
        // Agent delegation provider (scoped — one instance per crew execution)
        services.AddScoped<AgentDelegationToolsProvider>();

        // Who runs a task that declares no agent. This is the call site
        // OrkeonApplicationOptions.AgentSelectionStrategy was missing: the option, the two
        // real strategies and IAgentSelectionService all existed, and nothing in the engine
        // ever asked them anything.
        services.TryAddSingleton<Crew.Strategies.TaskAgentSelector>();

        // The collaborators every strategy shares, resolved once per scope so a strategy
        // constructor only names what its own mode adds on top of them.
        services.TryAddScoped<Crew.Strategies.CrewStrategyDependencies>();

        // Add Process Strategies
        services.AddScoped<SequentialProcessStrategy>();
        services.AddScoped<HierarchicalProcessStrategy>();
        services.AddScoped<ParallelProcessStrategy>();
        services.AddScoped<GraphProcessStrategy>();
        services.AddScoped<AutonomousProcessStrategy>();
        services.AddScoped<IAgentChannel, InMemoryAgentChannel>();

        // Add Process Strategy Factory
        services.AddScoped<IProcessStrategyFactory, ProcessStrategyFactory>();

        // Unit of Work (coupled with domain event dispatch)
        services.AddScoped<IUnitOfWork, InMemoryUnitOfWork>();

        // In-memory repository implementations (Scoped — aligned with IUnitOfWork for future DB migration)
        // IAgentRepository is TryAdd (ANT-019): when AddOrkeonA2A() ran first, its
        // SharedStoreAgentRepository upgrade must keep winning regardless of call order —
        // a later AddScoped would silently bring back the per-scope (empty) directory.
        services.TryAddScoped<IAgentRepository, InMemoryAgentRepository>();
        services.AddScoped<ICrewRepository, InMemoryCrewRepository>();
        services.AddScoped<ITaskRepository, InMemoryTaskRepository>();
        services.AddScoped<IAgentMemoryStoreRepository, InMemoryAgentMemoryStoreRepository>();

        // Streaming agent execution service — registered by default so that
        // ICrewOrchestrationService.KickoffStreamingAsync streams AgentThought-level
        // (tool-call granular) events instead of degrading to per-task replay. It resolves
        // the host's IChatClient (AddOrkeonLlmProvider registers one), which the infrastructure
        // no longer fakes when the host registers no model (GAP-29).
        services.AddScoped<Orkeon.Application.Interfaces.Services.IStreamingAgentExecutionService, StreamingAgentExecutionService>();

        // Agent lifecycle manager (kill switch)
        services.AddSingleton<Orkeon.Application.Interfaces.Services.IAgentLifecycleManager, AgentLifecycleManager>();

        return services;
    }

    private static IServiceCollection AddOrkeonSecurityCore(this IServiceCollection services)
    {
        // === Phase 2: Security Core ===

        // Prompt injection defense (P0-5)
        services.AddOptions<PromptSecurityOptions>()
            .BindConfiguration("Security:Prompt");
        services.AddOptions<ToolResultSecurityOptions>()
            .BindConfiguration("Security:ToolResults");
        services.AddSingleton<IPromptSanitizer, PromptSanitizer>();
        // Applied to every tool result by the tool-invocation pipeline (GAP-09).
        services.AddSingleton<IToolResultSanitizer, ToolResultSanitizer>();

        // SSRF protection (P0-7)
        services.AddOptions<UrlSecurityOptions>()
            .BindConfiguration("Security:Url");
        services.TryAddSingleton<IUrlValidator, UrlValidator>();
        services.AddSingleton<HttpHeaderSanitizer>();

        // Rate limiting (P0-9) — LLM-level rate limiting only.
        // Tool-level rate limiting and token budget tracking are dormant subsystems
        // (no production consumer) and are opt-in via AddOrkeonToolRateLimiting()
        // (R4.9 — see docs/reference/opt-in-subsystems.md).
        services.AddOptions<RateLimitingOptions>()
            .BindConfiguration("RateLimiting");
        services.AddSingleton<ILlmRateLimiter, LlmRateLimiter>();

        // Audit trail (P1-2)
        services.AddOptions<AuditOptions>()
            .BindConfiguration("Security:Audit");
        services.AddSingleton<IAuditSink, StructuredLogAuditSink>();
        services.AddSingleton<IAuditSink, InMemoryAuditSink>();
        services.AddSingleton<IAuditLogger, AuditLogger>();

        // Secure credentials (P1-8)
        // LogSanitizer is a static class - used directly, no DI needed
        services.AddOptions<VaultOptions>()
            .BindConfiguration("Security:Vault");
        services.TryAddSingleton<ISecretProvider>(BuildChainedSecretProvider);

        return services;
    }

    private static ISecretProvider BuildChainedSecretProvider(IServiceProvider sp)
    {
        var config = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        var logger = sp.GetRequiredService<ILogger<ChainedSecretProvider>>();
        var vaultOptions = sp.GetService<IOptions<VaultOptions>>()?.Value;

        var providers = new List<ISecretProvider>
        {
            new EnvironmentSecretProvider("ORKEON_"),
            new ConfigurationSecretProvider(config, "Secrets"),
        };

        AppendOptionalVaultProviders(providers, vaultOptions, sp);

        return new ChainedSecretProvider(providers, logger);
    }

    private static void AppendOptionalVaultProviders(
        List<ISecretProvider> providers,
        VaultOptions? vaultOptions,
        IServiceProvider sp)
    {
        // Azure Key Vault (if configured)
        if (vaultOptions?.AzureKeyVaultUri is not null)
        {
            providers.Add(new AzureKeyVaultSecretProvider(
                vaultOptions.AzureKeyVaultUri,
                sp.GetRequiredService<ILogger<AzureKeyVaultSecretProvider>>(),
                vaultOptions.CacheTtl));
        }

        // AWS Secrets Manager (if configured)
        if (vaultOptions?.UseAwsSecretsManager == true)
        {
            var awsClient = sp.GetService<Amazon.SecretsManager.IAmazonSecretsManager>();
            if (awsClient != null)
            {
                providers.Add(new AwsSecretsManagerProvider(
                    awsClient,
                    sp.GetRequiredService<ILogger<AwsSecretsManagerProvider>>(),
                    vaultOptions.CacheTtl));
            }
        }

        // DPAPI Windows (if configured)
        if (OperatingSystem.IsWindows() && !string.IsNullOrEmpty(vaultOptions?.DpapiSecretsDirectory))
        {
            // The secrets directory is created lazily on first use by DpapiSecretProvider,
            // so no blocking sync-over-async initialization is required at composition time.
            var dpapi = new DpapiSecretProvider(
                sp.GetRequiredService<IFileSystemService>(),
                vaultOptions.DpapiSecretsDirectory,
                sp.GetRequiredService<ILogger<DpapiSecretProvider>>());
            providers.Add(dpapi);
        }
    }

    /// <summary>
    /// What a container that registers no model says at its first LLM resolution (GAP-29).
    /// </summary>
    internal const string NoLlmProviderRegistered =
        "No LLM provider is registered. Register the host's model with services.AddOrkeonLlmProvider(sp => …, baseConfig), " +
        "which serves it as ILlmProvider, IBasicLlmProvider and IChatClient over one instance; the orkeon runners and " +
        "orkeon-repl register theirs from the Llm section, or the echo provider when there is none.";

    private static IServiceCollection AddOrkeonNativeToolCalling(this IServiceCollection services)
    {
        // === Phase 2b: Native Tool Calling (P1-TC-06) ===
        // The host's ILlmProvider when it registered only an IBasicLlmProvider over one (the
        // runners register their configured provider that way), and IToolCallingStrategy for the
        // OpenAI-compatible providers.
        //
        // No model of its own (GAP-29): the IBasicLlmProvider and IChatClient fallbacks this
        // registered built a second provider from an empty configuration — OpenAI's endpoint, no
        // key — whose every call answered "API key is required"; orkeon-repl served its chat
        // client that way. A host registers its model (AddOrkeonLlmProvider), and one that does
        // not is told so at its first LLM resolution.
        services.TryAddSingleton<ILlmProvider>(sp => sp.GetService<IBasicLlmProvider>() switch
        {
            LLMs.LlmProviderAdapter adapter => adapter.UnderlyingProvider,
            null => throw new InvalidOperationException(NoLlmProviderRegistered),
            var other => throw new InvalidOperationException(
                $"The registered IBasicLlmProvider ({other.GetType().FullName}) exposes no ILlmProvider. " +
                "Register the model with services.AddOrkeonLlmProvider(sp => …, baseConfig), which serves the three surfaces over one instance."),
        });

        services.TryAddSingleton<Application.Interfaces.LLM.IToolCallingStrategy>(sp =>
        {
            var parserLogger = sp.GetRequiredService<ILogger<LLMs.ToolCalling.OpenAIToolCallParser>>();
            return new LLMs.ToolCalling.OpenAIToolCallingStrategy(parserLogger);
        });

        // Text-based fallback tool call parser (for models that don't return native tool_calls)
        services.TryAddSingleton<Application.Interfaces.LLM.IToolCallParser>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<LLMs.ToolCalling.TextFallbackToolCallParser>>();
            return new LLMs.ToolCalling.TextFallbackToolCallParser(logger);
        });

        return services;
    }

    private static IServiceCollection AddOrkeonOutputValidation(this IServiceCollection services)
    {
        // === Phase 3: Output Validation (P1-7) ===

        // Output parser factory
        services.TryAddSingleton<IOutputParserFactory, OutputParserFactory>();

        // Output validators (registered individually and as collection)
        services.AddSingleton<IOutputValidator, LengthValidator>();
        services.AddSingleton<IOutputValidator, FormatValidator>();
        services.AddSingleton<IOutputValidator, SchemaValidator>();
        services.AddSingleton<IOutputValidator, CompletenessValidator>();
        services.AddSingleton<IOutputValidator, ContentSafetyValidator>();
        services.AddSingleton<IOutputValidator, PiiDetectionValidator>();

        // Validation pipeline (pre-loaded with all registered validators)
        services.TryAddSingleton<IOutputValidationPipeline>(sp =>
        {
            var validators = sp.GetServices<IOutputValidator>();
            return new OutputValidationPipeline(validators);
        });

        // === Phase 3: Telemetry (P0-8) ===
        // Register OrkeonMetrics singleton (always available, even without full OTel pipeline)
        services.TryAddSingleton<OrkeonMetrics>();

        return services;
    }

    private static IServiceCollection AddOrkeonFeatureModules(this IServiceCollection services)
    {
        // === Phase 3: Evaluation Framework (P1-10) ===
        services.AddOrkeonEvaluation();

        // === Consensual Process (P2-11) ===
        services.AddOrkeonConsensus();

        // === Code Sandbox (P2-14) ===
        services.AddOrkeonCodeSandbox();

        // === Guardian System (P2-4) — on the execution path since GAP-09 ===
        services.AddOrkeonGuardian();

        // === Phase 8: Memory Encryption (S4) ===
        services.AddOrkeonEncryption();

        // === Cost Tracking (P2-5) ===
        services.AddOrkeonCostTracking();

        // === YAML Crew Definition (P2-10) ===
        services.AddOrkeonYaml();

        // === Phase 9: Session Checkpointing (S2) ===
        services.AddOrkeonCheckpointing();

        // === Phase 10: Training Framework (N1) ===
        services.AddOrkeonTraining();

        // Opt-in subsystems (R4.9 — MORT-003): DLP (AddOrkeonDlp) and NIST compliance
        // reporting (AddOrkeonNistCompliance) are intentionally NOT registered by
        // default — no production code path consumes them yet. Hosts enable them
        // explicitly. See docs/reference/opt-in-subsystems.md.

        return services;
    }

    /// <summary>
    /// Adds YAML crew definition loading, crew factory, and crew exporter services.
    /// </summary>
    public static IServiceCollection AddOrkeonYaml(this IServiceCollection services)
    {
        // Plain AddOptions (no BindConfiguration) keeps this usable in minimal containers that
        // register no IConfiguration; the library default is lenient tool resolution. Hosts that
        // want strict mode set CrewFactoryOptions.StrictTools explicitly (RunnerHost reads the
        // "Orkeon:CrewFactory:StrictTools" key and defaults it to true for runners).
        services.AddOptions<Orkeon.Infrastructure.Configuration.CrewFactoryOptions>();
        services.TryAddSingleton<ICrewDefinitionLoader, YamlCrewDefinitionLoader>();
        services.TryAddScoped<ICrewFactory, CrewFactory>();
        services.TryAddSingleton<YamlCrewExporter>();

        return services;
    }

    /// <summary>
    /// Adds cost tracking, model pricing registry, budget management, and enhanced token counter.
    /// Reads configuration from the "Orkeon:CostTracking" and "Orkeon:TokenCounter" sections.
    /// </summary>
    public static IServiceCollection AddOrkeonCostTracking(this IServiceCollection services)
    {
        services.AddOptions<CostTrackingOptions>()
            .BindConfiguration("Orkeon:CostTracking");
        services.AddOptions<TokenCounterOptions>()
            .BindConfiguration("Orkeon:TokenCounter");

        services.TryAddSingleton<IModelPricingRegistry, ModelPricingRegistry>();
        services.TryAddSingleton<ICostBudgetManager, CostBudgetManager>();
        services.TryAddSingleton<ITokenCounter, EnhancedTokenCounter>();

        return services;
    }

    /// <summary>
    /// Adds consensual process strategy with voting support.
    /// Reads configuration from the "Orkeon:Consensus" section. The voting mechanism is
    /// selected via <c>Orkeon:Consensus:VotingOptions:ConsensusType</c>
    /// (Majority — the default — SuperMajority, Unanimity, WeightedConsensus, BordaCount).
    /// </summary>
    public static IServiceCollection AddOrkeonConsensus(this IServiceCollection services)
    {
        services.AddOptions<ConsensualProcessOptions>()
            .BindConfiguration("Orkeon:Consensus");

        // Voting mechanism selection (R3.3 — MORT-001): the configured ConsensusType picks
        // the dedicated strategy; the default (Majority) preserves the historical behavior.
        services.TryAddSingleton<IVotingStrategyFactory, VotingStrategyFactory>();
        services.TryAddSingleton<IVotingStrategy>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ConsensualProcessOptions>>().Value;
            return sp.GetRequiredService<IVotingStrategyFactory>()
                .Create(options.VotingOptions.ConsensusType);
        });

        // Each agent casts its ballot through its own execution (GAP-04): scoped like the
        // execution service it runs on.
        services.TryAddScoped<IBallotCollector, AgentBallotCollector>();

        // Concrete registration lets ProcessStrategyFactory route ProcessType.Consensual
        // like every other process type (R3.3 — FON-010).
        services.TryAddScoped<ConsensualProcessStrategy>();

        return services;
    }

    /// <summary>
    /// Adds the code sandbox, security analyzer, and secure code interpreter tool.
    /// Configuration is read from the "Orkeon:CodeSandbox" section.
    /// </summary>
    public static IServiceCollection AddOrkeonCodeSandbox(this IServiceCollection services)
    {
        services.AddOptions<SandboxOptions>()
            .BindConfiguration("Orkeon:CodeSandbox");
        services.AddOptions<DockerSandboxOptions>()
            .BindConfiguration("Orkeon:CodeSandbox:Docker");

        services.TryAddSingleton<ICodeSecurityAnalyzer, RoslynCodeSecurityAnalyzer>();

        // Concrete sandboxes are registered so the lazy ICodeSandbox selector can route between
        // them. Both write their run directories under /sandbox, an Internal mount the plain
        // IFileSystemService no longer resolves — so they are built with the privileged view
        // rather than injected with the one every tool holds.
        services.TryAddSingleton(sp => new DockerSandbox(
            sp.GetRequiredService<ILogger<DockerSandbox>>(),
            sp.GetRequiredService<IOptions<SandboxOptions>>(),
            sp.GetRequiredService<IOptions<DockerSandboxOptions>>(),
            PrivilegedFileSystem(sp)));
        services.TryAddSingleton(sp => new HostProcessRunner(
            sp.GetRequiredService<ILogger<HostProcessRunner>>(),
            sp.GetRequiredService<IOptions<SandboxOptions>>(),
            PrivilegedFileSystem(sp)));

        // ICodeSandbox default: prefer the OS-isolating DockerSandbox when available.
        // R10.3 (ORG-012): this factory is pure synchronous wiring — the Docker availability
        // probe (a `docker version` child process, up to 10 s) must never block under the
        // container's singleton-resolution lock. LazyProbingCodeSandbox runs the probe
        // asynchronously at the first ExecuteAsync/IsAvailableAsync and memoizes the result
        // (singleton, so it is still probed at most once per container — as before).
        // Fail-closed (R2.2, unchanged in substance): when Docker is unavailable, execution
        // on the (non-isolating) HostProcessRunner is refused unless
        // SandboxOptions.AllowHostExecution is explicitly opted in; SecureCodeInterpreterTool
        // keeps its own identical gate as defense-in-depth (see SandboxIsolationGate).
        services.TryAddSingleton<ICodeSandbox>(sp => new LazyProbingCodeSandbox(
            sp.GetRequiredService<DockerSandbox>(),
            sp.GetRequiredService<HostProcessRunner>(),
            sp.GetRequiredService<IOptions<SandboxOptions>>(),
            sp.GetRequiredService<ILogger<LazyProbingCodeSandbox>>()));

        services.TryAddSingleton<SecureCodeInterpreterTool>();

        return services;
    }

    /// <summary>
    /// Adds the guardian system: the input, tool and delegation guards, wired to their phases in
    /// a <see cref="GuardianPipeline"/> registered as <see cref="IGuardianPipeline"/> — the port the
    /// execution orchestrator (input phase) and the tool-invocation pipeline (tool and delegation
    /// phases) call. Reads <c>Orkeon:Guardian</c>; with <c>Enabled = false</c> the pipeline holds
    /// no guard and every check allows.
    /// </summary>
    public static IServiceCollection AddOrkeonGuardian(this IServiceCollection services)
    {
        services.AddOptions<GuardianOptions>()
            .BindConfiguration("Orkeon:Guardian");

        // Policy engine (singleton) - uses the default policy from options
        services.TryAddSingleton<GuardianPolicyEngine>(sp =>
        {
            var options = sp.GetService<IOptions<GuardianOptions>>()?.Value ?? new GuardianOptions();
            return new GuardianPolicyEngine(options.DefaultPolicy);
        });

        // Guard implementations (singletons)
        services.TryAddSingleton<InputGuard>();
        services.TryAddSingleton<ToolGuard>();
        services.TryAddSingleton<DelegationGuard>();

        // Guardian pipeline (singleton) - wires guards to their phases
        services.TryAddSingleton<GuardianPipeline>(sp =>
        {
            var options = sp.GetService<IOptions<GuardianOptions>>()?.Value ?? new GuardianOptions();
            var policyEngine = sp.GetRequiredService<GuardianPolicyEngine>();
            var logger = sp.GetRequiredService<ILogger<GuardianPipeline>>();
            var auditLogger = sp.GetService<IAuditLogger>();

            var pipeline = new GuardianPipeline(policyEngine, logger, auditLogger);
            if (!options.Enabled)
                return pipeline;

            pipeline.AddGuard(GuardPhase.Input, sp.GetRequiredService<InputGuard>());
            pipeline.AddGuard(GuardPhase.ToolExecution, sp.GetRequiredService<ToolGuard>());
            pipeline.AddGuard(GuardPhase.Delegation, sp.GetRequiredService<DelegationGuard>());

            return pipeline;
        });
        services.TryAddSingleton<IGuardianPipeline>(sp => sp.GetRequiredService<GuardianPipeline>());

        return services;
    }
}
