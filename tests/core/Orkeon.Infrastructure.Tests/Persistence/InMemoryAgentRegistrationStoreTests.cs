using Orkeon.Domain.Agent;
using Orkeon.Infrastructure.Persistence.Agent;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Persistence;

/// <summary>
/// R4.6 / ANT-001 — the singleton backing store for A2A agent registrations:
/// data must persist across (scoped) repository instances and be safe for
/// concurrent use.
/// </summary>
public class InMemoryAgentRegistrationStoreTests
{
    private static DomainAgent NewAgent(string role = "Researcher")
        => new AgentBuilder().Role(role).Goal($"Goal of {role}").Build();

    [Fact]
    public void TryAdd_ShouldRegisterAgent()
    {
        // Arrange
        var store = new InMemoryAgentRegistrationStore();
        var agent = NewAgent();

        // Act
        var added = store.TryAdd(agent);

        // Assert
        Assert.True(added);
        Assert.True(store.Contains(agent.Id));
        Assert.Same(agent, store.GetById(agent.Id));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void TryAdd_ShouldReturnFalse_WhenIdAlreadyRegistered()
    {
        // Arrange
        var store = new InMemoryAgentRegistrationStore();
        var agent = NewAgent();
        store.TryAdd(agent);

        // Act — same aggregate id, add-if-absent semantics
        var addedAgain = store.TryAdd(agent);

        // Assert — first registration kept, no duplicate
        Assert.False(addedAgain);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Save_ShouldUpsert()
    {
        // Arrange
        var store = new InMemoryAgentRegistrationStore();
        var agent = NewAgent();

        // Act — Save on a fresh id registers it
        store.Save(agent);

        // Assert
        Assert.Equal(1, store.Count);
        Assert.Same(agent, store.GetById(agent.Id));

        // Act — Save again replaces (idempotent on same instance)
        store.Save(agent);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Remove_ShouldUnregisterAgent()
    {
        // Arrange
        var store = new InMemoryAgentRegistrationStore();
        var agent = NewAgent();
        store.TryAdd(agent);

        // Act
        var removed = store.Remove(agent.Id);

        // Assert
        Assert.True(removed);
        Assert.False(store.Contains(agent.Id));
        Assert.Null(store.GetById(agent.Id));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Remove_ShouldReturnFalse_WhenIdUnknown()
    {
        // Arrange
        var store = new InMemoryAgentRegistrationStore();

        // Act & Assert
        Assert.False(store.Remove(NewAgent().Id));
    }

    [Fact]
    public void Snapshot_ShouldBePointInTime()
    {
        // Arrange
        var store = new InMemoryAgentRegistrationStore();
        var first = NewAgent("Researcher");
        store.TryAdd(first);

        // Act — take a snapshot, then mutate the store
        var snapshot = store.Snapshot();
        store.TryAdd(NewAgent("Writer"));

        // Assert — the snapshot does not observe later mutations
        Assert.Single(snapshot);
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public void TryAdd_ShouldBeThreadSafe_UnderConcurrentRegistrations()
    {
        // Arrange — the store is shared between A2A request scopes and pipeline scopes
        var store = new InMemoryAgentRegistrationStore();
        var agents = Enumerable.Range(0, 64).Select(i => NewAgent($"Role-{i}")).ToList();

        // Act — register concurrently
        Parallel.ForEach(agents, agent => store.TryAdd(agent));

        // Assert — every distinct agent is registered exactly once
        Assert.Equal(agents.Count, store.Count);
        Assert.All(agents, agent => Assert.True(store.Contains(agent.Id)));
    }

    [Fact]
    public void TryAdd_ShouldThrow_WhenAgentIsNull()
    {
        var store = new InMemoryAgentRegistrationStore();
        Assert.Throws<ArgumentNullException>(() => store.TryAdd(null!));
        Assert.Throws<ArgumentNullException>(() => store.Save(null!));
    }
}
