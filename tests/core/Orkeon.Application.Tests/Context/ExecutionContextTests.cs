using Orkeon.Domain.Common;
using Orkeon.Domain.Tools;
using Orkeon.Application.Execution;
using Orkeon.Application.Interfaces.Ports;
using ExecutionContext = Orkeon.Application.Execution.ExecutionContext;
using static Orkeon.Tests.Shared.Constants.TestEntityIds;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.Contexts;

public class ExecutionContextTests
{
    #region Test Doubles

    private class TestMemoryScope : IMemoryScope
    {
        public string AgentId { get; }
        public string ScopeId { get; }
        public bool IsDisposed { get; private set; }
        public List<Func<System.Threading.Tasks.Task>> ExecutedOperations { get; } = [];

        public TestMemoryScope(string agentId, string? scopeId = null)
        {
            AgentId = agentId;
            ScopeId = scopeId ?? Guid.NewGuid().ToString();
        }

        public async System.Threading.Tasks.Task<T> ExecuteInScopeAsync<T>(Func<System.Threading.Tasks.Task<T>> operation)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, typeof(TestMemoryScope));

            ExecutedOperations.Add(async () => await operation());
            return await operation();
        }

        public async System.Threading.Tasks.Task ExecuteInScopeAsync(Func<System.Threading.Tasks.Task> operation)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, typeof(TestMemoryScope));

            ExecutedOperations.Add(operation);
            await operation();
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private class TestData
    {
        public string Name { get; set; } = "Test";
        public int Value { get; set; } = 42;
        public List<string> Items { get; set; } = [];
    }

    private class TransformedData
    {
        public string TransformedName { get; set; } = string.Empty;
        public double TransformedValue { get; set; }
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void ShouldInitializeAllProperties_WhenConstructing()
    {
        // Arrange
        var crewId = CrewId.Create();
        var data = new TestData { Name = "InitialData", Value = 100 };
        using var memory = new TestMemoryScope(AgentId1);
        var outputs = new List<TaskOutput>
        {
            new TaskOutput(
                TaskId: TaskId1,
                AgentId: AgentId1,
                Content: "Output 1",
                CompletedAt: DateTime.UtcNow,
                Success: true,
                ExecutionTime: TimeSpan.FromSeconds(10))
        };
        using var cts = new CancellationTokenSource();

        // Act
        var context = new ExecutionContext<TestData>(
            crewId, data, memory, outputs, cts.Token);

        // Assert
        Assert.Equal(crewId, context.CrewId);
        Assert.Equal("InitialData", context.Data.Name);
        Assert.Equal(100, context.Data.Value);
        Assert.Equal(memory, context.Memory);
        Assert.Single(context.PreviousOutputs);
        Assert.Equal("Output 1", context.PreviousOutputs[0].Content);
        Assert.Equal(cts.Token, context.CancellationToken);
    }

    [Fact]
    public void ShouldThrow_WhenConstructingWithNullCrewId()
    {
        // Arrange
        var data = new TestData();
        using var memory = new TestMemoryScope(AgentId1);

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new ExecutionContext<TestData>(null!, data, memory, [], CancellationToken.None));
        Assert.Equal("crewId", ex.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructingWithNullData()
    {
        // Arrange
        var crewId = CrewId.Create();
        using var memory = new TestMemoryScope(AgentId1);

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new ExecutionContext<TestData>(crewId, null!, memory, [], CancellationToken.None));
        Assert.Equal("data", ex.ParamName);
    }

    [Fact]
    public void ShouldThrow_WhenConstructingWithNullMemory()
    {
        // Arrange
        var crewId = CrewId.Create();
        var data = new TestData();

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new ExecutionContext<TestData>(crewId, data, null!, [], CancellationToken.None));
        Assert.Equal("memory", ex.ParamName);
    }

    [Fact]
    public void ShouldUseEmptyList_WhenConstructingWithNullPreviousOutputs()
    {
        // Arrange
        var crewId = CrewId.Create();
        var data = new TestData();
        using var memory = new TestMemoryScope(AgentId1);

        // Act
        var context = new ExecutionContext<TestData>(
            crewId, data, memory, null!, CancellationToken.None);

        // Assert
        Assert.NotNull(context.PreviousOutputs);
        Assert.Empty(context.PreviousOutputs);
    }

    #endregion

    #region UpdateData Tests

    [Fact]
    public void ShouldModifyDataInPlace_WhenUsingUpdateData()
    {
        // Arrange
        var context = CreateTestContext(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        context.UpdateData(data =>
        {
            data.Name = "Updated";
            data.Value = 200;
            data.Items.Add("Item1");
        });

        // Assert
        Assert.Equal("Updated", context.Data.Name);
        Assert.Equal(200, context.Data.Value);
        Assert.Single(context.Data.Items);
        Assert.Equal("Item1", context.Data.Items[0]);
    }

    [Fact]
    public void ShouldThrow_WhenUsingUpdateDataWithNullAction()
    {
        // Arrange
        var context = CreateTestContext(cancellationToken: TestContext.Current.CancellationToken);

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            context.UpdateData(null!));
        Assert.Equal("updateAction", ex.ParamName);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldBeThreadSafe_WhenUsingUpdateData()
    {
        // Arrange
        var context = CreateTestContext(cancellationToken: TestContext.Current.CancellationToken);
        var updates = 1000;
        var tasks = new List<System.Threading.Tasks.Task>();

        // Act
        for (int i = 0; i < updates; i++)
        {
            var localI = i;
            tasks.Add(System.Threading.Tasks.Task.Run(() =>
            {
                context.UpdateData(data => data.Value += 1);
            }, TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks.ToArray());

        // Assert
        Assert.Equal(42 + updates, context.Data.Value);
    }

    #endregion

    #region Transform Tests

    [Fact]
    public void ShouldCreateNewContextWithTransformedData_WhenTransforming()
    {
        // Arrange
        var originalContext = CreateTestContext(cancellationToken: TestContext.Current.CancellationToken);
        originalContext.Data.Name = "Original";
        originalContext.Data.Value = 100;

        // Act
        var transformedContext = originalContext.Transform<TransformedData>(data =>
            new TransformedData
            {
                TransformedName = data.Name.ToUpper(),
                TransformedValue = data.Value * 2.5
            });

        // Assert
        Assert.False(ReferenceEquals(originalContext, transformedContext));
        Assert.Equal("ORIGINAL", transformedContext.Data.TransformedName);
        Assert.Equal(250.0, transformedContext.Data.TransformedValue);
        Assert.Equal(originalContext.CrewId, transformedContext.CrewId);
        Assert.Equal(originalContext.Memory, transformedContext.Memory);
        Assert.Equal(originalContext.PreviousOutputs, transformedContext.PreviousOutputs);
        Assert.Equal(originalContext.CancellationToken, transformedContext.CancellationToken);
    }

    [Fact]
    public void ShouldThrow_WhenTransformingWithNullTransformer()
    {
        // Arrange
        var context = CreateTestContext(cancellationToken: TestContext.Current.CancellationToken);

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            context.Transform<TransformedData>(null!));
        Assert.Equal("transformer", ex.ParamName);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldBeThreadSafe_WhenTransforming()
    {
        // Arrange
        var context = CreateTestContext(cancellationToken: TestContext.Current.CancellationToken);
        context.Data.Value = 50;
        var transforms = 100;
        var tasks = new List<System.Threading.Tasks.Task<ExecutionContext<TransformedData>>>();

        // Act
        for (int i = 0; i < transforms; i++)
        {
            tasks.Add(System.Threading.Tasks.Task.Run(() =>
                context.Transform<TransformedData>(data =>
                    new TransformedData
                    {
                        TransformedName = $"Transform-{Environment.CurrentManagedThreadId}",
                        TransformedValue = data.Value * 2
                    })));
        }

        var results = await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        Assert.Equal(transforms, results.Length);
        Assert.All(results, r => Assert.Equal(100.0, r.Data.TransformedValue));
    }

    #endregion

    #region WithOutput Tests

    [Fact]
    public void ShouldCreateNewContextWithAdditionalOutput_WhenUsingWithOutput()
    {
        // Arrange
        var originalContext = CreateTestContext(cancellationToken: TestContext.Current.CancellationToken);
        var newOutput = new TaskOutput(
            TaskId: TaskId2,
            AgentId: AgentId2,
            Content: "New output",
            CompletedAt: DateTime.UtcNow,
            Success: true,
            ExecutionTime: TimeSpan.FromSeconds(5));

        // Act
        var newContext = originalContext.WithOutput(newOutput);

        // Assert
        Assert.False(ReferenceEquals(originalContext, newContext));
        Assert.Empty(originalContext.PreviousOutputs);
        Assert.Single(newContext.PreviousOutputs);
        Assert.Equal("New output", newContext.PreviousOutputs[0].Content);
        Assert.Equal(originalContext.Data, newContext.Data);
        Assert.Equal(originalContext.CrewId, newContext.CrewId);
        Assert.Equal(originalContext.Memory, newContext.Memory);
    }

    [Fact]
    public void ShouldMaintainOrder_WhenUsingWithOutputWithMultipleOutputs()
    {
        // Arrange
        var context = CreateTestContext(cancellationToken: TestContext.Current.CancellationToken);
        var output1 = CreateTaskOutput(TaskId1, "Output 1");
        var output2 = CreateTaskOutput(TaskId2, "Output 2");
        var output3 = CreateTaskOutput(TaskId3, "Output 3");

        // Act
        var finalContext = context
            .WithOutput(output1)
            .WithOutput(output2)
            .WithOutput(output3);

        // Assert
        Assert.Equal(3, finalContext.PreviousOutputs.Count);
        Assert.Equal("Output 1", finalContext.PreviousOutputs[0].Content);
        Assert.Equal("Output 2", finalContext.PreviousOutputs[1].Content);
        Assert.Equal("Output 3", finalContext.PreviousOutputs[2].Content);
    }

    [Fact]
    public void ShouldThrow_WhenUsingWithOutputWithNullOutput()
    {
        // Arrange
        var context = CreateTestContext(cancellationToken: TestContext.Current.CancellationToken);

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() =>
            context.WithOutput(null!));
        Assert.Equal("output", ex.ParamName);
    }

    #endregion

    #region GetDataSnapshot Tests

    [Fact]
    public void ShouldReturnCurrentData_WhenUsingGetDataSnapshot()
    {
        // Arrange
        var context = CreateTestContext(cancellationToken: TestContext.Current.CancellationToken);
        context.UpdateData(d =>
        {
            d.Name = "Snapshot Test";
            d.Value = 999;
        });

        // Act
        var snapshot = context.DataSnapshot;

        // Assert
        Assert.Equal("Snapshot Test", snapshot.Name);
        Assert.Equal(999, snapshot.Value);
        Assert.Same(context.Data, snapshot); // Should be the same reference
    }

    #endregion

    #region IsCancelled Tests

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsCancelledWithNonCancelledToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var context = CreateTestContext(cancellationToken: cts.Token);

        // Act & Assert
        Assert.False(context.IsCancelled);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsCancelledWithCancelledToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var context = CreateTestContext(cancellationToken: cts.Token);
        cts.Cancel();

        // Act & Assert
        Assert.True(context.IsCancelled);
    }

    #endregion

    #region LastOutput Tests

    [Fact]
    public void ShouldReturnNull_WhenUsingLastOutputWithNoOutputs()
    {
        // Arrange
        var context = CreateTestContext(cancellationToken: TestContext.Current.CancellationToken);

        // Act & Assert
        Assert.Null(context.LastOutput);
    }

    [Fact]
    public void ShouldReturnLastOne_WhenUsingLastOutputWithOutputs()
    {
        // Arrange
        var context = CreateTestContext(cancellationToken: TestContext.Current.CancellationToken);
        var output1 = CreateTaskOutput(TaskId1, "First");
        var output2 = CreateTaskOutput(TaskId2, "Second");
        var output3 = CreateTaskOutput(TaskId3, "Last");

        context = context.WithOutput(output1).WithOutput(output2).WithOutput(output3);

        // Act & Assert
        Assert.NotNull(context.LastOutput);
        Assert.Equal("Last", context.LastOutput.Content);
    }

    #endregion

    #region GetOutputsFromAgent Tests

    [Fact]
    public void ShouldReturnOutputsFromSpecificAgent_WhenUsingGetOutputsFromAgent()
    {
        // Arrange
        var context = CreateTestContext(cancellationToken: TestContext.Current.CancellationToken);
        var output1 = CreateTaskOutput(TaskId1, "Output 1", AgentId1);
        var output2 = CreateTaskOutput(TaskId2, "Output 2", AgentId2);
        var output3 = CreateTaskOutput(TaskId3, "Output 3", AgentId1);
        var output4 = CreateTaskOutput("task-4", "Output 4", AgentId3);

        context = context
            .WithOutput(output1)
            .WithOutput(output2)
            .WithOutput(output3)
            .WithOutput(output4);

        // Act
        var agent1Outputs = context.GetOutputsFromAgent(AgentId1).ToList();
        var agent2Outputs = context.GetOutputsFromAgent(AgentId2).ToList();
        var agent4Outputs = context.GetOutputsFromAgent("agent-4").ToList();

        // Assert
        Assert.Equal(2, agent1Outputs.Count);
        Assert.Equal("Output 1", agent1Outputs[0].Content);
        Assert.Equal("Output 3", agent1Outputs[1].Content);

        Assert.Single(agent2Outputs);
        Assert.Equal("Output 2", agent2Outputs[0].Content);

        Assert.Empty(agent4Outputs);
    }

    #endregion

    #region GetLastOutput Tests

    [Fact]
    public void ShouldReturnNull_WhenUsingGetLastOutputWithNoOutputs()
    {
        // Arrange
        var context = CreateTestContext(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var lastOutput = context.LastOutput;

        // Assert
        Assert.Null(lastOutput);
    }

    [Fact]
    public void ShouldReturnLastOne_WhenUsingGetLastOutputWithOutputs()
    {
        // Arrange
        var context = CreateTestContext(cancellationToken: TestContext.Current.CancellationToken);
        var outputs = Enumerable.Range(1, 5)
            .Select(i => CreateTaskOutput($"task-{i}", $"Output {i}"))
            .ToList();

        foreach (var output in outputs)
        {
            context = context.WithOutput(output);
        }

        // Act
        var lastOutput = context.LastOutput;

        // Assert
        Assert.NotNull(lastOutput);
        Assert.Equal("Output 5", lastOutput.Content);
    }

    #endregion

    #region GetSuccessfulOutputs Tests

    [Fact]
    public void ShouldReturnOnlySuccessfulOutputs_WhenGettingSuccessfulOutputs()
    {
        // Arrange
        var context = CreateTestContext(cancellationToken: TestContext.Current.CancellationToken);
        var successOutput1 = CreateTaskOutput(TaskId1, "Success 1", success: true);
        var failureOutput1 = CreateTaskOutput(TaskId2, "Failure 1", success: false);
        var successOutput2 = CreateTaskOutput(TaskId3, "Success 2", success: true);
        var failureOutput2 = CreateTaskOutput("task-4", "Failure 2", success: false);
        var successOutput3 = CreateTaskOutput("task-5", "Success 3", success: true);

        context = context
            .WithOutput(successOutput1)
            .WithOutput(failureOutput1)
            .WithOutput(successOutput2)
            .WithOutput(failureOutput2)
            .WithOutput(successOutput3);

        // Act
        var successfulOutputs = context.GetSuccessfulOutputs().ToList();

        // Assert
        Assert.Equal(3, successfulOutputs.Count);
        Assert.Equal("Success 1", successfulOutputs[0].Content);
        Assert.Equal("Success 2", successfulOutputs[1].Content);
        Assert.Equal("Success 3", successfulOutputs[2].Content);
    }

    [Fact]
    public void ShouldReturnEmpty_WhenGettingSuccessfulOutputsWithNoSuccessfulOutputs()
    {
        // Arrange
        var context = CreateTestContext(cancellationToken: TestContext.Current.CancellationToken);
        var failure1 = CreateTaskOutput(TaskId1, "Failure 1", success: false);
        var failure2 = CreateTaskOutput(TaskId2, "Failure 2", success: false);

        context = context.WithOutput(failure1).WithOutput(failure2);

        // Act
        var successfulOutputs = context.GetSuccessfulOutputs().ToList();

        // Assert
        Assert.Empty(successfulOutputs);
    }

    #endregion

    #region Static Factory Methods Tests

    [Fact]
    public void ShouldCreateContextWithProvidedData_WhenCreating()
    {
        // Arrange
        var crewId = CrewId.Create();
        var data = new TestData { Name = "Factory Test", Value = 777 };
        using var memory = new TestMemoryScope("agent-factory");
        var outputs = new List<TaskOutput> { CreateTaskOutput(TaskId1, "Factory Output") };
        using var cts = new CancellationTokenSource();

        // Act
        var context = ExecutionContext.Create(
            crewId, data, memory, outputs, cts.Token);

        // Assert
        Assert.Equal(crewId, context.CrewId);
        Assert.Equal("Factory Test", context.Data.Name);
        Assert.Equal(777, context.Data.Value);
        Assert.Equal(memory, context.Memory);
        Assert.Single(context.PreviousOutputs);
        Assert.Equal("Factory Output", context.PreviousOutputs[0].Content);
        Assert.Equal(cts.Token, context.CancellationToken);
    }

    [Fact]
    public void ShouldUseEmptyList_WhenCreatingWithNullPreviousOutputs()
    {
        // Arrange
        var crewId = CrewId.Create();
        var data = new TestData();
        using var memory = new TestMemoryScope("agent-factory");

        // Act
        var context = ExecutionContext.Create<TestData>(
            crewId, data, memory, null, CancellationToken.None);

        // Assert
        Assert.NotNull(context.PreviousOutputs);
        Assert.Empty(context.PreviousOutputs);
    }

    [Fact]
    public void ShouldCreateContextWithNewData_WhenCreatingEmpty()
    {
        // Arrange
        var crewId = CrewId.Create();
        using var memory = new TestMemoryScope("agent-empty");
        using var cts = new CancellationTokenSource();

        // Act
        var context = ExecutionContext.CreateEmpty<TestData>(
            crewId, memory, cts.Token);

        // Assert
        Assert.Equal(crewId, context.CrewId);
        Assert.NotNull(context.Data);
        Assert.Equal("Test", context.Data.Name); // Default value
        Assert.Equal(42, context.Data.Value); // Default value
        Assert.Empty(context.Data.Items);
        Assert.Equal(memory, context.Memory);
        Assert.Empty(context.PreviousOutputs);
        Assert.Equal(cts.Token, context.CancellationToken);
    }

    #endregion

    #region TaskOutput Tests

    [Fact]
    public void ShouldInitializeAllProperties_WhenUsingTaskOutput()
    {
        // Arrange
        var taskId = "task-output-1";
        var agentId = "agent-output-1";
        var content = "Task output content";
        var completedAt = DateTime.UtcNow;
        var success = true;
        var executionTime = TimeoutQuick;
        var toolsUsed = new List<ToolUsage>
        {
            new ToolUsage(
                new ToolCallIdentity("tool-1", "ToolName", AgentId1, "task-output-1"),
                TimeSpan.FromSeconds(1),
                true)
        };

        // Act
        var output = new TaskOutput(
            taskId, agentId, content, completedAt, success, executionTime, toolsUsed);

        // Assert
        Assert.Equal(taskId, output.TaskId);
        Assert.Equal(agentId, output.AgentId);
        Assert.Equal(content, output.Content);
        Assert.Equal(completedAt, output.CompletedAt);
        Assert.Equal(success, output.Success);
        Assert.Equal(executionTime, output.ExecutionTime);
        Assert.Single(output.ToolsUsed);
        Assert.Equal("tool-1", output.ToolsUsed[0].ToolId);
    }

    [Fact]
    public void ShouldReturnContent_WhenUsingTaskOutputUsingRawOutput()
    {
        // Arrange & Act
        var output = new TaskOutput(
            TaskId1, AgentId1, "Raw content", DateTime.UtcNow, true, TimeSpan.Zero);

        // Assert
        Assert.Equal("Raw content", output.RawOutput);
        Assert.Equal(output.Content, output.RawOutput);
    }

    [Fact]
    public void ShouldUseEmptyList_WhenUsingTaskOutputWithNullToolsUsed()
    {
        // Arrange & Act
        var output = new TaskOutput(
            TaskId1, AgentId1, "Content", DateTime.UtcNow, true, TimeSpan.Zero, null);

        // Assert
        Assert.NotNull(output.ToolsUsed);
        Assert.Empty(output.ToolsUsed);
    }

    #endregion

    #region Helper Methods

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
        Justification = "Ownership of the default TestMemoryScope is transferred to the returned ExecutionContext; the double's Dispose only flips a flag and the object lives for the duration of the test.")]
    private static ExecutionContext<TestData> CreateTestContext(
        CrewId? crewId = null,
        TestData? data = null,
        IMemoryScope? memory = null,
        IReadOnlyList<TaskOutput>? outputs = null,
        CancellationToken cancellationToken = default)
    {
        return new ExecutionContext<TestData>(
            crewId ?? CrewId.Create(),
            data ?? new TestData(),
            memory ?? new TestMemoryScope("agent-test"),
            outputs ?? [],
            cancellationToken);
    }

    private static TaskOutput CreateTaskOutput(
        string taskId,
        string content,
        string agentId = AgentId1,
        bool success = true)
    {
        return new TaskOutput(
            taskId,
            agentId,
            content,
            DateTime.UtcNow,
            success,
            TimeSpan.FromSeconds(10));
    }

    #endregion
}
