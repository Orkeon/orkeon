using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Checkpointing;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>
/// Manual stub of <see cref="IServiceScopeFactory"/> whose scopes resolve a fixed
/// <see cref="IStateStore"/> instance (and nothing else). Used to exercise the durable
/// persistence path of <c>ScopedCrewExecutionStateManager</c> without a DI container.
/// Pass a null store to simulate "persistence enabled but no store registered".
/// </summary>
public sealed class StubStateStoreScopeFactory : IServiceScopeFactory
{
    private readonly IStateStore? _store;

    /// <summary>Number of scopes created so far (for assertions).</summary>
    public int CreatedScopeCount { get; private set; }

    public StubStateStoreScopeFactory(IStateStore? store)
    {
        _store = store;
    }

    public IServiceScope CreateScope()
    {
        CreatedScopeCount++;
        return new StubScope(_store);
    }

    private sealed class StubScope : IServiceScope
    {
        public StubScope(IStateStore? store) => ServiceProvider = new StubProvider(store);

        public IServiceProvider ServiceProvider { get; }

        public void Dispose()
        {
            // Nothing to dispose — the store lifetime is owned by the test.
        }
    }

    private sealed class StubProvider : IServiceProvider
    {
        private readonly IStateStore? _store;

        public StubProvider(IStateStore? store) => _store = store;

        public object? GetService(Type serviceType)
            => serviceType == typeof(IStateStore) ? _store : null;
    }
}
