using Microsoft.Extensions.Logging;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Agent;
using Orkeon.Tests.Shared.Doubles;
using ParameterSchema = Orkeon.Domain.Tools.Protocol.ParameterSchema;
using ToolCallRequest = Orkeon.Domain.Tools.Protocol.ToolCallRequest;
using ToolCallResponse = Orkeon.Domain.Tools.Protocol.ToolCallResponse;
using ToolSchema = Orkeon.Domain.Tools.Protocol.ToolSchema;

namespace Orkeon.Infrastructure.Tests.CovAgentMcp;

/// <summary>
/// Coverage tests for <see cref="AgentFactory"/> error and logging paths.
/// </summary>
public sealed class CovAgentMcp_AgentFactoryTests
{
    private readonly CrewId _crewId = CrewId.Create();
    private readonly AgentRole _role = AgentRole.From("Builder");
    private readonly AgentGoal _goal = AgentGoal.From("Build");

    private sealed class TypedLogger<T> : ILogger<T>
    {
        private readonly RecordingLogger _inner;
        public TypedLogger(RecordingLogger inner) => _inner = inner;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);
        public void Log<TState>(LogLevel l, EventId e, TState s, Exception? ex, Func<TState, Exception?, string> f)
            => _inner.Log(l, e, s, ex, f);
    }

    private sealed class FakeTool : ITool
    {
        public FakeTool(string name)
        {
            Name = name;
            Schema = new ToolSchema(name, "fake", new Dictionary<string, ParameterSchema>());
        }
        public string Name { get; }
        public string Description => "fake";
        public ToolSchema Schema { get; }
        public Task<ToolCallResponse> CallAsync(ToolCallRequest request, CancellationToken ct = default)
            => Task.FromResult(new ToolCallResponse(true, null, null));
        public Task<ToolResult> ExecuteAsync(string input, CancellationToken ct = default)
            => Task.FromResult(ToolResult.CreateSuccess(string.Empty));
        public bool ValidateInput(string input) => true;
    }

    [Fact]
    public void CreateAgent_WithoutLogger_DoesNotThrow()
    {
        var factory = new AgentFactory();
        var request = new AgentSpawnRequest(_role, _goal, _crewId);

        var agent = factory.CreateAgent(request);

        Assert.NotNull(agent);
    }

    [Fact]
    public async Task CreateAgentAsync_WithTools_AssignsTools()
    {
        var factory = new AgentFactory();
        var request = new AgentSpawnRequest(
            _role, _goal, _crewId, tools: new ITool[] { new FakeTool("toolA"), new FakeTool("toolB") });

        var agent = await factory.CreateAgentAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(2, agent.Tools.Count);
    }

    [Fact]
    public async Task CreateAgentAsync_WithoutTools_HasNoTools()
    {
        var factory = new AgentFactory();
        var request = new AgentSpawnRequest(_role, _goal, _crewId);

        var agent = await factory.CreateAgentAsync(request, TestContext.Current.CancellationToken);

        Assert.Empty(agent.Tools);
    }

    [Fact]
    public void CreateAgent_WithLoggerInfoDisabled_StillCreates()
    {
        // RecordingLogger.IsEnabled always returns true; this exercises the
        // logging branch with a real category logger instance.
        using var loggerFactory = new RecordingLoggerFactory();
        var factory = new AgentFactory(new TypedLogger<AgentFactory>(loggerFactory.Logger));
        var request = new AgentSpawnRequest(_role, _goal, _crewId);

        var agent = factory.CreateAgent(request);

        Assert.NotNull(agent);
        Assert.True(loggerFactory.Logger.HasEntry(e =>
            e.Level == LogLevel.Information && e.Message.Contains("created")));
    }

    [Fact]
    public void CreateAgent_WithNullRequestAndLogger_DoesNotLogError()
    {
        // Null guard fires before the try/catch, so no error is logged.
        using var loggerFactory = new RecordingLoggerFactory();
        var factory = new AgentFactory(new TypedLogger<AgentFactory>(loggerFactory.Logger));

        Assert.Throws<ArgumentNullException>(() => factory.CreateAgent(null!));
        Assert.False(loggerFactory.Logger.HasEntry(e => e.Level == LogLevel.Error));
    }
}
