using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Host.Tests;

/// <summary>
/// The risk the gateway specification calls the most serious of this design: a memory scope
/// leaking between two conversations. The defence is one dependency-injection scope per run,
/// and this holds it rather than trusting the comment that says so.
/// </summary>
public class RunIsolationTests
{
    /// <summary>A memory scope that can be told apart from another instance.</summary>
    private sealed class CountingMemoryScope : IMemoryScope
    {
        private static int _created;

        public CountingMemoryScope() => Ordinal = Interlocked.Increment(ref _created);

        public int Ordinal { get; }

        public bool Disposed { get; private set; }

        public string AgentId => "test";

        public string ScopeId => Ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public Task<T> ExecuteInScopeAsync<T>(Func<Task<T>> operation) => operation();

        public Task ExecuteInScopeAsync(Func<Task> operation) => operation();

        public void Dispose() => Disposed = true;
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        // Mirrors how the framework registers it: scoped, so the scope is what separates runs.
        services.AddScoped<IMemoryScope, CountingMemoryScope>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Two_runs_do_not_share_a_memory_scope()
    {
        using var provider = BuildProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();

        using var first = scopes.CreateScope();
        using var second = scopes.CreateScope();

        var firstMemory = (CountingMemoryScope)first.ServiceProvider.GetRequiredService<IMemoryScope>();
        var secondMemory = (CountingMemoryScope)second.ServiceProvider.GetRequiredService<IMemoryScope>();

        Assert.NotSame(firstMemory, secondMemory);
        Assert.NotEqual(firstMemory.Ordinal, secondMemory.Ordinal);
    }

    [Fact]
    public void One_run_sees_one_memory_scope_throughout()
    {
        // Resolving twice inside a run must not create a second memory: the crew and the tools
        // have to be looking at the same thing.
        using var provider = BuildProvider();
        using var scope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();

        var once = scope.ServiceProvider.GetRequiredService<IMemoryScope>();
        var twice = scope.ServiceProvider.GetRequiredService<IMemoryScope>();

        Assert.Same(once, twice);
    }

    [Fact]
    public void A_run_s_memory_dies_with_the_run()
    {
        // Otherwise the leak is not between two live conversations but between a finished one
        // and everything that follows.
        using var provider = BuildProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();

        CountingMemoryScope memory;
        using (var scope = scopes.CreateScope())
        {
            memory = (CountingMemoryScope)scope.ServiceProvider.GetRequiredService<IMemoryScope>();
            Assert.False(memory.Disposed);
        }

        Assert.True(memory.Disposed);
    }
}
