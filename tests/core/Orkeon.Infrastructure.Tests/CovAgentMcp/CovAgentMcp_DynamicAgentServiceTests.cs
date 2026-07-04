using Microsoft.Extensions.Logging;
using Orkeon.Application.Agent;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Infrastructure.Agent;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.CovAgentMcp;

/// <summary>
/// Coverage tests for <see cref="DynamicAgentService"/> error/warning/logging branches.
/// </summary>
public sealed class CovAgentMcp_DynamicAgentServiceTests
{
    private readonly CrewId _crewId = CrewId.Create();
    private readonly AgentRole _role = AgentRole.From("Coordinator");
    private readonly AgentGoal _goal = AgentGoal.From("Coordinate");

    private sealed class TypedLogger<T> : ILogger<T>
    {
        private readonly RecordingLogger _inner;
        public TypedLogger(RecordingLogger inner) => _inner = inner;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);
        public void Log<TState>(LogLevel l, EventId e, TState s, Exception? ex, Func<TState, Exception?, string> f)
            => _inner.Log(l, e, s, ex, f);
    }

    private sealed class StubFactory : IAgentFactory
    {
        public Domain.Agent.Agent CreateAgent(AgentSpawnRequest request)
            => Domain.Agent.Agent.Create(request.Role, request.Goal, request.Backstory);
        public Task<Domain.Agent.Agent> CreateAgentAsync(AgentSpawnRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(CreateAgent(request));
        }
    }

    private sealed class ThrowingFactory : IAgentFactory
    {
        public Domain.Agent.Agent CreateAgent(AgentSpawnRequest request)
            => throw new InvalidOperationException("boom");
        public Task<Domain.Agent.Agent> CreateAgentAsync(AgentSpawnRequest request, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task SpawnAgentAsync_WhenFactoryThrows_LogsErrorAndRethrows()
    {
        using var loggerFactory = new RecordingLoggerFactory();
        var service = new DynamicAgentService(
            new ThrowingFactory(),
            new TypedLogger<DynamicAgentService>(loggerFactory.Logger));
        var registry = new DynamicAgentRegistry(_crewId);
        var request = new AgentSpawnRequest(_role, _goal, _crewId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SpawnAgentAsync(request, registry, TestContext.Current.CancellationToken));

        Assert.True(loggerFactory.Logger.HasEntry(e =>
            e.Level == LogLevel.Error && e.Message.Contains("Failed to spawn")));
        Assert.Equal(0, registry.DynamicAgentCount);
    }

    [Fact]
    public async Task SpawnAgentAsync_WithExistingAgents_LogsDebugCount()
    {
        using var loggerFactory = new RecordingLoggerFactory();
        var service = new DynamicAgentService(
            new StubFactory(),
            new TypedLogger<DynamicAgentService>(loggerFactory.Logger));
        var registry = new DynamicAgentRegistry(_crewId);

        // First spawn: count is 0, no debug. Second spawn: count > 0 triggers debug log.
        await service.SpawnAgentAsync(new AgentSpawnRequest(_role, _goal, _crewId), registry, TestContext.Current.CancellationToken);
        await service.SpawnAgentAsync(new AgentSpawnRequest(_role, _goal, _crewId), registry, TestContext.Current.CancellationToken);

        Assert.True(loggerFactory.Logger.HasEntry(e =>
            e.Level == LogLevel.Debug && e.Message.Contains("dynamic agent count")));
        Assert.Equal(2, registry.DynamicAgentCount);
    }

    [Fact]
    public async Task TerminateAgentAsync_NonExistent_LogsWarning()
    {
        using var loggerFactory = new RecordingLoggerFactory();
        var service = new DynamicAgentService(
            new StubFactory(),
            new TypedLogger<DynamicAgentService>(loggerFactory.Logger));
        var registry = new DynamicAgentRegistry(_crewId);

        var result = await service.TerminateAgentAsync(AgentId.Create(), registry, "no-op");

        Assert.False(result);
        Assert.True(loggerFactory.Logger.HasEntry(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("non-existent")));
    }

    [Fact]
    public async Task TerminateAgentAsync_Existing_LogsInformation()
    {
        using var loggerFactory = new RecordingLoggerFactory();
        var service = new DynamicAgentService(
            new StubFactory(),
            new TypedLogger<DynamicAgentService>(loggerFactory.Logger));
        var registry = new DynamicAgentRegistry(_crewId);
        var agent = await service.SpawnAgentAsync(new AgentSpawnRequest(_role, _goal, _crewId), registry, TestContext.Current.CancellationToken);

        var result = await service.TerminateAgentAsync(agent.Id, registry, "done");

        Assert.True(result);
        Assert.True(loggerFactory.Logger.HasEntry(e =>
            e.Level == LogLevel.Information && e.Message.Contains("terminated")));
    }

    [Fact]
    public async Task TerminateAgentAsync_WithNullReason_UsesDefaultReason()
    {
        var service = new DynamicAgentService(new StubFactory());
        var registry = new DynamicAgentRegistry(_crewId);
        var agent = await service.SpawnAgentAsync(new AgentSpawnRequest(_role, _goal, _crewId), registry, TestContext.Current.CancellationToken);

        var result = await service.TerminateAgentAsync(agent.Id, registry, reason: null);

        Assert.True(result);
        Assert.False(registry.IsAgentActive(agent.Id));
    }

    [Fact]
    public async Task AllowsDynamicAgentsAsync_WithLogger_LogsDebug()
    {
        using var loggerFactory = new RecordingLoggerFactory();
        var service = new DynamicAgentService(
            new StubFactory(),
            new TypedLogger<DynamicAgentService>(loggerFactory.Logger));

        var result = await service.AllowsDynamicAgentsAsync(_crewId);

        Assert.True(result);
        Assert.True(loggerFactory.Logger.HasEntry(e =>
            e.Level == LogLevel.Debug && e.Message.Contains("policy")));
    }

    [Fact]
    public async Task GetMaxConcurrentDynamicAgentsAsync_WithLogger_LogsDebug()
    {
        using var loggerFactory = new RecordingLoggerFactory();
        var service = new DynamicAgentService(
            new StubFactory(),
            new TypedLogger<DynamicAgentService>(loggerFactory.Logger));

        var result = await service.GetMaxConcurrentDynamicAgentsAsync(_crewId);

        Assert.Null(result);
        Assert.True(loggerFactory.Logger.HasEntry(e =>
            e.Level == LogLevel.Debug && e.Message.Contains("max concurrent")));
    }

    [Fact]
    public async Task AllowsDynamicAgentsAsync_WithGlobalDefaultDisabled_ReturnsFalse()
    {
        // Global options forbid dynamic agents entirely.
        var options = new DynamicAgentServiceOptions { AllowDynamicAgentsByDefault = false };
        var service = new DynamicAgentService(new StubFactory(), logger: null, options: options);

        var result = await service.AllowsDynamicAgentsAsync(_crewId);

        Assert.False(result);
    }

    [Fact]
    public async Task AllowsDynamicAgentsAsync_WithPerCrewOverrideDisabled_ForbidsOnlyThatCrew()
    {
        // Global default allows, but one crew is explicitly forbidden.
        var options = new DynamicAgentServiceOptions { AllowDynamicAgentsByDefault = true };
        options.CrewOverrides[_crewId.Value.ToString()] =
            new DynamicAgentCrewPolicy { AllowDynamicAgents = false };
        var service = new DynamicAgentService(new StubFactory(), logger: null, options: options);

        var deniedCrew = await service.AllowsDynamicAgentsAsync(_crewId);
        var otherCrew = await service.AllowsDynamicAgentsAsync(CrewId.Create());

        Assert.False(deniedCrew);
        Assert.True(otherCrew);
    }

    [Fact]
    public async Task GetMaxConcurrentDynamicAgentsAsync_WithGlobalDefaultCap_ReturnsCap()
    {
        var options = new DynamicAgentServiceOptions { DefaultMaxConcurrentDynamicAgents = 3 };
        var service = new DynamicAgentService(new StubFactory(), logger: null, options: options);

        var result = await service.GetMaxConcurrentDynamicAgentsAsync(_crewId);

        Assert.Equal(3, result);
    }

    [Fact]
    public async Task GetMaxConcurrentDynamicAgentsAsync_WithPerCrewOverrideCap_CapsOnlyThatCrew()
    {
        // Global is unlimited; one crew is capped at 1.
        var options = new DynamicAgentServiceOptions { DefaultMaxConcurrentDynamicAgents = null };
        options.CrewOverrides[_crewId.Value.ToString()] =
            new DynamicAgentCrewPolicy { MaxConcurrentDynamicAgents = 1 };
        var service = new DynamicAgentService(new StubFactory(), logger: null, options: options);

        var cappedCrew = await service.GetMaxConcurrentDynamicAgentsAsync(_crewId);
        var otherCrew = await service.GetMaxConcurrentDynamicAgentsAsync(CrewId.Create());

        Assert.Equal(1, cappedCrew);
        Assert.Null(otherCrew);
    }
}
