using Xunit;
using Orkeon.Infrastructure.Agent;
using Orkeon.Application.Agent;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Crew;
using Microsoft.Extensions.Logging;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.Agent;

/// <summary>
/// Tests for DynamicAgentService implementation.
/// </summary>
public class DynamicAgentServiceTests
{
    private readonly CrewId _crewId = CrewId.Create();
    private readonly AgentRole _role = AgentRole.From("Coordinator");
    private readonly AgentGoal _goal = AgentGoal.From("Coordinate tasks");

    private static StubAgentFactory CreateMockFactory() => new StubAgentFactory();

    /// <summary>Hand-rolled <see cref="IAgentFactory"/> used by these tests.</summary>
    private sealed class StubAgentFactory : IAgentFactory
    {
        public Domain.Agent.Agent CreateAgent(AgentSpawnRequest request)
            => Domain.Agent.Agent.Create(request.Role, request.Goal, request.Backstory);

        public Task<Domain.Agent.Agent> CreateAgentAsync(AgentSpawnRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateAgent(request));
        }
    }

    [Fact]
    public void Constructor_WithValidFactory_CreatesInstance()
    {
        // Arrange
        var mockFactory = CreateMockFactory();

        // Act
        var service = new DynamicAgentService(mockFactory);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullFactory_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new DynamicAgentService(null!));
    }

    [Fact]
    public async Task SpawnAgentAsync_WithValidRequest_CreatesAndRegistersAgent()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new DynamicAgentService(mockFactory);
        var registry = new DynamicAgentRegistry(_crewId);
        var request = new AgentSpawnRequest(_role, _goal, _crewId);

        // Act
        var agent = await service.SpawnAgentAsync(request, registry);

        // Assert
        Assert.NotNull(agent);
        Assert.Equal(_role, agent.Role);
        Assert.Equal(_goal, agent.Goal);
        Assert.Equal(1, registry.DynamicAgentCount);
        Assert.NotNull(registry.GetAgent(agent.Id));
    }

    [Fact]
    public async Task SpawnAgentAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new DynamicAgentService(mockFactory);
        var registry = new DynamicAgentRegistry(_crewId);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.SpawnAgentAsync(null!, registry));
    }

    [Fact]
    public async Task SpawnAgentAsync_WithNullRegistry_ThrowsArgumentNullException()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new DynamicAgentService(mockFactory);
        var request = new AgentSpawnRequest(_role, _goal, _crewId);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.SpawnAgentAsync(request, null!));
    }

    [Fact]
    public async Task SpawnAgentAsync_WithRequestingAgent_TracksParentage()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new DynamicAgentService(mockFactory);
        var registry = new DynamicAgentRegistry(_crewId);
        var requestingAgentId = AgentId.Create();
        var request = AgentSpawnRequest.CreateBuilder(_role, _goal, _crewId)
            .RequestedBy(requestingAgentId)
            .Build();

        // Act
        var agent = await service.SpawnAgentAsync(request, registry);

        // Assert
        var metadata = registry.GetAgentMetadata(agent.Id);
        Assert.NotNull(metadata);
        Assert.Equal(requestingAgentId, metadata.RequestingAgentId);
    }

    [Fact]
    public async Task SpawnAgentAsync_WithCancellation_RespectsCancellation()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new DynamicAgentService(mockFactory);
        var registry = new DynamicAgentRegistry(_crewId);
        var request = new AgentSpawnRequest(_role, _goal, _crewId);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.SpawnAgentAsync(request, registry, cts.Token));
    }

    [Fact]
    public async Task TerminateAgentAsync_WithValidAgent_TerminatesAgent()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new DynamicAgentService(mockFactory);
        var registry = new DynamicAgentRegistry(_crewId);
        var request = new AgentSpawnRequest(_role, _goal, _crewId);
        var agent = await service.SpawnAgentAsync(request, registry);

        // Act
        var result = await service.TerminateAgentAsync(agent.Id, registry, "test termination");

        // Assert
        Assert.True(result);
        Assert.False(registry.IsAgentActive(agent.Id));
    }

    [Fact]
    public async Task TerminateAgentAsync_NonExistentAgent_ReturnsFalse()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new DynamicAgentService(mockFactory);
        var registry = new DynamicAgentRegistry(_crewId);
        var nonExistentAgentId = AgentId.Create();

        // Act
        var result = await service.TerminateAgentAsync(nonExistentAgentId, registry);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TerminateAgentAsync_WithNullAgentId_ThrowsArgumentNullException()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new DynamicAgentService(mockFactory);
        var registry = new DynamicAgentRegistry(_crewId);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.TerminateAgentAsync(null!, registry));
    }

    [Fact]
    public async Task TerminateAgentAsync_WithNullRegistry_ThrowsArgumentNullException()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new DynamicAgentService(mockFactory);
        var agentId = AgentId.Create();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.TerminateAgentAsync(agentId, null!));
    }

    [Fact]
    public async Task AllowsDynamicAgentsAsync_ReturnsTrueByDefault()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new DynamicAgentService(mockFactory);

        // Act
        var result = await service.AllowsDynamicAgentsAsync(_crewId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task AllowsDynamicAgentsAsync_WithNullCrewId_ThrowsArgumentNullException()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new DynamicAgentService(mockFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.AllowsDynamicAgentsAsync(null!));
    }

    [Fact]
    public async Task GetMaxConcurrentDynamicAgentsAsync_ReturnsNullByDefault()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new DynamicAgentService(mockFactory);

        // Act
        var result = await service.GetMaxConcurrentDynamicAgentsAsync(_crewId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetMaxConcurrentDynamicAgentsAsync_WithNullCrewId_ThrowsArgumentNullException()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new DynamicAgentService(mockFactory);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.GetMaxConcurrentDynamicAgentsAsync(null!));
    }

    [Fact]
    public async Task SpawnAgentAsync_WithLogger_LogsSpawn()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var loggerFactory = new RecordingLoggerFactory();
        var typedLogger = new TypedLoggerAdapter<DynamicAgentService>(loggerFactory.Logger);
        var service = new DynamicAgentService(mockFactory, typedLogger);
        var registry = new DynamicAgentRegistry(_crewId);
        var request = new AgentSpawnRequest(_role, _goal, _crewId);

        // Act
        var agent = await service.SpawnAgentAsync(request, registry);

        // Assert
        Assert.True(loggerFactory.Logger.HasEntry(e =>
            e.Level == LogLevel.Information && e.Message.Contains("Dynamic agent spawned")));
    }

    private sealed class TypedLoggerAdapter<T> : ILogger<T>
    {
        private readonly RecordingLogger _inner;
        public TypedLoggerAdapter(RecordingLogger inner) { _inner = inner; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _inner.Log(logLevel, eventId, state, exception, formatter);
    }

    [Fact]
    public async Task MultipleSpawns_CreatesDistinctAgents()
    {
        // Arrange
        var mockFactory = CreateMockFactory();
        var service = new DynamicAgentService(mockFactory);
        var registry = new DynamicAgentRegistry(_crewId);
        var request1 = new AgentSpawnRequest(_role, _goal, _crewId);
        var request2 = new AgentSpawnRequest(_role, _goal, _crewId);

        // Act
        var agent1 = await service.SpawnAgentAsync(request1, registry);
        var agent2 = await service.SpawnAgentAsync(request2, registry);

        // Assert
        Assert.NotEqual(agent1.Id, agent2.Id);
        Assert.Equal(2, registry.DynamicAgentCount);
    }
}
