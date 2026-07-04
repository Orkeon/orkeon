using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Tests.Doubles;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Persistence;

/// <summary>
/// R4.6 / ANT-001 — the scoped <see cref="SharedStoreAgentRepository"/> hydrates from the
/// singleton <see cref="InMemoryAgentRegistrationStore"/> on every call: agents registered
/// through one repository instance (one scope) must be visible through any other instance
/// (another scope) sharing the same store.
/// </summary>
public class SharedStoreAgentRepositoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DomainAgent NewAgent(string role = "Researcher")
        => new AgentBuilder().Role(role).Goal($"Goal of {role}").Build();

    [Fact]
    public async Task AddAsync_ShouldMakeAgentVisible_ToAnotherRepositoryInstance()
    {
        // Arrange — two repository "scopes" sharing one singleton store
        var store = new InMemoryAgentRegistrationStore();
        var pipelineScopeRepo = new SharedStoreAgentRepository(store, new NullUnitOfWork());
        var a2aRequestScopeRepo = new SharedStoreAgentRepository(store, new NullUnitOfWork());
        var agent = NewAgent();

        // Act — the pipeline registers an agent in its own scope
        await pipelineScopeRepo.AddAsync(agent, Ct);

        // Assert — a later A2A request scope hydrates the same registration
        var fetched = await a2aRequestScopeRepo.GetByIdAsync(agent.Id, Ct);
        Assert.Same(agent, fetched);

        var available = await a2aRequestScopeRepo.GetAvailableAgentsAsync(Ct);
        Assert.Contains(available, a => a.Id == agent.Id);
    }

    [Fact]
    public async Task AddAsync_ShouldTrackAggregate_InScopedUnitOfWork()
    {
        // Arrange
        var store = new InMemoryAgentRegistrationStore();
        var unitOfWork = new MockUnitOfWork();
        var repo = new SharedStoreAgentRepository(store, unitOfWork);
        var agent = NewAgent();

        // Act
        await repo.AddAsync(agent, Ct);

        // Assert — domain-event tracking stays per-scope
        Assert.Single(unitOfWork.TrackedAggregates);
        Assert.Same(agent, unitOfWork.TrackedAggregates[0]);
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpsertIntoSharedStore_AndTrack()
    {
        // Arrange
        var store = new InMemoryAgentRegistrationStore();
        var unitOfWork = new MockUnitOfWork();
        var repo = new SharedStoreAgentRepository(store, unitOfWork);
        var agent = NewAgent();

        // Act — update without prior add (upsert semantics, mirrors InMemoryAgentRepository)
        await repo.UpdateAsync(agent, Ct);

        // Assert
        Assert.Same(agent, store.GetById(agent.Id));
        Assert.Single(unitOfWork.TrackedAggregates);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveFromSharedStore_ForAllInstances()
    {
        // Arrange
        var store = new InMemoryAgentRegistrationStore();
        var repoA = new SharedStoreAgentRepository(store, new NullUnitOfWork());
        var repoB = new SharedStoreAgentRepository(store, new NullUnitOfWork());
        var agent = NewAgent();
        await repoA.AddAsync(agent, Ct);

        // Act
        await repoB.DeleteAsync(agent.Id, Ct);

        // Assert — removal is visible through every instance
        Assert.False(await repoA.ExistsAsync(agent.Id, Ct));
        Assert.Null(await repoA.GetByIdAsync(agent.Id, Ct));
    }

    [Fact]
    public async Task CountAsync_ShouldReflectSharedStore()
    {
        // Arrange
        var store = new InMemoryAgentRegistrationStore();
        var repoA = new SharedStoreAgentRepository(store, new NullUnitOfWork());
        var repoB = new SharedStoreAgentRepository(store, new NullUnitOfWork());

        await repoA.AddAsync(NewAgent("Researcher"), Ct);
        await repoB.AddAsync(NewAgent("Writer"), Ct);

        // Act & Assert — both instances observe the union
        Assert.Equal(2, await repoA.CountAsync(Ct));
        Assert.Equal(2, await repoB.CountAsync(Ct));
    }

    [Fact]
    public async Task GetByRoleAsync_ShouldFilterOnSharedData()
    {
        // Arrange
        var store = new InMemoryAgentRegistrationStore();
        var writerRepo = new SharedStoreAgentRepository(store, new NullUnitOfWork());
        var readerRepo = new SharedStoreAgentRepository(store, new NullUnitOfWork());
        var researcher = NewAgent("Researcher");
        await writerRepo.AddAsync(researcher, Ct);
        await writerRepo.AddAsync(NewAgent("Writer"), Ct);

        // Act
        var byRole = await readerRepo.GetByRoleAsync(AgentRole.From("Researcher"), Ct);

        // Assert
        Assert.Single(byRole);
        Assert.Equal(researcher.Id, byRole[0].Id);
    }

    [Fact]
    public async Task GetByIdsAsync_ShouldReturnOnlyRequestedAgents()
    {
        // Arrange
        var store = new InMemoryAgentRegistrationStore();
        var repo = new SharedStoreAgentRepository(store, new NullUnitOfWork());
        var first = NewAgent("Researcher");
        var second = NewAgent("Writer");
        await repo.AddAsync(first, Ct);
        await repo.AddAsync(second, Ct);

        // Act
        var fetched = await repo.GetByIdsAsync([first.Id], Ct);

        // Assert
        Assert.Single(fetched);
        Assert.Equal(first.Id, fetched[0].Id);
    }
}
