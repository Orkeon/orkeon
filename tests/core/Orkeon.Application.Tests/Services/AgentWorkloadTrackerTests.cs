using Microsoft.Extensions.Logging;
using Orkeon.Application.Agent;
using static Orkeon.Tests.Shared.Constants.TestTimingConstants;

namespace Orkeon.Application.Tests.Services;

public class AgentWorkloadTrackerTests
{
    private static readonly string[] s_agents123 = ["agent1", "agent2", "agent3"];
    private static readonly string[] s_agents12 = ["agent1", "agent2"];
    private static readonly string[] s_agent1 = ["agent1"];

    #region Test Doubles

    private class TestLogger : ILogger<AgentWorkloadTracker>
    {
        public List<string> LoggedMessages { get; } = [];
        public List<Exception> LoggedExceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            LoggedMessages.Add($"[{logLevel}] {message}");
            if (exception != null)
            {
                LoggedExceptions.Add(exception);
            }
        }

        public bool HasLoggedDebug(string partialMessage)
        {
            return LoggedMessages.Any(m => m.StartsWith("[Debug]") && m.Contains(partialMessage));
        }

        public bool HasLoggedInfo(string partialMessage)
        {
            return LoggedMessages.Any(m => m.StartsWith("[Information]") && m.Contains(partialMessage));
        }

