using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.AgentCommunication;
using Orkeon.Domain.Agent;
using Orkeon.Infrastructure.AgentCommunication;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.DependencyInjection;

namespace Orkeon.Infrastructure.Tests.A2A;

/// <summary>
/// R4.6 / ANT-001 — DI lifecycle of the A2A agent directory (decision: "scoped per request",
/// DECISIONS.md §2): <c>IAgentRepository</c> is scoped and hydrates from the singleton
/// <c>IAgentRegistrationStore</c>; the singleton <c>A2AServer</c>/<c>A2ATaskRouter</c> resolve
/// it through <c>IServiceScopeFactory</c> per request instead of capturing it (captive
/// dependency). All resolution tests run with <c>ValidateScopes = true</c>.
/// </summary>
public class A2ALifecycleValidationTests
{
    private static readonly ServiceProviderOptions s_validateScopes = new()
    {
        ValidateScopes = true,
    };

    private static readonly ServiceProviderOptions s_validateScopesAndBuild = new()
    {
        ValidateScopes = true,
        ValidateOnBuild = true,
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Orkeon.Domain.FileSystem.IFileSystemService>(
            new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService());
        return services;
    }

    // -------------------------------------------------------------------------
    // 1. No captive dependency: the server graph resolves under ValidateScopes
    // -------------------------------------------------------------------------

    [Fact]
    public void A2AServer_ShouldResolve_WithValidateScopes_WhenServerEnabledByDelegate()
    {
        // Arrange — before R4.6 this threw InvalidOperationException at resolution:
        // the singleton IA2AServer captured the scoped IAgentRepository.
        var services = NewServices();
        services.AddOrkeonA2A(options => options.EnableServer = true);

        // Act
        using var provider = services.BuildServiceProvider(s_validateScopesAndBuild);

        // Assert — resolving from the root no longer trips scope validation.
        Assert.NotNull(provider.GetRequiredService<IA2AServer>());
        Assert.NotNull(provider.GetRequiredService<IA2ATaskRouter>());
    }

