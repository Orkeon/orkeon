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
using Orkeon.Infrastructure.Knowledge;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.Flows;
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
        services.AddOrkeonChatClientAdapters();
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

        // === Phase 4: Standards & Interoperability ===
        services.AddOrkeonMcp(configuration);
        services.AddOrkeonVectorSearch(configuration);
        services.AddOrkeonKnowledge(configuration);
        services.AddOrkeonRag(configuration);

        // === Phase 8: RAG Data Validation (S6) ===
        services.AddOrkeonRagValidation();

        // === Phase 10: Vector Store Providers (N5) ===
        if (configuration.GetSection("Orkeon:ChromaDb").Exists())
            services.AddOrkeonChromaDb(configuration);
        if (configuration.GetSection("Orkeon:Pinecone").Exists())
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

        // Add resilience options (configurable via appsettings.json "Resilience" section)
        services.AddOptions<Orkeon.Infrastructure.Configuration.ResilienceOptions>()
            .BindConfiguration("Resilience");

        // Add domain event dispatcher
        services.AddSingleton<Domain.SharedKernel.Events.IDomainEventDispatcher, DomainEventDispatcher>();

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

        // Add Memory providers.
        // Driven by configuration (Memory:Provider) and delegated to the factory, instead of a
        // hard-wired InMemoryProvider. When no configuration is registered, the factory falls back
        // to the in-memory provider (with an explicit warning for unrecognized types).
        services.TryAddSingleton<Application.Interfaces.Ports.IMemoryProviderFactory, Memory.MemoryProviderFactory>();
        services.AddSingleton<Orkeon.Domain.Memory.IMemoryProvider>(sp =>
        {
            var factory = sp.GetRequiredService<Application.Interfaces.Ports.IMemoryProviderFactory>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var configuration = sp.GetService<IConfiguration>();

            var providerType = configuration?["Memory:Provider"];
            var connectionString = configuration?["Memory:ConnectionString"] ?? string.Empty;
            var config = string.IsNullOrWhiteSpace(providerType)
                ? MemoryProviderConfigDefaults.InMemory
                : new MemoryProviderConfigDto(providerType, connectionString);

            return factory.Create(config, loggerFactory);
        });

        return services;
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

        // Add embedding service
#pragma warning disable CS0618 // Type or member is obsolete
        services.AddSingleton<Domain.Memory.IEmbeddingService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SimpleEmbeddingService>>();
            var options = sp.GetService<IOptions<OrkeonApplicationOptions>>();
            var dimension = options?.Value?.EmbeddingDimension ?? 384;
            var simpleService = new SimpleEmbeddingService(logger, dimension);
            // Adapter to match Domain interface
            return new DomainEmbeddingServiceAdapter(simpleService);
        });

        // Application-port embedding service (consumed by EmbeddingBasedSelectionStrategy).
        // TryAdd so a real provider (OpenAI / Ollama / Local) registered earlier wins.
        // NOTE: this default SimpleEmbeddingService is hash-based and carries no semantic
        // signal — semantic agent selection requires a real embedding provider.
        services.TryAddSingleton<Application.Interfaces.Ports.IEmbeddingService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SimpleEmbeddingService>>();
            var options = sp.GetService<IOptions<OrkeonApplicationOptions>>();
            var dimension = options?.Value?.EmbeddingDimension ?? 384;
            return new SimpleEmbeddingService(logger, dimension);
        });