        public bool HasLoggedWarning(string partialMessage)
        {
            return LoggedMessages.Any(m => m.StartsWith("[Warning]") && m.Contains(partialMessage));
        }
    }

    /// <summary>
    /// Manual <see cref="TimeProvider"/> whose UTC now only moves when <see cref="Advance"/>
    /// is called, so retention cutoffs are computed deterministically instead of racing the
    /// real clock under CI load.
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow += delta;
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void ShouldInitialize_WhenConstructingWithDefaultParameters()
    {
        // Arrange & Act
        var tracker = new AgentWorkloadTracker();

        // Assert
        Assert.NotNull(tracker);
        var workloads = tracker.GetAllWorkloads();
        Assert.Empty(workloads);
    }

    [Fact]
    public void ShouldInitialize_WhenConstructingWithLogger()
    {
        // Arrange
        var logger = new TestLogger();

        // Act
        var tracker = new AgentWorkloadTracker(logger);

        // Assert
        Assert.NotNull(tracker);
    }

    [Fact]
    public void ShouldInitialize_WhenConstructingWithCustomRetentionPeriod()
    {
        // Arrange
        var logger = new TestLogger();
        var retentionPeriod = TimeoutExtended;

        // Act
        var tracker = new AgentWorkloadTracker(logger, retentionPeriod);

        // Assert
        Assert.NotNull(tracker);
    }

    #endregion

    #region RecordTaskStarted Tests

    [Fact]
    public void ShouldIncrementActiveTaskCount_WhenRecordingTaskStarted()
    {
        // Arrange
        var logger = new TestLogger();
        var tracker = new AgentWorkloadTracker(logger);
        var agentId = "agent1";
        var taskId = "task1";

        // Act
        tracker.RecordTaskStarted(agentId, taskId);

        // Assert
        var workload = tracker.GetWorkloadInfo(agentId);
        Assert.Equal(1, workload.ActiveTaskCount);
        Assert.True(logger.HasLoggedDebug($"Agent {agentId} started task {taskId}. Active tasks: 1"));
    }

    [Fact]
    public void ShouldIncrementCorrectly_WhenRecordingTaskStartedWithMultipleTasksForSameAgent()
    {
        // Arrange
        var tracker = new AgentWorkloadTracker();
        var agentId = "agent1";

        // Act
        tracker.RecordTaskStarted(agentId, "task1");
        tracker.RecordTaskStarted(agentId, "task2");
        tracker.RecordTaskStarted(agentId, "task3");

        // Assert
        var workload = tracker.GetWorkloadInfo(agentId);
        Assert.Equal(3, workload.ActiveTaskCount);
    }

    [Fact]
    public void ShouldTrackSeparately_WhenRecordingTaskStartedWithDifferentAgents()
    {
        // Arrange
        var tracker = new AgentWorkloadTracker();

        // Act
        tracker.RecordTaskStarted("agent1", "task1");
        tracker.RecordTaskStarted("agent2", "task2");
        tracker.RecordTaskStarted("agent1", "task3");

        // Assert
        var workload1 = tracker.GetWorkloadInfo("agent1");
        var workload2 = tracker.GetWorkloadInfo("agent2");
        Assert.Equal(2, workload1.ActiveTaskCount);
        Assert.Equal(1, workload2.ActiveTaskCount);
    }

    #endregion

    #region RecordTaskCompleted Tests

    [Fact]
    public void ShouldDecrementActiveTaskCount_WhenRecordingTaskCompletedAfterTaskStarted()
    {
        // Arrange
        var logger = new TestLogger();
        var tracker = new AgentWorkloadTracker(logger);
        var agentId = "agent1";
        var taskId = "task1";
        var executionTime = TimeSpan.FromSeconds(5);

        // Act
        tracker.RecordTaskStarted(agentId, taskId);
        tracker.RecordTaskCompleted(agentId, taskId, executionTime, true);

        // Assert
        var workload = tracker.GetWorkloadInfo(agentId);
        Assert.Equal(0, workload.ActiveTaskCount);
        Assert.Equal(1, workload.CompletedTaskCount);
        Assert.True(logger.HasLoggedDebug($"Agent {agentId} completed task {taskId}"));
    }

    [Fact]
    public void ShouldLogWarning_WhenRecordingTaskCompletedForUnknownAgent()
    {
        // Arrange
        var logger = new TestLogger();
        var tracker = new AgentWorkloadTracker(logger);

        // Act
        tracker.RecordTaskCompleted("unknown", "task1", TimeSpan.FromSeconds(1), true);

        // Assert
        Assert.True(logger.HasLoggedWarning("Cannot record task completion for unknown agent unknown"));
    }

    [Fact]
    public void ShouldCalculateSuccessRate_WhenRecordingTaskCompletedWithSuccessAndFailure()
    {
        // Arrange
        var tracker = new AgentWorkloadTracker();
        var agentId = "agent1";

        // Act
        tracker.RecordTaskStarted(agentId, "task1");
        tracker.RecordTaskCompleted(agentId, "task1", TimeSpan.FromSeconds(1), true);

        tracker.RecordTaskStarted(agentId, "task2");
        tracker.RecordTaskCompleted(agentId, "task2", TimeSpan.FromSeconds(2), false);

        tracker.RecordTaskStarted(agentId, "task3");
        tracker.RecordTaskCompleted(agentId, "task3", TimeSpan.FromSeconds(3), true);

        // Assert
        var workload = tracker.GetWorkloadInfo(agentId);
        Assert.Equal(0, workload.ActiveTaskCount);
        Assert.Equal(3, workload.CompletedTaskCount);
        Assert.Equal(2.0 / 3.0, workload.SuccessRate, 0.01);
    }

    [Fact]
    public void ShouldCalculateAverageExecutionTime_WhenRecordingTaskCompleted()
    {
        // Arrange
        var tracker = new AgentWorkloadTracker();
        var agentId = "agent1";

        // Act
        tracker.RecordTaskStarted(agentId, "task1");
        tracker.RecordTaskCompleted(agentId, "task1", TimeSpan.FromSeconds(2), true);

        tracker.RecordTaskStarted(agentId, "task2");
        tracker.RecordTaskCompleted(agentId, "task2", TimeSpan.FromSeconds(4), true);

        tracker.RecordTaskStarted(agentId, "task3");
        tracker.RecordTaskCompleted(agentId, "task3", TimeSpan.FromSeconds(6), true);

        // Assert
        var workload = tracker.GetWorkloadInfo(agentId);
        Assert.Equal(TimeSpan.FromSeconds(4), workload.AverageExecutionTime);
    }

    #endregion

    #region GetWorkloadInfo Tests

    [Fact]
    public void ShouldReturnDefaultValues_WhenUsingGetWorkloadInfoForUnknownAgent()
    {
        // Arrange
        var tracker = new AgentWorkloadTracker();

        // Act
        var workload = tracker.GetWorkloadInfo("unknown");

        // Assert
        Assert.Equal("unknown", workload.AgentId);
        Assert.Equal(0, workload.ActiveTaskCount);
        Assert.Equal(0, workload.CompletedTaskCount);
        Assert.Equal(TimeSpan.Zero, workload.AverageExecutionTime);
        Assert.Equal(0, workload.SuccessRate);
    }

    [Fact]
    public void ShouldReturnCorrectInfo_WhenUsingGetWorkloadInfoWithNoCompletedTasks()
    {
        // Arrange
        var tracker = new AgentWorkloadTracker();
        var agentId = "agent1";

        // Act
        tracker.RecordTaskStarted(agentId, "task1");
        tracker.RecordTaskStarted(agentId, "task2");
        var workload = tracker.GetWorkloadInfo(agentId);

        // Assert
        Assert.Equal(agentId, workload.AgentId);
        Assert.Equal(2, workload.ActiveTaskCount);
        Assert.Equal(0, workload.CompletedTaskCount);
        Assert.Equal(TimeSpan.Zero, workload.AverageExecutionTime);
        Assert.Equal(1.0, workload.SuccessRate); // Default when no completed tasks
    }

    #endregion

    #region GetAllWorkloads Tests

    [Fact]
    public void ShouldReturnAllWorkloads_WhenGettingAllWorkloadsWithMultipleAgents()
    {
        // Arrange
        var tracker = new AgentWorkloadTracker();

        // Act
        tracker.RecordTaskStarted("agent1", "task1");
        tracker.RecordTaskStarted("agent2", "task2");
        tracker.RecordTaskStarted("agent3", "task3");

        var workloads = tracker.GetAllWorkloads();

        // Assert
        Assert.Equal(3, workloads.Count);
        Assert.Contains("agent1", workloads.Keys);
        Assert.Contains("agent2", workloads.Keys);
        Assert.Contains("agent3", workloads.Keys);
        Assert.All(workloads.Values, w => Assert.Equal(1, w.ActiveTaskCount));
    }

    [Fact]
    public void ShouldReturnEmptyDictionary_WhenGettingAllWorkloadsWithNoAgents()
    {
        // Arrange
        var tracker = new AgentWorkloadTracker();

        // Act
        var workloads = tracker.GetAllWorkloads();

        // Assert
        Assert.Empty(workloads);
    }

    #endregion

    #region SelectLeastLoadedAgent Tests

    [Fact]
    public void ShouldReturnThatAgent_WhenUsingSelectLeastLoadedAgentWithSingleAgent()
    {
        // Arrange
        var tracker = new AgentWorkloadTracker();
        var agents = new[] { "agent1" };

        // Act
        var selected = tracker.SelectLeastLoadedAgent(agents);

        // Assert
        Assert.Equal("agent1", selected);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingSelectLeastLoadedAgentWithNoAgents()
    {
        // Arrange
        var tracker = new AgentWorkloadTracker();
        var agents = Array.Empty<string>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => tracker.SelectLeastLoadedAgent(agents));
        Assert.Equal("agentIds", exception.ParamName);
    }

    [Fact]
    public void ShouldSelectAgentWithFewestActiveTasks_WhenUsingSelectLeastLoadedAgent()
    {
        // Arrange
        var tracker = new AgentWorkloadTracker();

        // Agent1: 3 active tasks
        tracker.RecordTaskStarted("agent1", "task1");
        tracker.RecordTaskStarted("agent1", "task2");
        tracker.RecordTaskStarted("agent1", "task3");

        // Agent2: 1 active task
        tracker.RecordTaskStarted("agent2", "task4");

        // Agent3: 2 active tasks
        tracker.RecordTaskStarted("agent3", "task5");
        tracker.RecordTaskStarted("agent3", "task6");

        // Act
        var selected = tracker.SelectLeastLoadedAgent(s_agents123);

        // Assert
        Assert.Equal("agent2", selected);
    }

    [Fact]
    public void ShouldConsiderSuccessRate_WhenUsingSelectLeastLoadedAgentWithEqualActiveTasks()
    {
        // Arrange
        var tracker = new AgentWorkloadTracker();

        // Agent1: 0 active tasks, 100% success rate
        tracker.RecordTaskStarted("agent1", "task1");
        tracker.RecordTaskCompleted("agent1", "task1", TimeSpan.FromSeconds(1), true);

        // Agent2: 0 active tasks, 50% success rate
        tracker.RecordTaskStarted("agent2", "task2");
        tracker.RecordTaskCompleted("agent2", "task2", TimeSpan.FromSeconds(1), true);
        tracker.RecordTaskStarted("agent2", "task3");
        tracker.RecordTaskCompleted("agent2", "task3", TimeSpan.FromSeconds(1), false);

        // Act
        var selected = tracker.SelectLeastLoadedAgent(s_agents12);

        // Assert
        Assert.Equal("agent1", selected); // Higher success rate
    }

    [Fact]
    public void ShouldLogSelection_WhenUsingSelectLeastLoadedAgent()
    {
        // Arrange
        var logger = new TestLogger();
        var tracker = new AgentWorkloadTracker(logger);
        tracker.RecordTaskStarted("agent1", "task1");

        // Act
        var selected = tracker.SelectLeastLoadedAgent(s_agent1);

        // Assert
        Assert.True(logger.HasLoggedInfo($"Selected agent {selected}"));
    }

    #endregion

    #region ResetMetrics Tests

    [Fact]
    public void ShouldClearOnlyThatAgentMetrics_WhenUsingResetMetricsForSpecificAgent()
    {
        // Arrange
        var logger = new TestLogger();
        var tracker = new AgentWorkloadTracker(logger);

        tracker.RecordTaskStarted("agent1", "task1");
        tracker.RecordTaskStarted("agent2", "task2");

        // Act
        tracker.ResetMetrics("agent1");

        // Assert
        var workload1 = tracker.GetWorkloadInfo("agent1");
        var workload2 = tracker.GetWorkloadInfo("agent2");

        Assert.Equal(0, workload1.ActiveTaskCount);
        Assert.Equal(1, workload2.ActiveTaskCount);
        Assert.True(logger.HasLoggedInfo("Reset workload metrics for agent agent1"));
    }

    [Fact]
    public void ShouldClearAllMetrics_WhenUsingResetMetricsWithNullAgentId()
    {
        // Arrange
        var logger = new TestLogger();
        var tracker = new AgentWorkloadTracker(logger);

        tracker.RecordTaskStarted("agent1", "task1");
        tracker.RecordTaskStarted("agent2", "task2");

        // Act
        tracker.ResetMetrics();

        // Assert
        var workloads = tracker.GetAllWorkloads();
        Assert.Empty(workloads);
        Assert.True(logger.HasLoggedInfo("Reset all workload metrics"));
    }

    #endregion

    #region Retention Period Tests

    [Fact]
    public void ShouldExcludeOldMetrics_WhenUsingGetWorkloadInfo()
    {
        // Arrange - a manual clock makes the retention cutoff deterministic. The old version
        // slept 150 ms against a 100 ms window, so a stall between the two completions aged
        // the second one out too and the count flipped to 0 under parallel load.
        var retentionPeriod = TimeSpan.FromMinutes(10);
        var clock = new ManualTimeProvider();
        var tracker = new AgentWorkloadTracker(null, retentionPeriod, clock);
        var agentId = "agent1";

        // Act
        // A first task, completed at T0.
        tracker.RecordTaskStarted(agentId, "oldTask");
        tracker.RecordTaskCompleted(agentId, "oldTask", TimeSpan.FromSeconds(1), true);

        // The clock jumps past the retention window: the first task is now out of it,
        // with no real waiting involved.
        clock.Advance(retentionPeriod + TimeSpan.FromMinutes(1));

        // A second task, completed at T0 + 11 min, well inside the window from there on.
        tracker.RecordTaskStarted(agentId, "newTask");
        tracker.RecordTaskCompleted(agentId, "newTask", TimeSpan.FromSeconds(2), true);

        var workload = tracker.GetWorkloadInfo(agentId);

        // Assert
        Assert.Equal(1, workload.CompletedTaskCount); // Only the new task
        Assert.Equal(TimeSpan.FromSeconds(2), workload.AverageExecutionTime);
    }

    #endregion

    #region Concurrent Access Tests

    [Fact]
    public async System.Threading.Tasks.Task ShouldHandleCorrectly_WhenRecordingTaskStartedWithConcurrentCalls()
    {
        // Arrange
        var tracker = new AgentWorkloadTracker();
        var agentId = "agent1";
        var taskCount = 100;
        var tasks = new List<System.Threading.Tasks.Task>();

        // Act
        for (int i = 0; i < taskCount; i++)
        {
            var taskId = $"task{i}";
            tasks.Add(System.Threading.Tasks.Task.Run(() => tracker.RecordTaskStarted(agentId, taskId), TestContext.Current.CancellationToken));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks);

        // Assert
        var workload = tracker.GetWorkloadInfo(agentId);
        Assert.Equal(taskCount, workload.ActiveTaskCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldBeThreadSafe_WhenUsingSelectLeastLoadedAgentWithConcurrentUpdates()
    {
        // Arrange
        var tracker = new AgentWorkloadTracker();
        var agents = new[] { "agent1", "agent2", "agent3" };
        var tasks = new List<System.Threading.Tasks.Task>();

        // Act
        // Concurrent task starts and completions
        for (int i = 0; i < 50; i++)
        {
            var index = i;
            tasks.Add(System.Threading.Tasks.Task.Run(() =>
            {
                var agentId = agents[index % agents.Length];
                var taskId = $"task{index}";
                tracker.RecordTaskStarted(agentId, taskId);

                if (index % 2 == 0)
                {
                    tracker.RecordTaskCompleted(agentId, taskId, TimeSpan.FromSeconds(1), true);
                }
            }, TestContext.Current.CancellationToken));
        }

        // Concurrent selections
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(System.Threading.Tasks.Task.Run(() => tracker.SelectLeastLoadedAgent(agents)));
        }

        // Assert - No exceptions should be thrown
        await System.Threading.Tasks.Task.WhenAll(tasks);

        var workloads = tracker.GetAllWorkloads();
        Assert.Equal(3, workloads.Count);
    }

    #endregion
}
