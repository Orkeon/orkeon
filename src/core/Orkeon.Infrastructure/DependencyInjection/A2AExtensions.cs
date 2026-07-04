using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Agent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orkeon.Infrastructure.DomainEvents;
using Orkeon.Infrastructure.Persistence;
using Orkeon.Infrastructure.Persistence.Agent;

namespace Orkeon.Infrastructure.AgentCommunication;

/// <summary>
/// DI extension methods for registering A2A protocol services.
/// <para>
/// Opt-in subsystem (R4.9 — MORT-003): the A2A server stack is NOT registered by
/// <c>AddOrkeonInfrastructure()</c>. Hosts that expose or consume the A2A protocol
/// call <c>AddOrkeonA2A(...)</c> explicitly. See <c>docs/reference/opt-in-subsystems.md</c>.
/// </para>
/// </summary>
public static class A2AExtensions
{
    /// <summary>
    /// Adds A2A agent-to-agent protocol services to the DI container.
    /// Reads configuration from the "A2A" section.
    /// </summary>
    public static IServiceCollection AddOrkeonA2A(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<A2AOptions>(configuration.GetSection("A2A"));

        // Security options bind unconditionally: the client honours mTLS / server-cert
        // validation, and the server enforces mTLS / auth schemes when the server is enabled.
        services.Configure<A2ASecurityOptions>(configuration.GetSection("A2A:Security"));

        var options = configuration.GetSection("A2A").Get<A2AOptions>() ?? new A2AOptions();
        return services.AddOrkeonA2ACore(options.EnableServer);
    }

    /// <summary>
    /// Adds A2A agent-to-agent protocol services to the DI container without requiring
    /// an <see cref="IConfiguration"/>. Options are configured through the optional
    /// delegates (defaults apply when omitted).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional A2A options configuration.</param>
    /// <param name="configureSecurity">Optional A2A security options configuration.</param>
    public static IServiceCollection AddOrkeonA2A(
        this IServiceCollection services,
        Action<A2AOptions>? configure = null,
        Action<A2ASecurityOptions>? configureSecurity = null)
    {
        if (configure != null)
            services.Configure(configure);
        if (configureSecurity != null)
            services.Configure(configureSecurity);

        // Probe the configured options to decide whether the server must be hosted.
        var probe = new A2AOptions();
        configure?.Invoke(probe);

        return services.AddOrkeonA2ACore(probe.EnableServer);
    }

    /// <summary>
    /// Registers the A2A services shared by both overloads, plus the external
    /// dependencies the subsystem needs so the opt-in graph is resolvable on its own
    /// (TryAdd — registrations made by <c>AddOrkeonInfrastructure()</c> win when present).
    /// </summary>
    private static IServiceCollection AddOrkeonA2ACore(
        this IServiceCollection services,
        bool enableServer)
    {
        // External dependencies of the subsystem (no-ops when the host already
        // called AddOrkeonInfrastructure(), which registers all of them).
        services.AddHttpClient();
        services.TryAddSingleton<Orkeon.Domain.SharedKernel.Events.IDomainEventDispatcher, DomainEventDispatcher>();
        services.TryAddScoped<IUnitOfWork, InMemoryUnitOfWork>();

        // R4.6 / ANT-001 — A2A agent directory lifecycle (decision: "scoped per request"):
        // the registrations live in a thread-safe SINGLETON backing store that persists
        // across A2A requests, while IAgentRepository stays SCOPED and hydrates from that
        // store on every request. A2AServer/A2ATaskRouter never capture the scoped
        // repository: they open a scope per request via IServiceScopeFactory.
        services.TryAddSingleton<IAgentRegistrationStore, InMemoryAgentRegistrationStore>();
        RegisterSharedStoreAgentRepository(services);

        services.TryAddSingleton<IA2AAgentDiscovery, A2AAgentDiscovery>();
        services.TryAddSingleton<IA2AClient, A2AClient>();
        services.TryAddSingleton<IA2ATaskRouter, A2ATaskRouter>();

        if (enableServer)
        {
            services.AddSingleton<IA2AServer, A2AServer>();
        }

        return services;
    }

    /// <summary>
    /// Ensures the <see cref="IAgentRepository"/> seen by A2A request scopes is backed by
    /// storage that survives across requests (DECISIONS.md §2, R4.6).
    /// <list type="bullet">
    ///   <item>No registration yet → registers the scoped <see cref="SharedStoreAgentRepository"/>
    ///   (hydrates from the singleton <see cref="IAgentRegistrationStore"/>). This is also the
    ///   inverted-order case (<c>AddOrkeonA2A()</c> before <c>AddOrkeonInfrastructure()</c>):
    ///   the infrastructure registration is <c>TryAdd</c> (ANT-019, R9.3), so the upgrade
    ///   posed here keeps winning whatever the call order.</item>
    ///   <item><see cref="InMemoryAgentRepository"/> registered by <c>AddOrkeonInfrastructure()</c> →
    ///   upgraded to <see cref="SharedStoreAgentRepository"/> (same scoped lifetime, shared data):
    ///   its per-scope instance store would be empty in every A2A request scope, so the router
    ///   would never find the agents registered by the pipeline.</item>
    ///   <item>Any other registration (e.g. a DB-backed repository) is left untouched — it
    ///   already provides cross-request storage of its own.</item>
    /// </list>
    /// </summary>
    private static void RegisterSharedStoreAgentRepository(IServiceCollection services)
    {
        var existing = services.LastOrDefault(d => d.ServiceType == typeof(IAgentRepository));
        if (existing is null)
        {
            services.AddScoped<IAgentRepository, SharedStoreAgentRepository>();
            return;
        }

        if (existing.ImplementationType == typeof(InMemoryAgentRepository))
        {
            services.Replace(ServiceDescriptor.Scoped<IAgentRepository, SharedStoreAgentRepository>());
        }
    }
}
