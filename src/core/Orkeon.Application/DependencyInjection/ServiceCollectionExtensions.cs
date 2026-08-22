using Orkeon.Application.Configuration;
using Microsoft.Extensions.AI;
using Orkeon.Application.Common.CQRS;
using Orkeon.Application.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Crew.Planning;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Agent;
using Orkeon.Application.Crew;
using Orkeon.Application.Memory;
using Orkeon.Application.Services.Memory;
using Orkeon.Application.Services.Monitoring;
using Orkeon.Application.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;
using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Application.DependencyInjection;

/// <summary>
/// Extension methods for registering Application services.
/// </summary>
public static class ServiceCollectionExtensions
{
    private static readonly Type s_commandHandlerInterface = typeof(ICommandHandler<,>);
    private static readonly Type s_queryHandlerInterface = typeof(IQueryHandler<,>);
    private static readonly Type s_validatorInterface = typeof(ICommandValidator<>);
    private static readonly Type s_domainEventHandlerInterface = typeof(Domain.SharedKernel.Events.IDomainEventHandler<>);

    /// <summary>
    /// Adds Orkeon Application layer services.
    /// </summary>
    public static IServiceCollection AddOrkeonApplication(this IServiceCollection services)
    {
        // CQRS handlers (auto-scanned from assembly)
        services.AddCqrsHandlers();

        // Command validators (auto-scanned from assembly)
        services.AddCommandValidators();

        // Domain event handlers (auto-scanned from assembly)
        services.AddDomainEventHandlers();

        // Agent execution service and dependencies
        services.AddScoped<IAgentExecutionService, AgentExecutionService>();
        services.RegisterDeliverableResolvers();
        services.RegisterExecutionOrchestrator();
        services.AddScoped<ICallbackOrchestrator, CallbackOrchestrator>();
        services.AddScoped<IMemoryCoordinator, MemoryCoordinator>();
#pragma warning disable CS0618 // Type or member is obsolete
        services.AddSingleton<Interfaces.Ports.IPerformanceMetrics, PerformanceMetricsCollector>();
#pragma warning restore CS0618

        // Memory services
        services.AddSingleton<Memory.CrewMemoryProviderRegistry>();
        services.AddSingleton<IMemoryService, MemoryService>();
        services.AddScoped<IMemorySearchService, MemorySearchService>();

        // Planning services. The default IAgentPlanner is the deterministic stub
        // (fixed 4-step plan — see AgentPlannerService XML doc); an LLM-backed
        // planner is a planned feature.
        services.AddScoped<IAgentPlanner, AgentPlannerService>();
        // TryAdd so a planning-enabled PlanningConfiguration registered by the options
        // overload (before delegating here) is not overwritten by the default one.
        services.TryAddSingleton(new Domain.Crew.Planning.PlanningConfiguration());

        return services;
    }

    /// <summary>
    /// Adds Orkeon Application layer with custom configuration.
    /// </summary>
    public static IServiceCollection AddOrkeonApplication(
        this IServiceCollection services,
        Action<OrkeonApplicationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new OrkeonApplicationOptions();
        configure(options);

        services.Configure<OrkeonApplicationOptions>(opt =>
        {
            opt.MaxShortTermMemoryItems = options.MaxShortTermMemoryItems;
            opt.EnablePersistence = options.EnablePersistence;
            opt.DefaultMaxIterations = options.DefaultMaxIterations;
            opt.EnableRAG = options.EnableRAG;
            opt.MemoryDatabasePath = options.MemoryDatabasePath;
            opt.EmbeddingDimension = options.EmbeddingDimension;
            opt.EnableFlowPersistence = options.EnableFlowPersistence;
            opt.EnablePlanning = options.EnablePlanning;
            opt.PlanningLlmModel = options.PlanningLlmModel;
            opt.CrewRepositoryType = options.CrewRepositoryType;
            opt.CrewsPath = options.CrewsPath;
            opt.EmbeddingProvider = options.EmbeddingProvider;
            opt.OpenAIApiKey = options.OpenAIApiKey;
            opt.OpenAIEmbeddingModel = options.OpenAIEmbeddingModel;
            opt.AzureOpenAIEndpoint = options.AzureOpenAIEndpoint;
            opt.AzureOpenAIDeploymentName = options.AzureOpenAIDeploymentName;
        });

        // Configure planning based on options. Registered BEFORE the base overload so
        // that the planning-enabled PlanningConfiguration wins over the default one
        // (TryAddSingleton in the base overload is a no-op once this is present).
        if (options.EnablePlanning)
        {
            services.AddSingleton(new Domain.Crew.Planning.PlanningConfiguration
            {
                EnablePlanning = true,
                PlanningLlmModel = options.PlanningLlmModel ?? LlmDefaults.DefaultPlanningModel,
                Temperature = 0.1
            });
        }

        // Delegate every shared registration (CQRS handlers, validators, domain event
        // handlers, execution pipeline, memory and planning services) to the base
        // overload so the registration block lives in exactly one place.
        return services.AddOrkeonApplication();
    }

