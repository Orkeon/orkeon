using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Memory;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Infrastructure.DependencyInjection;

namespace Orkeon.Host.Tests;

/// <summary>
/// The risk the gateway specification calls the most serious of this design: state leaking
/// between two conversations. These tests run against the REAL composition — the same
/// AddOrkeonApplication + AddOrkeonInfrastructure the host builds on — because the first
/// version of this file registered its own hand-made scoped service in its own container and
/// then proved that Microsoft's DI gives two scopes two instances. That test could not fail,
/// would have passed with CrewRunner never creating a scope at all, and passed while the
/// product registered a shared singleton behind the scoped facade.
/// </summary>
public class RunIsolationTests
{
    private static ServiceProvider BuildRealProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<Orkeon.Domain.FileSystem.IFileSystemService>(
            new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService());
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Two_run_scopes_do_not_share_a_crew_repository()
    {
        // This is the isolation CrewRunner actually leans on: the factory persists the loaded
        // crew into a scoped repository and the orchestrator resolves it back from the same
        // scope. Shared across scopes, one conversation's crew would be resolvable — and
        // runnable — from another's.
        using var provider = BuildRealProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();

        using var first = scopes.CreateScope();
        using var second = scopes.CreateScope();

        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<ICrewRepository>(),
            second.ServiceProvider.GetRequiredService<ICrewRepository>());
    }

    [Fact]
    public void A_released_crew_memory_is_gone_not_merely_hidden()
    {
        // The memory service is a singleton keyed by crew id, and every hosted message loads
        // a fresh crew with a fresh id. Without release, a daemon accumulates one memory
        // system per conversation forever; with it, the entry is dropped and a later lookup
        // builds a new, empty one.
        using var provider = BuildRealProvider();
        var memory = provider.GetRequiredService<IMemoryService>();
        var crewId = CrewId.Create();

        var before = memory.GetMemorySystem(crewId);
        memory.ReleaseMemorySystem(crewId);
        var after = memory.GetMemorySystem(crewId);

        Assert.NotSame(before, after);
    }

    [Fact]
    public void A_released_provider_selection_falls_back_to_the_host_default()
    {
        using var provider = BuildRealProvider();
        var registry = provider.GetRequiredService<CrewMemoryProviderRegistry>();
        var crewId = CrewId.Create();

        registry.SetProvider(crewId, "redis");
        Assert.Equal("redis", registry.GetProvider(crewId));

        registry.Remove(crewId);
        Assert.Null(registry.GetProvider(crewId));
    }
}