#pragma warning restore CS0618

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

        // IToolRegistry — required by CrewFactory, FlowStepExecutor, McpServer, McpToolProvider
        services.TryAddSingleton<Domain.Tools.IToolRegistry, Stubs.InMemoryToolRegistry>();

        // IKnowledgeStore (Domain) — no-op stub; real implementation requires a vector store
        services.TryAddSingleton<Domain.Knowledge.IKnowledgeStore, Stubs.InMemoryKnowledgeStore>();

        // IMemorySystem (Domain) — in-memory agent memory system
        services.TryAddSingleton<Domain.Memory.IMemorySystem, Stubs.InMemoryMemorySystem>();

        // IEmbeddingProvider (Application) — hash-based fallback when no IConfiguration overload is used
        services.TryAddSingleton<Application.Interfaces.Ports.IEmbeddingProvider>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<Stubs.HashBasedEmbeddingProvider>>();
            var options = sp.GetService<IOptions<OrkeonApplicationOptions>>();
            var dim = options?.Value?.EmbeddingDimension ?? 384;
            return new Stubs.HashBasedEmbeddingProvider(logger, dim);
        });

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

        // Callback interfaces — no-op stubs that log at Debug level
        services.TryAddSingleton<Domain.Agent.IStepCallback, Stubs.NullStepCallback>();
        services.TryAddSingleton<Domain.Agent.IStepProgressHandler, Stubs.NullStepProgressHandler>();
        services.TryAddSingleton<Domain.Task.ITaskCallback, Stubs.NullTaskCallback>();

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

        // Streaming agent execution service
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
        services.AddSingleton<PromptShieldBuilder>();
        services.AddSingleton<ToolResultSanitizer>();

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

    private static IServiceCollection AddOrkeonChatClientAdapters(this IServiceCollection services)
    {
        // IChatClient adapters - allow users to register their own IChatClient
        // If not already registered, wrap the IBasicLlmProvider from the factory
        services.TryAddSingleton<IBasicLlmProvider>(sp =>
        {
            var factory = sp.GetRequiredService<ILlmProviderFactory>();
            return factory.Create(Domain.SharedKernel.ValueObjects.LlmConfig.Default());
        });

        services.TryAddSingleton<IChatClient>(sp =>
        {
            // Create a default provider via factory and wrap it
            var factory = sp.GetRequiredService<ILlmProviderFactory>();
            var config = Domain.SharedKernel.ValueObjects.LlmConfig.Default();
            var provider = factory.Create(config);
            var textParser = sp.GetService<Application.Interfaces.LLM.IToolCallParser>();

            // Unwrap LlmProviderAdapter to get the actual ILlmProvider
            // (factory.Create returns LlmProviderAdapter which wraps the real provider)
            var llmProvider = provider switch
            {
                ILlmProvider lp => lp,
                LlmProviderAdapter adapter => adapter.UnderlyingProvider,
                _ => null
            };

            if (llmProvider == null)
            {
                throw new InvalidOperationException(
                    "No IChatClient registered. Register one via services.AddSingleton<IChatClient>(...) " +
                    "or ensure the LlmProviderFactory creates an ILlmProvider.");
            }

            // Detect Anthropic provider and inject its native tool call parser
            Application.Interfaces.LLM.IToolCallParser? nativeParser = null;
            if (llmProvider is AnthropicLlmProvider)
                nativeParser = new LLMs.ToolCalling.AnthropicToolCallParser();

            return new LlmProviderToChatClientAdapter(llmProvider, textFallbackParser: textParser, nativeToolCallParser: nativeParser);
        });

        return services;
    }

    private static IServiceCollection AddOrkeonNativeToolCalling(this IServiceCollection services)
    {
        // === Phase 2b: Native Tool Calling (P1-TC-06) ===
        // Register ILlmProvider by extracting it from the LlmProviderAdapter when available,
        // and register IToolCallingStrategy for OpenAI-compatible providers.
        services.TryAddSingleton<ILlmProvider>(sp =>
        {
            var basicProvider = sp.GetRequiredService<IBasicLlmProvider>();
            if (basicProvider is LLMs.LlmProviderAdapter adapter)
                return adapter.UnderlyingProvider;
            // Fallback: try the factory directly
            var factory = sp.GetRequiredService<ILlmProviderFactory>();
            var provider = factory.Create(Domain.SharedKernel.ValueObjects.LlmConfig.Default());
            if (provider is ILlmProvider llm)
                return llm;
            throw new InvalidOperationException(
                "No ILlmProvider available. The IBasicLlmProvider must be a LlmProviderAdapter or the factory must produce an ILlmProvider.");
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

        // === Phase 5: Flow Engine (P2-1) ===
        services.AddOrkeonFlows();
        services.AddOrkeonFlowVisualization();

        // === Code Sandbox (P2-14) ===
        services.AddOrkeonCodeSandbox();

        // === Guardian System (P2-4) ===
        services.AddOrkeonGuardian();

        // === Phase 8: Enterprise Auth (S3) ===
        services.AddOrkeonAuth();

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

        // Concrete registration lets ProcessStrategyFactory route ProcessType.Consensual
        // like every other process type (R3.3 — FON-010); the application port maps to
        // the same scoped instance.
        services.TryAddScoped<ConsensualProcessStrategy>();
        services.TryAddScoped<IConsensualProcessStrategy>(
            sp => sp.GetRequiredService<ConsensualProcessStrategy>());

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

        // Concrete sandboxes are registered so the lazy ICodeSandbox selector can route between them.
        services.TryAddSingleton<DockerSandbox>();
        services.TryAddSingleton<HostProcessRunner>();

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
    /// Adds the flow engine, step executor, and YAML flow definition loader.
    /// </summary>
    public static IServiceCollection AddOrkeonFlows(this IServiceCollection services)
    {
        services.TryAddSingleton<IFlowStepExecutor, FlowStepExecutor>();
        services.TryAddSingleton<IFlowEngine, FlowEngine>();
        services.TryAddSingleton<YamlFlowDefinitionLoader>();

        return services;
    }

    /// <summary>
    /// Adds the guardian system with default guards for input, output, tool, and delegation phases.
    /// Reads configuration from the "Orkeon:Guardian" section.
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
        services.TryAddSingleton<OutputGuard>();
        services.TryAddSingleton<ToolGuard>();
        services.TryAddSingleton<DelegationGuard>();

        // Guardian pipeline (singleton) - wires guards to their phases
        services.TryAddSingleton<GuardianPipeline>(sp =>
        {
            var policyEngine = sp.GetRequiredService<GuardianPolicyEngine>();
            var logger = sp.GetRequiredService<ILogger<GuardianPipeline>>();
            var auditLogger = sp.GetService<IAuditLogger>();

            var pipeline = new GuardianPipeline(policyEngine, logger, auditLogger);

            pipeline.AddGuard(GuardPhase.Input, sp.GetRequiredService<InputGuard>());
            pipeline.AddGuard(GuardPhase.Output, sp.GetRequiredService<OutputGuard>());
            pipeline.AddGuard(GuardPhase.ToolExecution, sp.GetRequiredService<ToolGuard>());
            pipeline.AddGuard(GuardPhase.Delegation, sp.GetRequiredService<DelegationGuard>());

            return pipeline;
        });

        return services;
    }
}
