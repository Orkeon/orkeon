using Microsoft.Extensions.DependencyInjection;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IServiceScopeFactory"/> double: every created scope resolves
/// the instances registered via <see cref="With{TService}"/>. Used to drive singletons
/// that resolve scoped dependencies per request (R4.6 / ANT-001 — e.g.
/// <c>A2AServer</c>/<c>A2ATaskRouter</c> resolving <c>IAgentRepository</c> per A2A request).
/// </summary>
internal sealed class StubServiceScopeFactory : IServiceScopeFactory
{
    private readonly Dictionary<Type, object> _services = [];

    /// <summary>Number of scopes created so far (one per simulated request).</summary>
    public int CreatedScopeCount;

    /// <summary>Registers an instance resolvable from every scope created by this factory.</summary>
    public StubServiceScopeFactory With<TService>(TService instance) where TService : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        _services[typeof(TService)] = instance;
        return this;
    }

    public IServiceScope CreateScope()
    {
        CreatedScopeCount++;
        return new StubScope(new StubProvider(_services));
    }

    private sealed class StubScope(IServiceProvider provider) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = provider;

        public void Dispose()
        {
            // Nothing to release: the stub hands out externally-owned instances.
        }
    }

    private sealed class StubProvider(Dictionary<Type, object> services) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => services.TryGetValue(serviceType, out var service) ? service : null;
    }
}
