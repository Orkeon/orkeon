using Orkeon.Application.Context;
using Orkeon.Application.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Common;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;

namespace Orkeon.Application.Tests.Services;

/// <summary>
/// Tests for SimpleExecutionContext record.
/// </summary>
public class SimpleExecutionContextTests
{
    #region Test Doubles

    private class TestMemoryScope : IMemoryScope
    {
        public string AgentId { get; }
        public string ScopeId { get; }

        public TestMemoryScope(string agentId = "test-agent", string? scopeId = null)
        {
            AgentId = agentId;
            ScopeId = scopeId ?? Guid.NewGuid().ToString();
        }

        public async System.Threading.Tasks.Task<T> ExecuteInScopeAsync<T>(Func<System.Threading.Tasks.Task<T>> operation)
        {
            return await operation();
        }

        public async System.Threading.Tasks.Task ExecuteInScopeAsync(Func<System.Threading.Tasks.Task> operation)
        {
            await operation();
        }

        public void Dispose() { }
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void ShouldCreateContext_WhenValidParametersProvided()
    {
        // Arrange
        var crewId = CrewId.From(Guid.NewGuid());
        var variables = new Dictionary<string, string>
        {
            { "key1", "value1" },
            { "key2", "value2" }
        };
        using var memory = new TestMemoryScope();
        var previousOutputs = new List<TaskOutput>
        {
            new TaskOutput(TaskId1, AgentId1, "Output 1", DateTime.UtcNow, true, TimeSpan.FromSeconds(5))
        };
        using var cts = new CancellationTokenSource();

        // Act
        var context = new SimpleExecutionContext(
            crewId, variables, memory, previousOutputs, cts.Token);

        // Assert
        Assert.NotNull(context);
        Assert.Equal(crewId, context.CrewId);
        Assert.Equal(2, context.Variables.Count);
        Assert.Equal("value1", context.Variables["key1"]);
        Assert.Equal("value2", context.Variables["key2"]);
        Assert.Equal(memory, context.Memory);
        Assert.Single(context.PreviousOutputs);
        Assert.Equal("Output 1", context.PreviousOutputs[0].Content);
        Assert.Equal(cts.Token, context.CancellationToken);
    }

    #endregion

    #region Properties Access Tests

    [Fact]
    public void ShouldExposeProperties_WhenAccessed()
    {
        // Arrange
        var crewId = CrewId.From(Guid.NewGuid());
        var variables = new Dictionary<string, string> { { "topic", "AI" } };
        using var memory = new TestMemoryScope("agent-42", "scope-42");
        var outputs = new List<TaskOutput>();
        var token = CancellationToken.None;

        var context = new SimpleExecutionContext(crewId, variables, memory, outputs, token);

        // Act & Assert
        Assert.Equal(crewId, context.CrewId);
        Assert.Same(variables, context.Variables);
        Assert.Same(memory, context.Memory);
        Assert.Same(outputs, context.PreviousOutputs);
        Assert.Equal(token, context.CancellationToken);
    }

    [Fact]
    public void ShouldAllowEmptyVariables_WhenCreated()
    {
        // Arrange & Act
        using var memoryScope = new TestMemoryScope();
        var context = new SimpleExecutionContext(
            CrewId.From(Guid.NewGuid()),
            [],
            memoryScope,
            []);

        // Assert
        Assert.Empty(context.Variables);
    }

    [Fact]
    public void ShouldAllowEmptyPreviousOutputs_WhenCreated()
    {
        // Arrange & Act
        using var memoryScope = new TestMemoryScope();
        var context = new SimpleExecutionContext(
            CrewId.From(Guid.NewGuid()),
            [],
            memoryScope,
            []);

        // Assert
        Assert.Empty(context.PreviousOutputs);
    }

    [Fact]
    public void ShouldDefaultCancellationToken_WhenNotProvided()
    {
        // Arrange & Act
        using var memoryScope = new TestMemoryScope();
        var context = new SimpleExecutionContext(
            CrewId.From(Guid.NewGuid()),
            [],
            memoryScope,
            []);

        // Assert -- default value from the record parameter
        Assert.Equal(CancellationToken.None, context.CancellationToken);
    }

    [Fact]
    public void ShouldSupportRecordEquality_WhenCompared()
    {
        // Arrange
        var crewId = CrewId.From(Guid.NewGuid());
        var variables = new Dictionary<string, string> { { "k", "v" } };
        using var memory = new TestMemoryScope();
        var outputs = new List<TaskOutput>();

        var context1 = new SimpleExecutionContext(crewId, variables, memory, outputs);
        var context2 = new SimpleExecutionContext(crewId, variables, memory, outputs);

        // Act & Assert -- records use value equality on their parameters
        Assert.Equal(context1, context2);
    }

    [Fact]
    public void ShouldSupportWithExpression_WhenCreatingModifiedCopy()
    {
        // Arrange
        var originalCrew = CrewId.From(Guid.NewGuid());
        var newCrew = CrewId.From(Guid.NewGuid());
        using var memoryScope = new TestMemoryScope();
        var context = new SimpleExecutionContext(
            originalCrew,
            new Dictionary<string, string> { { "original", "value" } },
            memoryScope,
            []);

        // Act -- record with-expression
        var modified = context with { CrewId = newCrew };

        // Assert
        Assert.Equal(newCrew, modified.CrewId);
        Assert.Equal(originalCrew, context.CrewId);
        Assert.Equal(context.Variables, modified.Variables);
    }

    #endregion
}
