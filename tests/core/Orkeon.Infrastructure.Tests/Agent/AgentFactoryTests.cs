using Xunit;
using Orkeon.Infrastructure.Agent;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Tools;
using Microsoft.Extensions.Logging;
using Orkeon.Tests.Shared.Doubles;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;
using ParameterSchema = Orkeon.Domain.Tools.Protocol.ParameterSchema;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Infrastructure.Tests.Agent;

/// <summary>
/// Tests for AgentFactory implementation.
/// </summary>
public class AgentFactoryTests
{
    private readonly CrewId _crewId = CrewId.Create();
    private readonly AgentRole _role = AgentRole.From("Engineer");
    private readonly AgentGoal _goal = AgentGoal.From("Build systems");

    [Fact]
    public void Constructor_CreatesInstance()
    {
        // Act
        var factory = new AgentFactory();

        // Assert
        Assert.NotNull(factory);
    }

    [Fact]
    public void CreateAgent_WithValidRequest_CreatesAgent()
    {
        // Arrange
        var factory = new AgentFactory();
        var request = new AgentSpawnRequest(_role, _goal, _crewId);

        // Act
        var agent = factory.CreateAgent(request);

        // Assert
        Assert.NotNull(agent);
        Assert.Equal(_role, agent.Role);
        Assert.Equal(_goal, agent.Goal);
        Assert.False(agent.AllowDelegation);
    }

    [Fact]
    public void CreateAgent_WithNullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        var factory = new AgentFactory();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            factory.CreateAgent(null!));
    }

    [Fact]
    public void CreateAgent_WithAllOptions_CreatesConfiguredAgent()
    {
        // Arrange
        var factory = new AgentFactory();
        var backstory = AgentBackstory.From("Experienced engineer");

        var request = new AgentSpawnRequest(
            _role,
            _goal,
            _crewId,
            backstory,
            allowDelegation: true,
            maxIterations: 8,
            maxRpm: 250,
            verbose: true,
            maxExecutionTime: TimeoutStandard,
            cacheEnabled: false,
            tools: new ITool[] { new FakeTool("CodeAnalyzer") });

        // Act
        var agent = factory.CreateAgent(request);

        // Assert
        Assert.NotNull(agent);
        Assert.Equal(backstory, agent.Backstory);
        Assert.True(agent.AllowDelegation);
        Assert.Equal(8, agent.MaxIterations);
        Assert.Equal(250, agent.MaxRpm);
        Assert.True(agent.Verbose);
        Assert.Equal(TimeoutStandard, agent.MaxExecutionTime);
        Assert.False(agent.CacheEnabled);
        Assert.Single(agent.Tools);
        Assert.Equal("CodeAnalyzer", agent.Tools[0].Name);
    }

    [Fact]
    public async Task CreateAgentAsync_WithValidRequest_CreatesAgent()
    {
        // Arrange
        var factory = new AgentFactory();
        var request = new AgentSpawnRequest(_role, _goal, _crewId);

        // Act
        var agent = await factory.CreateAgentAsync(request);

        // Assert
        Assert.NotNull(agent);
        Assert.Equal(_role, agent.Role);
        Assert.Equal(_goal, agent.Goal);
    }

    [Fact]
    public async Task CreateAgentAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        var factory = new AgentFactory();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            factory.CreateAgentAsync(null!));
    }

    [Fact]
    public async Task CreateAgentAsync_WithCancellationToken_RespectsCancellation()
    {
        // Arrange
        var factory = new AgentFactory();
        var request = new AgentSpawnRequest(_role, _goal, _crewId);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            factory.CreateAgentAsync(request, cts.Token));
    }

    [Fact]
    public void CreateAgent_CreatesUniqueAgents()
    {
        // Arrange
        var factory = new AgentFactory();
        var request1 = new AgentSpawnRequest(_role, _goal, _crewId);
        var request2 = new AgentSpawnRequest(_role, _goal, _crewId);

        // Act
        var agent1 = factory.CreateAgent(request1);
        var agent2 = factory.CreateAgent(request2);

        // Assert
        Assert.NotEqual(agent1.Id, agent2.Id);
    }

    [Fact]
    public void CreateAgent_WithLogger_LogsCreation()
    {
        // Arrange
        var loggerFactory = new RecordingLoggerFactory();
        var logger = loggerFactory.CreateLogger("AgentFactory") as RecordingLogger
            ?? throw new InvalidOperationException("Expected RecordingLogger.");
        var typedLogger = new TypedLoggerAdapter<AgentFactory>(logger);
        var factory = new AgentFactory(typedLogger);
        var request = new AgentSpawnRequest(_role, _goal, _crewId);

        // Act
        var agent = factory.CreateAgent(request);

        // Assert
        Assert.True(logger.HasEntry(e => e.Level == LogLevel.Information
            && e.Message.Contains("Dynamic agent created")));
    }

    [Fact]
    public void CreateAgent_WithInvalidRequest_LogsError()
    {
        // Arrange
        var loggerFactory = new RecordingLoggerFactory();
        var typedLogger = new TypedLoggerAdapter<AgentFactory>(loggerFactory.Logger);
        var factory = new AgentFactory(typedLogger);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            factory.CreateAgent(null!));
    }

    private sealed class FakeTool : ITool
    {
        public FakeTool(string name)
        {
            Name = name;
            Schema = new ToolSchema(name, "fake tool", new Dictionary<string, ParameterSchema>());
        }

        public string Name { get; }
        public string Description => "fake tool";
        public ToolSchema Schema { get; }

        public System.Threading.Tasks.Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new ToolCallResponse(true, null, null));

        public System.Threading.Tasks.Task<ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(ToolResult.CreateSuccess(string.Empty));

        public bool ValidateInput(string input) => true;
    }

    /// <summary>Adapts a non-generic <see cref="RecordingLogger"/> to <see cref="ILogger{T}"/>.</summary>
    private sealed class TypedLoggerAdapter<T> : ILogger<T>
    {
        private readonly RecordingLogger _inner;
        public TypedLoggerAdapter(RecordingLogger inner) { _inner = inner; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