    [Fact]
    public void A2AServer_ShouldResolve_WithValidateScopes_WhenServerEnabledByConfiguration()
    {
        // Arrange — the exact DoD scenario: ValidateScopes=true + A2A:EnableServer=true.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["A2A:EnableServer"] = "true",
            })
            .Build();
        var services = NewServices();
        services.AddOrkeonA2A(configuration);

        // Act
        using var provider = services.BuildServiceProvider(s_validateScopesAndBuild);

        // Assert
        Assert.NotNull(provider.GetRequiredService<IA2AServer>());
    }

    // -------------------------------------------------------------------------
    // 2. Scoped repository + singleton backing store (hydration per request)
    // -------------------------------------------------------------------------

    [Fact]
    public void AgentRepository_ShouldBeScopedPerRequest_OverASingletonStore()
    {
        // Arrange
        var services = NewServices();
        services.AddOrkeonA2A();
        using var provider = services.BuildServiceProvider(s_validateScopes);

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        // Act
        var storeA = scopeA.ServiceProvider.GetRequiredService<IAgentRegistrationStore>();
        var storeB = scopeB.ServiceProvider.GetRequiredService<IAgentRegistrationStore>();
        var repoA = scopeA.ServiceProvider.GetRequiredService<IAgentRepository>();
        var repoB = scopeB.ServiceProvider.GetRequiredService<IAgentRepository>();

        // Assert — decision R4.6: repository scoped per request, data in a shared singleton.
        Assert.Same(storeA, storeB);
        Assert.NotSame(repoA, repoB);
        Assert.IsType<SharedStoreAgentRepository>(repoA);
        Assert.IsType<SharedStoreAgentRepository>(repoB);
    }

    [Fact]
    public async Task AgentRegisteredInOneScope_ShouldBeVisibleInAnotherScope()
    {
        // Arrange — DECISIONS.md §2: a purely scoped repository would keep nothing between
        // two requests; the shared backing store is what makes registrations survive.
        var services = NewServices();
        services.AddOrkeonA2A();
        using var provider = services.BuildServiceProvider(s_validateScopes);

        var agent = new AgentBuilder().Role("Researcher").Goal("Research topics").Build();

        using (var pipelineScope = provider.CreateScope())
        {
            var repo = pipelineScope.ServiceProvider.GetRequiredService<IAgentRepository>();
            await repo.AddAsync(agent, Ct);
        }

        // Act — a later "request" scope hydrates from the shared store.
        using var requestScope = provider.CreateScope();
        var requestRepo = requestScope.ServiceProvider.GetRequiredService<IAgentRepository>();
        var fetched = await requestRepo.GetByIdAsync(agent.Id, Ct);

        // Assert
        Assert.NotNull(fetched);
        Assert.Equal(agent.Id, fetched.Id);
    }

    // -------------------------------------------------------------------------
    // 3. The singleton router/server find agents registered by other scopes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Router_ShouldFindAgent_RegisteredInAnotherScope()
    {
        // Arrange — end-to-end over the real container: the pipeline registers an agent
        // in its own scope; the singleton router must find it in its per-request scope.
        var services = NewServices();
        services.AddOrkeonA2A();
        using var provider = services.BuildServiceProvider(s_validateScopes);

        using (var pipelineScope = provider.CreateScope())
        {
            var repo = pipelineScope.ServiceProvider.GetRequiredService<IAgentRepository>();
            await repo.AddAsync(new AgentBuilder().Role("Researcher").Goal("Research topics").Build(), Ct);
        }

        var router = provider.GetRequiredService<IA2ATaskRouter>();

        // Act
        var response = await router.RouteTaskAsync(new A2ATaskRequest
        {
            Id = "cross-scope-1",
            SkillId = "Researcher",
            Input = "Find the latest AI papers",
        }, Ct);

        // Assert — before R4.6, a fresh per-request scope held an EMPTY instance store
        // and the router could never find pipeline agents.
        Assert.Equal(A2ATaskStatus.Completed, response.Status);
        Assert.Contains("Researcher", response.Output!);
    }

    [Fact]
    public async Task AgentCard_ShouldListAgent_RegisteredInAnotherScope_OverHttp()
    {
        // Arrange — full stack: DI container with scope validation + real HTTP server.
        var port = GetFreePort();
        var services = NewServices();
        services.AddOrkeonA2A(options =>
        {
            options.EnableServer = true;
            options.Port = port;
        });
        using var provider = services.BuildServiceProvider(s_validateScopes);

        using (var pipelineScope = provider.CreateScope())
        {
            var repo = pipelineScope.ServiceProvider.GetRequiredService<IAgentRepository>();
            await repo.AddAsync(new AgentBuilder().Role("Researcher").Goal("Research topics").Build(), Ct);
        }

        var server = provider.GetRequiredService<IA2AServer>();
        try
        {
            await server.StartAsync(Ct);

            // Act — the discovery endpoint resolves the repository in its own request scope.
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync($"http://localhost:{port}/.well-known/agent.json", Ct);
            var json = await response.Content.ReadAsStringAsync(Ct);

            // Assert — the agent registered by the pipeline scope is advertised.
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Researcher", json);
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Asks the OS for a free ephemeral loopback port.</summary>
    private static int GetFreePort()
    {
        using var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    // -------------------------------------------------------------------------
    // 4. Repository registration upgrade rules (AddOrkeonA2A composition)
    // -------------------------------------------------------------------------

    [Fact]
    public void AddOrkeonA2A_ShouldRegisterScopedRepository_AndSingletonStore()
    {
        // Arrange
        var services = NewServices();

        // Act
        services.AddOrkeonA2A();

        // Assert — lifetimes per decision R4.6.
        var repository = Assert.Single(services, d => d.ServiceType == typeof(IAgentRepository));
        Assert.Equal(ServiceLifetime.Scoped, repository.Lifetime);
        Assert.Equal(typeof(SharedStoreAgentRepository), repository.ImplementationType);

        var store = Assert.Single(services, d => d.ServiceType == typeof(IAgentRegistrationStore));
        Assert.Equal(ServiceLifetime.Singleton, store.Lifetime);
        Assert.Equal(typeof(InMemoryAgentRegistrationStore), store.ImplementationType);
    }

    [Fact]
    public void AddOrkeonA2A_ShouldUpgradeInMemoryAgentRepository_RegisteredByInfrastructure()
    {
        // Arrange — AddOrkeonInfrastructure() registers the per-scope InMemoryAgentRepository,
        // whose instance store would be empty in every A2A request scope.
        var services = NewServices();
        services.AddOrkeonInfrastructure();

        // Act
        services.AddOrkeonA2A();

        // Assert — the known in-memory default is upgraded to the shared-store-backed
        // implementation (same scoped lifetime) so pipeline and A2A share one directory.
        var repository = Assert.Single(services, d => d.ServiceType == typeof(IAgentRepository));
        Assert.Equal(ServiceLifetime.Scoped, repository.Lifetime);
        Assert.Equal(typeof(SharedStoreAgentRepository), repository.ImplementationType);
    }

    [Fact]
    public void AddOrkeonA2A_ShouldKeepCustomAgentRepository()
    {
        // Arrange — a host-supplied repository (e.g. DB-backed) already provides
        // cross-request storage and must NOT be replaced by the opt-in.
        var services = NewServices();
        services.AddScoped<IAgentRepository, MockAgentRepository>();

        // Act
        services.AddOrkeonA2A();

        // Assert
        var repository = Assert.Single(services, d => d.ServiceType == typeof(IAgentRepository));
        Assert.Equal(typeof(MockAgentRepository), repository.ImplementationType);
    }

    [Fact]
    public void AddOrkeonA2A_ShouldBeIdempotent_ForAgentRepositoryRegistration()
    {
        // Arrange
        var services = NewServices();

        // Act
        services.AddOrkeonA2A();
        services.AddOrkeonA2A();

        // Assert — no duplicate registration on double activation.
        var repository = Assert.Single(services, d => d.ServiceType == typeof(IAgentRepository));
        Assert.Equal(typeof(SharedStoreAgentRepository), repository.ImplementationType);
        Assert.Single(services, d => d.ServiceType == typeof(IAgentRegistrationStore));
    }

    // -------------------------------------------------------------------------
    // 5. R9.3 / ANT-019 — registration order must not matter
    // -------------------------------------------------------------------------

    [Fact]
    public void AddOrkeonInfrastructure_AfterA2A_ShouldKeepSharedStoreRepository()
    {
        // ANT-019: on e91ef936 the later non-TryAdd AddScoped<IAgentRepository,
        // InMemoryAgentRepository> silently won back the registration in this order,
        // reviving ANT-001's failure mode (per-scope empty directory, no crash).
        var services = NewServices();

        services.AddOrkeonA2A();
        services.AddOrkeonInfrastructure();

        var repository = Assert.Single(services, d => d.ServiceType == typeof(IAgentRepository));
        Assert.Equal(typeof(SharedStoreAgentRepository), repository.ImplementationType);
    }

    [Fact]
    public async Task InvertedOrder_AgentRegisteredInOneScope_ShouldBeVisibleInAnother()
    {
        // The functional consequence of the inverted order: the directory must still
        // survive across scopes (fails on e91ef936 — the second scope saw an empty
        // per-scope InMemoryAgentRepository).
        var services = NewServices();
        services.AddOrkeonA2A();
        services.AddOrkeonInfrastructure();
        using var provider = services.BuildServiceProvider(s_validateScopes);

        var agent = new AgentBuilder().Role("Researcher").Goal("Research topics").Build();

        using (var pipelineScope = provider.CreateScope())
        {
            var repo = pipelineScope.ServiceProvider.GetRequiredService<IAgentRepository>();
            await repo.AddAsync(agent, Ct);
        }

        using var requestScope = provider.CreateScope();
        var requestRepo = requestScope.ServiceProvider.GetRequiredService<IAgentRepository>();
        var fetched = await requestRepo.GetByIdAsync(agent.Id, Ct);

        Assert.NotNull(fetched);
        Assert.Equal(agent.Id, fetched.Id);
    }

    [Fact]
    public void HostCustomRepository_RegisteredFirst_ShouldSurviveBothExtensions()
    {
        // Documented override contract (bootstrap.md): a host repository registered before
        // the Orkeon extensions is never crushed — neither by the infrastructure default
        // (now TryAdd) nor by the A2A upgrade (which only replaces the in-memory default).
        var services = NewServices();
        services.AddScoped<IAgentRepository, MockAgentRepository>();

        services.AddOrkeonInfrastructure();
        services.AddOrkeonA2A();

        var repository = Assert.Single(services, d => d.ServiceType == typeof(IAgentRepository));
        Assert.Equal(typeof(MockAgentRepository), repository.ImplementationType);
    }
}
