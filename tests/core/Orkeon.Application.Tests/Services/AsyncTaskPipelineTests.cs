using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Ports;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Application.Tests.Services;

public class AsyncTaskPipelineTests
{
    [Fact]
    public void ShouldInitializeCorrectly_WhenConstructingWithDefaultParameters()
    {
        // Act
        using var pipeline = new AsyncTaskPipeline<string>();

        // Assert
        Assert.Equal(0, pipeline.Count);
        Assert.False(pipeline.IsCompleted);
    }

    [Fact]
    public void ShouldInitializeCorrectly_WhenConstructingWithCustomCapacityAndConcurrency()
    {
        // Arrange
        var capacity = 50;
        var maxConcurrency = 4;

        // Act
        using var pipeline = new AsyncTaskPipeline<string>(capacity, maxConcurrency);

        // Assert
        Assert.Equal(0, pipeline.Count);
        Assert.False(pipeline.IsCompleted);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldEnqueueSuccessfully_WhenEnqueuingAsyncWithSimpleItem()
    {
        // Arrange
        using var pipeline = new AsyncTaskPipeline<string>();
        var item = "test-item";

        // Act
        await pipeline.EnqueueAsync(item, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, pipeline.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowException_WhenEnqueuingAsyncAfterCompleteAdding()
    {
        // Arrange
        using var pipeline = new AsyncTaskPipeline<string>();
        pipeline.CompleteAdding();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await pipeline.EnqueueAsync("test-item", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnAllItems_WhenDequeuingAllAsyncWithMultipleItems()
    {
        // Arrange
        using var pipeline = new AsyncTaskPipeline<string>();
        var items = new[] { "item1", "item2", "item3" };

        foreach (var item in items)
        {
            await pipeline.EnqueueAsync(item, TestContext.Current.CancellationToken);
        }
        pipeline.CompleteAdding();

        // Act
        var dequeuedItems = new List<string>();
        await foreach (var item in pipeline.DequeueAllAsync(TestContext.Current.CancellationToken))
        {
            dequeuedItems.Add(item);
        }

        // Assert
        Assert.Equal(3, dequeuedItems.Count);
        Assert.Contains("item1", dequeuedItems);
        Assert.Contains("item2", dequeuedItems);
        Assert.Contains("item3", dequeuedItems);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldProcessAllItems_WhenProcessingAsyncWithSimpleProcessor()
    {
        // Arrange
        using var pipeline = new AsyncTaskPipeline<int>();
        var items = new[] { 1, 2, 3, 4, 5 };

        foreach (var item in items)
        {
            await pipeline.EnqueueAsync(item, TestContext.Current.CancellationToken);
        }
        pipeline.CompleteAdding();

        // Act
        var results = await pipeline.ProcessAsync(
            async (item, ct) =>
            {
                await System.Threading.Tasks.Task.Delay(10, ct);
                return item * 2;
            }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, results.Count);
        Assert.Contains(2, results);
        Assert.Contains(4, results);
        Assert.Contains(6, results);
        Assert.Contains(8, results);
        Assert.Contains(10, results);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRespectLimit_WhenProcessingAsyncWithConcurrencyLimit()
    {
        // Arrange
        using var pipeline = new AsyncTaskPipeline<int>(capacity: 10, maxConcurrency: 2);
        var items = Enumerable.Range(1, 10).ToArray();
        var concurrentExecutions = 0;
        var maxConcurrentExecutions = 0;
        var executionLock = new object();

        foreach (var item in items)
        {
            await pipeline.EnqueueAsync(item, TestContext.Current.CancellationToken);
        }
        pipeline.CompleteAdding();

        // Act
        await pipeline.ProcessAsync(
            async (item, ct) =>
            {
                lock (executionLock)
                {
                    concurrentExecutions++;
                    maxConcurrentExecutions = Math.Max(maxConcurrentExecutions, concurrentExecutions);
                }

                await System.Threading.Tasks.Task.Delay(50, ct); // Simulate work

                lock (executionLock)
                {
                    concurrentExecutions--;
                }

                return item;
            }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(maxConcurrentExecutions <= 2,
            $"Max concurrent executions was {maxConcurrentExecutions}, expected <= 2");
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldContinueProcessingOtherItems_WhenProcessingAsyncWithException()
    {
        // Arrange
        using var pipeline = new AsyncTaskPipeline<int>();
        var items = new[] { 1, 2, 3, 4, 5 };

        foreach (var item in items)
        {
            await pipeline.EnqueueAsync(item, TestContext.Current.CancellationToken);
        }
        pipeline.CompleteAdding();

        // Act
        var results = await pipeline.ProcessAsync(
            async (item, ct) =>
            {
                await System.Threading.Tasks.Task.Yield();
                if (item == 3)
                {
                    throw new InvalidOperationException("Test exception");
                }
                return item * 2;
            }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4, results.Count); // Should process all except the failed one
        Assert.DoesNotContain(6, results); // 3 * 2 = 6 should not be in results
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldStopProcessing_WhenProcessingAsyncWithCancellation()
    {
        // Arrange
        using var pipeline = new AsyncTaskPipeline<int>();
        using var cts = new CancellationTokenSource();
        var items = Enumerable.Range(1, 100).ToArray();

        foreach (var item in items)
        {
            await pipeline.EnqueueAsync(item, TestContext.Current.CancellationToken);
        }
        pipeline.CompleteAdding();

        // Act
        cts.CancelAfter(100); // Cancel after 100ms

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => pipeline.ProcessAsync(
                async (item, ct) =>
                {
                    await System.Threading.Tasks.Task.Delay(50, ct); // Each item takes 50ms
                    return item;
                }, cts.Token));
    }

    [Fact]
    public void ShouldMarkAsCompleted_WhenCompletingAdding()
    {
        // Arrange
        using var pipeline = new AsyncTaskPipeline<string>();

        // Act
        pipeline.CompleteAdding();

        // Assert
        Assert.True(pipeline.IsCompleted);
    }

    [Fact]
    public void ShouldBeIdempotent_WhenCompletingAddingCalledMultipleTimes()
    {
        // Arrange
        using var pipeline = new AsyncTaskPipeline<string>();

        // Act
        pipeline.CompleteAdding();
        pipeline.CompleteAdding();
        pipeline.CompleteAdding();

        // Assert
        Assert.True(pipeline.IsCompleted);
    }

    [Fact]
    public void ShouldCompleteAdding_WhenDisposing()
    {
        // Arrange
        var pipeline = new AsyncTaskPipeline<string>();

        // Act
        pipeline.Dispose();

        // Assert
        Assert.True(pipeline.IsCompleted);
    }
}

public class WorkItemTests
{
    [Fact]
    public void ShouldCreateCorrectly_WhenCreatingWithoutDependencies()
    {
        // Arrange
        var task = "test-task";
        var id = "task-123";

        // Act
        var workItem = WorkItem<string>.Create(task, id);

        // Assert
        Assert.Equal(task, workItem.Task);
        Assert.Equal(id, workItem.Id);
        Assert.Empty(workItem.Dependencies);
        Assert.NotNull(workItem.CompletionSource);
    }

    [Fact]
    public void ShouldCreateCorrectly_WhenCreatingWithDependencies()
    {
        // Arrange
        var task = "test-task";
        var id = "task-123";
        var dependencies = new[] { "dep1", "dep2", "dep3" };

        // Act
        var workItem = WorkItem<string>.Create(task, id, dependencies);

        // Assert
        Assert.Equal(task, workItem.Task);
        Assert.Equal(id, workItem.Id);
        Assert.Equal(3, workItem.Dependencies.Count);
        Assert.Contains("dep1", workItem.Dependencies);
        Assert.Contains("dep2", workItem.Dependencies);
        Assert.Contains("dep3", workItem.Dependencies);
        Assert.NotNull(workItem.CompletionSource);
    }

    [Fact]
    public void ShouldBeRecordType_WhenUsingWorkItem()
    {
        // Arrange
        var dependencies = new HashSet<string> { "dep1" };
        var completionSource = new TaskCompletionSource<object?>();
        var workItem1 = new WorkItem<string>("task", "id1", dependencies, completionSource);
        var workItem2 = new WorkItem<string>("task", "id1", dependencies, completionSource);
        var workItem3 = new WorkItem<string>("task", "id2", dependencies, completionSource);

        // Assert - same reference objects should be equal
        Assert.Equal(workItem1, workItem2);
        Assert.NotEqual(workItem1, workItem3);

        // Verify record-like properties
        Assert.Equal("task", workItem1.Task);
        Assert.Equal("id1", workItem1.Id);
        Assert.Same(dependencies, workItem1.Dependencies);
        Assert.Same(completionSource, workItem1.CompletionSource);
    }
}

public class CrewTaskPipelineTests
{
    [Fact]
    public void ShouldInitializeCorrectly_WhenConstructingWithDefaultParameters()
    {
        // Act
        using var pipeline = new CrewTaskPipeline();

        // Assert
        Assert.Equal(0, pipeline.Count);
        Assert.False(pipeline.IsCompleted);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldEnqueueSuccessfully_WhenEnqueuingTaskAsyncWithSimpleTask()
    {
        // Arrange
        using var pipeline = new CrewTaskPipeline();
        var task = DomainTask.Create(
            TaskDescription.From("Test task"),
            ExpectedOutput.From("Test output"));

        // Act
        await pipeline.EnqueueTaskAsync(task, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, pipeline.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleDependenciesCorrectly_WhenEnqueuingTaskAsyncWithDependencies()
    {
        // Arrange
        using var pipeline = new CrewTaskPipeline();

        var task1 = DomainTask.Create(
            Domain.Task.ValueObjects.TaskDescription.From("Task 1"),
            ExpectedOutput.From("Output 1"));

        var task2 = DomainTask.Create(
            Domain.Task.ValueObjects.TaskDescription.From("Task 2"),
            ExpectedOutput.From("Output 2"));
        task2.AddDependency(task1.Id);

        // Act
        await pipeline.EnqueueTaskAsync(task1, TestContext.Current.CancellationToken);
        await pipeline.EnqueueTaskAsync(task2, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, pipeline.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldProcessAll_WhenProcessingAsyncWithMultipleTasks()
    {
        // Arrange
        using var pipeline = new CrewTaskPipeline();
        var tasks = new List<Domain.Task.CrewTask>();

        for (int i = 0; i < 5; i++)
        {
            var task = DomainTask.Create(
                TaskDescription.From($"Task {i}"),
                ExpectedOutput.From($"Output {i}"));
            tasks.Add(task);
            await pipeline.EnqueueTaskAsync(task, TestContext.Current.CancellationToken);
        }

        pipeline.CompleteAdding();

        // Act
        var results = await pipeline.ProcessAsync(
            async (task, ct) =>
            {
                await System.Threading.Tasks.Task.Delay(10, ct);
                return task.Description.Value;
            }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, results.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Contains($"Task {i}", results);
        }
    }
}
