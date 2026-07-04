using Orkeon.Domain.Common;
using Orkeon.Application.Execution;
using Orkeon.Application.Interfaces.Ports;
using ExecutionContext = Orkeon.Application.Execution.ExecutionContext;
using static Orkeon.Tests.Shared.Constants.TestDataConstants;

namespace Orkeon.Application.Tests.Common;

public sealed class ExecutionContextTests : IDisposable
{
    private readonly CrewId _crewId;
    private readonly TestMemoryScope _memoryScope;

    public ExecutionContextTests()
    {
        _crewId = CrewId.Create();
        _memoryScope = new TestMemoryScope();
    }

    public void Dispose() => _memoryScope.Dispose();

    [Fact]
    public void ShouldCreateContext_WhenConstructingWithValidParameters()
    {
        // Arrange
        var data = new TestData { Value = "test", Number = 42 };
        var outputs = new List<TaskOutput>
        {
            new("task1", "agent1", "Output 1", DateTime.UtcNow, true, TimeSpan.FromSeconds(1))
        };
        var cancellationToken = new CancellationToken();

        // Act
        var context = new ExecutionContext<TestData>(
            _crewId,
            data,
            _memoryScope,
            outputs,
            cancellationToken);

        // Assert
        Assert.NotNull(context);
        Assert.Equal(_crewId, context.CrewId);
        Assert.Equal(data, context.Data);
        Assert.Equal(_memoryScope, context.Memory);
        Assert.Equal(outputs, context.PreviousOutputs);
        Assert.Equal(cancellationToken, context.CancellationToken);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullCrewId()
    {
        // Arrange
        var data = new TestData();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ExecutionContext<TestData>(
                null!,
                data,
                _memoryScope,
                [],
                CancellationToken.None));
        Assert.Equal("crewId", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullData()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ExecutionContext<TestData>(
                _crewId,
                null!,
                _memoryScope,
                [],
                CancellationToken.None));
        Assert.Equal("data", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullMemory()
    {
        // Arrange
        var data = new TestData();

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new ExecutionContext<TestData>(
                _crewId,
                data,
                null!,
                [],
                CancellationToken.None));
        Assert.Equal("memory", exception.ParamName);
    }

    [Fact]
    public void ShouldUseEmptyList_WhenConstructingWithNullPreviousOutputs()
    {
        // Arrange
        var data = new TestData();

        // Act
        var context = new ExecutionContext<TestData>(
            _crewId,
            data,
            _memoryScope,
            null!,
            CancellationToken.None);

        // Assert
        Assert.NotNull(context.PreviousOutputs);
        Assert.Empty(context.PreviousOutputs);
    }

    [Fact]
    public void ShouldModifyDataInPlace_WhenUsingUpdateData()
    {
        // Arrange
        var data = new TestData { Value = "initial", Number = 1 };
        var context = ExecutionContext.Create(_crewId, data, _memoryScope, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        context.UpdateData(d =>
        {
            d.Value = "updated";
            d.Number = 42;
        });

        // Assert
        Assert.Equal("updated", context.Data.Value);
        Assert.Equal(42, context.Data.Number);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingUpdateDataWithNullAction()
    {
        // Arrange
        var context = ExecutionContext.Create(_crewId, new TestData(), _memoryScope, cancellationToken: TestContext.Current.CancellationToken);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            context.UpdateData(null!));
        Assert.Equal("updateAction", exception.ParamName);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldBeThreadSafe_WhenUsingUpdateData()
    {
        // Arrange
        var data = new TestData { Number = 0 };
        var context = ExecutionContext.Create(_crewId, data, _memoryScope, cancellationToken: TestContext.Current.CancellationToken);
        var updates = 1000;
        var tasks = new List<System.Threading.Tasks.Task>();

        // Act
        for (int i = 0; i < updates; i++)
        {
            tasks.Add(System.Threading.Tasks.Task.Run(() => context.UpdateData(d => d.Number++), TestContext.Current.CancellationToken));
        }
        await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        Assert.Equal(updates, context.Data.Number);
    }

    [Fact]
    public void ShouldCreateNewContextWithTransformedData_WhenTransforming()
    {
        // Arrange
        var originalData = new TestData { Value = "test", Number = 42 };
        var outputs = new List<TaskOutput>
        {
            new("task1", "agent1", "Output", DateTime.UtcNow, true, TimeSpan.FromSeconds(1))
        };
        var context = new ExecutionContext<TestData>(
            _crewId,
            originalData,
            _memoryScope,
            outputs,
            CancellationToken.None);

        // Act
        var transformed = context.Transform<TransformedData>(data =>
            new TransformedData { Combined = $"{data.Value}-{data.Number}", IsValid = true });

        // Assert
        Assert.NotSame(context, transformed);
        Assert.Equal(_crewId, transformed.CrewId);
        Assert.Equal("test-42", transformed.Data.Combined);
        Assert.True(transformed.Data.IsValid);
        Assert.Equal(outputs, transformed.PreviousOutputs);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenTransformingWithNullTransformer()
    {
        // Arrange
        var context = ExecutionContext.Create(_crewId, new TestData(), _memoryScope, cancellationToken: TestContext.Current.CancellationToken);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            context.Transform<TransformedData>(null!));
        Assert.Equal("transformer", exception.ParamName);
    }

    [Fact]
    public void ShouldCreateNewContextWithAdditionalOutput_WhenUsingWithOutput()
    {
        // Arrange
        var originalOutputs = new List<TaskOutput>
        {
            new("task1", "agent1", "Output 1", DateTime.UtcNow, true, TimeSpan.FromSeconds(1))
        };
        var context = new ExecutionContext<TestData>(
            _crewId,
            new TestData(),
            _memoryScope,
            originalOutputs,
            CancellationToken.None);
        var newOutput = new TaskOutput("task2", "agent2", "Output 2", DateTime.UtcNow, true, TimeSpan.FromSeconds(2));

        // Act
        var newContext = context.WithOutput(newOutput);

        // Assert
        Assert.NotSame(context, newContext);
        Assert.Single(context.PreviousOutputs);
        Assert.Equal(2, newContext.PreviousOutputs.Count);
        Assert.Equal(newOutput, newContext.PreviousOutputs[^1]);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenUsingWithOutputWithNullOutput()
    {
        // Arrange
        var context = ExecutionContext.Create(_crewId, new TestData(), _memoryScope, cancellationToken: TestContext.Current.CancellationToken);

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            context.WithOutput(null!));
        Assert.Equal("output", exception.ParamName);
    }

    [Fact]
    public void ShouldReturnCurrentData_WhenUsingGetDataSnapshot()
    {
        // Arrange
        var data = new TestData { Value = "test", Number = 42 };
        var context = ExecutionContext.Create(_crewId, data, _memoryScope, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var snapshot = context.DataSnapshot;

        // Assert
        Assert.Same(data, snapshot);
        Assert.Equal("test", snapshot.Value);
        Assert.Equal(42, snapshot.Number);
    }

    [Fact]
    public void ShouldReflectCancellationToken_WhenUsingIsCancelled()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var context = ExecutionContext.Create(
            _crewId,
            new TestData(),
            _memoryScope,
            null,
            cts.Token);

        // Assert initial state
        Assert.False(context.IsCancelled);

        // Act
        cts.Cancel();

        // Assert after cancellation
        Assert.True(context.IsCancelled);
    }

    [Fact]
    public void ShouldReturnLast_WhenUsingLastOutputWithOutputs()
    {
        // Arrange
        var outputs = new List<TaskOutput>
        {
            new("task1", "agent1", "Output 1", DateTime.UtcNow.AddMinutes(-2), true, TimeSpan.FromSeconds(1)),
            new("task2", "agent2", "Output 2", DateTime.UtcNow.AddMinutes(-1), true, TimeSpan.FromSeconds(2)),
            new("task3", "agent3", "Output 3", DateTime.UtcNow, true, TimeSpan.FromSeconds(3))
        };
        var context = new ExecutionContext<TestData>(
            _crewId,
            new TestData(),
            _memoryScope,
            outputs,
            CancellationToken.None);

        // Act
        var lastOutput = context.LastOutput;

        // Assert
        Assert.NotNull(lastOutput);
        Assert.Equal("task3", lastOutput.TaskId);
        Assert.Equal("Output 3", lastOutput.Content);
    }

    [Fact]
    public void ShouldReturnNull_WhenUsingLastOutputWithNoOutputs()
    {
        // Arrange
        var context = ExecutionContext.Create(_crewId, new TestData(), _memoryScope, cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var lastOutput = context.LastOutput;

        // Assert
        Assert.Null(lastOutput);
    }

    [Fact]
    public void ShouldFilterByAgentId_WhenUsingGetOutputsFromAgent()
    {
        // Arrange
        var outputs = new List<TaskOutput>
        {
            new("task1", "agent1", "Output 1", DateTime.UtcNow, true, TimeSpan.FromSeconds(1)),
            new("task2", "agent2", "Output 2", DateTime.UtcNow, true, TimeSpan.FromSeconds(2)),
            new("task3", "agent1", "Output 3", DateTime.UtcNow, true, TimeSpan.FromSeconds(3)),
            new("task4", "agent3", "Output 4", DateTime.UtcNow, false, TimeSpan.FromSeconds(4))
        };
        var context = new ExecutionContext<TestData>(
            _crewId,
            new TestData(),
            _memoryScope,
            outputs,
            CancellationToken.None);

        // Act
        var agent1Outputs = context.GetOutputsFromAgent("agent1").ToList();

        // Assert
        Assert.Equal(2, agent1Outputs.Count);
        Assert.All(agent1Outputs, output => Assert.Equal("agent1", output.AgentId));
    }

    [Fact]
    public void ShouldFilterSuccessful_WhenGettingSuccessfulOutputs()
    {
        // Arrange
        var outputs = new List<TaskOutput>
        {
            new("task1", "agent1", "Success 1", DateTime.UtcNow, true, TimeSpan.FromSeconds(1)),
            new("task2", "agent2", "Failure 1", DateTime.UtcNow, false, TimeSpan.FromSeconds(2)),
            new("task3", "agent1", "Success 2", DateTime.UtcNow, true, TimeSpan.FromSeconds(3)),
            new("task4", "agent3", "Failure 2", DateTime.UtcNow, false, TimeSpan.FromSeconds(4))
        };
        var context = new ExecutionContext<TestData>(
            _crewId,
            new TestData(),
            _memoryScope,
            outputs,
            CancellationToken.None);

        // Act
        var successfulOutputs = context.GetSuccessfulOutputs().ToList();

        // Assert
        Assert.Equal(2, successfulOutputs.Count);
        Assert.All(successfulOutputs, output => Assert.True(output.Success));
    }

    [Fact]
    public void ShouldCreateContext_WhenCreatingWithGenericMethod()
    {
        // Arrange
        var data = new TestData { Value = "created", Number = 123 };
        var outputs = new List<TaskOutput>();

        // Act
        var context = ExecutionContext.Create(
            _crewId,
            data,
            _memoryScope,
            outputs,
            CancellationToken.None);

        // Assert
        Assert.NotNull(context);
        Assert.Equal(data, context.Data);
        Assert.Equal(outputs, context.PreviousOutputs);
    }

    [Fact]
    public void ShouldCreateContextWithDefaultData_WhenCreatingEmpty()
    {
        // Act
        var context = ExecutionContext.CreateEmpty<TestData>(
            _crewId,
            _memoryScope,
            CancellationToken.None);

        // Assert
        Assert.NotNull(context);
        Assert.NotNull(context.Data);
        Assert.Null(context.Data.Value);
        Assert.Equal(0, context.Data.Number);
        Assert.Empty(context.PreviousOutputs);
    }

    [Fact]
    public void ShouldReturnContent_WhenUsingTaskOutputUsingRawOutput()
    {
        // Arrange
        var output = new TaskOutput(
            "task1",
            "agent1",
            TestContent,
            DateTime.UtcNow,
            true,
            TimeSpan.FromSeconds(1));

        // Act & Assert
        Assert.Equal(TestContent, output.Content);
        Assert.Equal(TestContent, output.RawOutput);
    }

    [Fact]
    public void ShouldInitializeList_WhenUsingTaskOutputWithToolsUsed()
    {
        // Arrange
        var tools = new List<Orkeon.Domain.Tools.ToolUsage>
        {
            new Orkeon.Domain.Tools.ToolUsage(
                new Orkeon.Domain.Tools.ToolCallIdentity("tool1", "tool1", "agent1", "task1"),
                duration: TimeSpan.FromSeconds(0.5),
                success: true)
        };
        var output = new TaskOutput(
            "task1",
            "agent1",
            "Test",
            DateTime.UtcNow,
            true,
            TimeSpan.FromSeconds(1),
            tools);

        // Assert
        Assert.Single(output.ToolsUsed);
        Assert.Equal("tool1", output.ToolsUsed[0].ToolName);
    }

    [Fact]
    public void ShouldHaveEmptyList_WhenUsingTaskOutputWithNullToolsUsed()
    {
        // Arrange
        var output = new TaskOutput(
            "task1",
            "agent1",
            "Test",
            DateTime.UtcNow,
            true,
            TimeSpan.FromSeconds(1),
            null);

        // Assert
        Assert.NotNull(output.ToolsUsed);
        Assert.Empty(output.ToolsUsed);
    }

    // Test helper classes
    private class TestData
    {
        public string? Value { get; set; }
        public int Number { get; set; }
    }

    private class TransformedData
    {
        public string Combined { get; set; } = string.Empty;
        public bool IsValid { get; set; }
    }

    private class TestMemoryScope : IMemoryScope
    {
        public string ScopeId => "test-scope";
        public string AgentId => "test-agent";

        public System.Threading.Tasks.Task<T> ExecuteInScopeAsync<T>(Func<System.Threading.Tasks.Task<T>> operation) => operation();
        public System.Threading.Tasks.Task ExecuteInScopeAsync(Func<System.Threading.Tasks.Task> operation) => operation();
        public void Dispose() { }
    }
}