    /// <summary>
    /// Registers the <see cref="Crew.DeliverableResolvers.IDeliverableResolver"/> implementations
    /// and the <see cref="Crew.DeliverableResolvers.IDeliverableResolverFactory"/>. Each resolver
    /// depends on <see cref="Domain.FileSystem.IFileSystemService"/>, which must be registered
    /// by the host (runner / web app) before the crew factory is invoked.
    /// </summary>
    private static IServiceCollection RegisterDeliverableResolvers(this IServiceCollection services)
    {
        // TryAddEnumerable so a double invocation of AddOrkeonApplication does not
        // register the same resolver implementations twice (ANT-012).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<Crew.DeliverableResolvers.IDeliverableResolver, Crew.DeliverableResolvers.FinalMessageResolver>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<Crew.DeliverableResolvers.IDeliverableResolver, Crew.DeliverableResolvers.StructuredOutputResolver>());
        services.TryAddScoped<Crew.DeliverableResolvers.IDeliverableResolverFactory, Crew.DeliverableResolvers.DeliverableResolverFactory>();
        return services;
    }

    /// <summary>
    /// Registers the <see cref="IExecutionOrchestrator"/> with a factory that prefers
    /// <see cref="IChatClient"/> when available and falls back to the
    /// <see cref="IBasicLlmProvider"/>-only constructor. Optionally injects
    /// <see cref="Interfaces.Security.ILlmRateLimiter"/> to throttle LLM calls.
    /// </summary>
    /// <remarks>
    /// Extracted from both <see cref="AddOrkeonApplication(IServiceCollection)"/> overloads
    /// to eliminate duplicate factory logic (DRY — AUDIT-P2-02).
    /// </remarks>
    private static IServiceCollection RegisterExecutionOrchestrator(this IServiceCollection services)
    {
        services.AddScoped<IExecutionOrchestrator>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ExecutionOrchestrator>>();
            var llmProvider = sp.GetRequiredService<IBasicLlmProvider>();
            var planner = sp.GetRequiredService<IAgentPlanner>();
            var chatClient = sp.GetService<IChatClient>();
            var rateLimiter = sp.GetService<Interfaces.Security.ILlmRateLimiter>();
            // Resolve optional native tool calling dependencies
            var fullProvider = sp.GetService<Domain.SharedKernel.ILlmProvider>();
            var toolCallingStrategy = sp.GetService<Interfaces.LLM.IToolCallingStrategy>();

            var deliverableFactory = sp.GetService<Crew.DeliverableResolvers.IDeliverableResolverFactory>();
            var fileSystem = sp.GetRequiredService<Domain.FileSystem.IFileSystemService>();

            ExecutionOrchestrator orchestrator;
            if (chatClient != null)
            {
                var tools = sp.GetServices<Domain.Tools.IBaseTool>();
                var validationPipeline = sp.GetService<IOutputValidationPipeline>();
                var parserFactory = sp.GetService<IOutputParserFactory>();
                if (validationPipeline != null && parserFactory != null && rateLimiter != null)
                {
                    // The usage sink is the seam an observed run's token meter hangs on —
                    // resolved optionally, so hosts without one pay nothing.
                    var usageSink = sp.GetService<Interfaces.Ports.ILlmUsageSink>();
                    orchestrator = new ExecutionOrchestrator(logger, llmProvider, planner, chatClient, tools, validationPipeline, parserFactory, rateLimiter, fullProvider, toolCallingStrategy, deliverableFactory, fileSystem, usageSink);
                }
                else if (validationPipeline != null && parserFactory != null)
                    orchestrator = new ExecutionOrchestrator(logger, llmProvider, planner, chatClient, tools, validationPipeline, parserFactory, fileSystem);
                else
                    orchestrator = new ExecutionOrchestrator(logger, llmProvider, planner, chatClient, tools, fileSystem);
            }
            else
            {
                orchestrator = new ExecutionOrchestrator(logger, llmProvider, planner);
            }

            // RAG-03/C4 — optional knowledge augmentation: hosts without the RAG
            // subsystem resolve null here and prompt composition stays unchanged.
            orchestrator.KnowledgeAugmenter =
                sp.GetService<Orkeon.Rag.Abstractions.Interfaces.IKnowledgeContextAugmenter>();
            return orchestrator;
        });
        return services;
    }

    /// <summary>
    /// Registers all ICommandHandler and IQueryHandler implementations from the Application assembly.
    /// Command handlers are decorated with <see cref="UnitOfWorkCommandHandler{TCommand,TResponse}"/>
    /// so that <see cref="IUnitOfWork.SaveChangesAsync"/> is called after every command.
    /// When a <see cref="ICommandValidator{TCommand}"/> exists for the command type, the handler is
    /// further decorated with <see cref="ValidatingCommandHandler{TCommand,TResponse}"/> as the
    /// outermost decorator: ValidatingCommandHandler → UnitOfWorkCommandHandler → concrete handler.
    /// </summary>
    private static IServiceCollection AddCqrsHandlers(this IServiceCollection services)
    {
        var assembly = typeof(ServiceCollectionExtensions).Assembly;

        // Pre-build a set of command types that have a registered validator so we can decide
        // at registration time whether to wrap with ValidatingCommandHandler.
        var commandTypesWithValidators = BuildCommandTypesWithValidators(assembly);

        foreach (var type in assembly.GetTypes().Where(t => !t.IsAbstract && !t.IsInterface))
        {
            RegisterHandlersForType(services, type, commandTypesWithValidators);
        }

        return services;
    }

    private static void RegisterHandlersForType(
        IServiceCollection services,
        Type type,
        HashSet<Type> commandTypesWithValidators)
    {
        foreach (var iface in type.GetInterfaces().Where(i => i.IsGenericType))
        {
            var genericDef = iface.GetGenericTypeDefinition();

            if (genericDef == s_queryHandlerInterface)
            {
                // Query handlers — register directly, no UoW decoration needed.
                services.AddScoped(iface, type);
            }
            else if (genericDef == s_commandHandlerInterface)
            {
                RegisterCommandHandler(services, type, iface, commandTypesWithValidators);
            }
        }
    }

    private static void RegisterCommandHandler(
        IServiceCollection services,
        Type concreteHandlerType,
        Type iface,
        HashSet<Type> commandTypesWithValidators)
    {
        // Register the concrete type, then decorate with UnitOfWorkCommandHandler
        // so SaveChangesAsync is called after every command (dispatching domain events).
        // If a validator exists for this command, also wrap with
        // ValidatingCommandHandler as the outermost decorator.
        services.AddScoped(concreteHandlerType);

        var commandType = iface.GetGenericArguments()[0];
        var responseType = iface.GetGenericArguments()[1];
        var uowDecoratorType = typeof(UnitOfWorkCommandHandler<,>).MakeGenericType(commandType, responseType);

        if (commandTypesWithValidators.Contains(commandType))
        {
            RegisterCommandHandlerWithValidation(services, iface, concreteHandlerType, uowDecoratorType, commandType, responseType);
        }
        else
        {
            RegisterCommandHandlerWithUoW(services, iface, concreteHandlerType, uowDecoratorType);
        }
    }

    private static void RegisterCommandHandlerWithValidation(
        IServiceCollection services,
        Type iface,
        Type concreteHandlerType,
        Type uowDecoratorType,
        Type commandType,
        Type responseType)
    {
        var validatingDecoratorType = typeof(ValidatingCommandHandler<,>).MakeGenericType(commandType, responseType);
        var validatorInterfaceType = typeof(ICommandValidator<>).MakeGenericType(commandType);

        services.AddScoped(iface, sp =>
        {
            // Inner: concrete handler
            var concreteHandler = sp.GetRequiredService(concreteHandlerType);
            // Middle: UnitOfWork decorator wraps the concrete handler
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            var uowHandler = Activator.CreateInstance(uowDecoratorType, concreteHandler, unitOfWork)!;
            // Outer: ValidatingCommandHandler wraps the UoW handler
            var validator = sp.GetRequiredService(validatorInterfaceType);
            return Activator.CreateInstance(validatingDecoratorType, uowHandler, validator)!;
        });
    }

    private static void RegisterCommandHandlerWithUoW(
        IServiceCollection services,
        Type iface,
        Type concreteHandlerType,
        Type uowDecoratorType)
    {
        services.AddScoped(iface, sp =>
        {
            var inner = sp.GetRequiredService(concreteHandlerType);
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            return Activator.CreateInstance(uowDecoratorType, inner, unitOfWork)!;
        });
    }

    /// <summary>
    /// Scans the assembly for all <see cref="ICommandValidator{TCommand}"/> implementations
    /// and returns the set of command types that have at least one validator.
    /// </summary>
    private static HashSet<Type> BuildCommandTypesWithValidators(Assembly assembly)
    {
        var result = new HashSet<Type>();
        foreach (var (validatorInterface, _) in EnumerateValidatorImplementations(assembly))
        {
            result.Add(validatorInterface.GetGenericArguments()[0]);
        }
        return result;
    }

    /// <summary>
    /// Registers all ICommandValidator implementations from the Application assembly.
    /// </summary>
    private static IServiceCollection AddCommandValidators(this IServiceCollection services)
    {
        var assembly = typeof(ServiceCollectionExtensions).Assembly;
        foreach (var (validatorInterface, implementation) in EnumerateValidatorImplementations(assembly))
        {
            services.AddScoped(validatorInterface, implementation);
        }

        return services;
    }

    /// <summary>
    /// Single assembly scan that yields every concrete <see cref="ICommandValidator{TCommand}"/>
    /// implementation paired with its closed validator interface. Shared by
    /// <see cref="BuildCommandTypesWithValidators"/> and <see cref="AddCommandValidators"/>
    /// to avoid re-scanning the assembly twice with the same predicate.
    /// </summary>
    private static IEnumerable<(Type ValidatorInterface, Type Implementation)> EnumerateValidatorImplementations(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface)
                continue;

            foreach (var iface in type.GetInterfaces())
            {
                if (!iface.IsGenericType)
                    continue;

                if (iface.GetGenericTypeDefinition() == s_validatorInterface)
                {
                    yield return (iface, type);
                }
            }
        }
    }

    /// <summary>
    /// Registers all IDomainEventHandler implementations from the Application assembly.
    /// </summary>
    private static IServiceCollection AddDomainEventHandlers(this IServiceCollection services)
    {
        var assembly = typeof(ServiceCollectionExtensions).Assembly;
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface)
                continue;

            foreach (var iface in type.GetInterfaces())
            {
                if (!iface.IsGenericType)
                    continue;

                if (iface.GetGenericTypeDefinition() == s_domainEventHandlerInterface)
                {
                    services.AddScoped(iface, type);
                }
            }
        }

        return services;
    }
}

