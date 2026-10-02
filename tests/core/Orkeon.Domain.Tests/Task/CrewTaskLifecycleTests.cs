using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.Events;
using Orkeon.Domain.Task.ValueObjects;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Domain.Tests.Task;

/// <summary>
/// GAP-21 — the lifecycle a run drives a <see cref="CrewTask"/> through: assigned to the agent that
/// runs it, started, then completed, failed or cancelled; handed to another agent mid-run; reopened
/// by the next run of its crew.
/// </summary>
public sealed class CrewTaskLifecycleTests
{
    private static CrewTask NewTask(string name = "draft") =>
        CrewTask.Create(TaskDescription.From(name), ExpectedOutput.From($"{name} done"));

    private static CrewTask RunningTask(AgentId agent)
    {
        var task = NewTask();
        task.AssignTo(agent);
        task.Start(agent);
        return task;
    }

    [Fact]
    public void A_task_with_dependencies_starts_when_the_run_starts_it()
    {
        // Start used to block any task with a dependency — it could not know its dependencies had
        // succeeded, so no task with one could ever run. The run decides, and only then starts it.
        var dependency = NewTask("extract");
        var task = NewTask("consolidate");
        task.AddDependency(dependency.Id);
        var agent = AgentId.Create();
        task.AssignTo(agent);
        task.ClearDomainEvents();

        task.Start(agent);

        Assert.Equal(TaskStatus.InProgress, task.Status);
        Assert.NotNull(task.StartedAt);
        var started = Assert.Single(task.DomainEvents.OfType<TaskStartedEvent>());
        Assert.Equal(agent, started.AgentId);
    }

    [Fact]
    public void A_task_no_single_agent_runs_starts_without_one()
    {
        var task = NewTask();
        task.ClearDomainEvents();

        task.Start();

        Assert.Equal(TaskStatus.InProgress, task.Status);
        Assert.Null(Assert.Single(task.DomainEvents.OfType<TaskStartedEvent>()).AgentId);
    }

    [Fact]
    public void A_task_is_started_only_by_the_agent_it_is_assigned_to()
    {
        var task = NewTask();
        task.AssignTo(AgentId.Create());

        var ex = Assert.Throws<InvalidOperationException>(() => task.Start(AgentId.Create()));

        Assert.Contains("must be assigned", ex.Message, StringComparison.Ordinal);
        Assert.Equal(TaskStatus.Pending, task.Status);
    }

    [Fact]
    public void A_running_task_handed_to_another_agent_is_completed_by_that_agent()
    {
        var first = AgentId.Create();
        var peer = AgentId.Create();
        var task = RunningTask(first);
        task.ClearDomainEvents();

        task.AssignTo(peer);
        task.Complete(peer, TaskOutput.Text("taken over"));

        Assert.Equal(peer, task.AssignedAgent);
        Assert.Equal(TaskStatus.Completed, task.Status);
        Assert.Equal(peer, Assert.Single(task.DomainEvents.OfType<TaskAssignedEvent>()).AgentId);
        Assert.Equal(peer, Assert.Single(task.DomainEvents.OfType<TaskCompletedEvent>()).AgentId);
        Assert.Empty(task.DomainEvents.OfType<TaskStartedEvent>());
    }

    [Fact]
    public void A_task_started_without_an_agent_is_completed_under_the_agent_it_is_then_assigned_to()
    {
        var author = AgentId.Create();
        var task = NewTask();
        task.Start();

        task.AssignTo(author);
        task.Complete(author, TaskOutput.Text("retained answer"));

        Assert.Equal(TaskStatus.Completed, task.Status);
        Assert.Equal(author, task.AssignedAgent);
    }

    [Fact]
    public void Assigning_the_agent_a_task_already_has_raises_nothing()
    {
        var agent = AgentId.Create();
        var task = NewTask();
        task.AssignTo(agent);
        task.ClearDomainEvents();

        task.AssignTo(agent);

        Assert.Empty(task.DomainEvents);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    public void A_task_that_ended_cannot_be_assigned(string ending)
    {
        var agent = AgentId.Create();
        var task = RunningTask(agent);
        switch (ending)
        {
            case "completed": task.Complete(agent, TaskOutput.Text("done")); break;
            case "failed": task.Fail("broken"); break;
            default: task.Cancel("run cancelled"); break;
        }

        Assert.Throws<InvalidOperationException>(() => task.AssignTo(AgentId.Create()));
    }

    [Fact]
    public void A_failed_task_records_when_it_ended()
    {
        var agent = AgentId.Create();
        var task = RunningTask(agent);

        task.Fail("rate limited");

        Assert.Equal(TaskStatus.Failed, task.Status);
        Assert.NotNull(task.CompletedAt);
        Assert.True(task.CompletedAt >= task.StartedAt);
        Assert.Equal(task.CompletedAt!.Value - task.StartedAt!.Value, task.GetExecutionTime());
        var failed = Assert.Single(task.DomainEvents.OfType<TaskFailedEvent>());
        Assert.Equal(agent, failed.AgentId);
        Assert.Equal("rate limited", failed.ErrorMessage);
    }

    [Fact]
    public void A_task_that_failed_before_it_started_ran_for_no_time()
    {
        var task = NewTask();

        task.Fail("the manager assigned it to an agent the crew does not carry");

        Assert.Equal(TaskStatus.Failed, task.Status);
        Assert.Null(task.StartedAt);
        Assert.Equal(TimeSpan.Zero, task.GetExecutionTime());
    }

    [Fact]
    public void A_skipped_task_is_cancelled_with_its_reason_and_neither_started_nor_ended()
    {
        var task = NewTask();
        task.ClearDomainEvents();

        task.Cancel("skipped: it depends on task 42, which did not succeed");

        Assert.Equal(TaskStatus.Cancelled, task.Status);
        Assert.Null(task.StartedAt);
        Assert.Null(task.CompletedAt);
        Assert.Equal(
            "skipped: it depends on task 42, which did not succeed",
            Assert.Single(task.DomainEvents.OfType<TaskCancelledEvent>()).Reason);
        Assert.Empty(task.DomainEvents.OfType<TaskStartedEvent>());
    }

    [Fact]
    public void Reopen_puts_a_task_an_earlier_run_ended_back_to_pending()
    {
        var agent = AgentId.Create();
        var dependency = TaskId.Create();
        var task = NewTask();
        task.AddDependency(dependency);
        task.AssignTo(agent);
        task.Start(agent);
        task.Complete(agent, TaskOutput.Text("first run"));
        task.ClearDomainEvents();

        task.Reopen();

        Assert.Equal(TaskStatus.Pending, task.Status);
        Assert.Null(task.Output);
        Assert.Null(task.StartedAt);
        Assert.Null(task.CompletedAt);
        Assert.Equal(agent, task.AssignedAgent);
        Assert.Equal([dependency], task.Dependencies);
        var reopened = Assert.Single(task.DomainEvents.OfType<TaskStatusChangedEvent>());
        Assert.Equal(TaskStatus.Completed, reopened.OldStatus);
        Assert.Equal(TaskStatus.Pending, reopened.NewStatus);

        // The next run starts it again.
        task.Start(agent);
        Assert.Equal(TaskStatus.InProgress, task.Status);
    }

    [Fact]
    public void Reopen_leaves_a_pending_task_alone()
    {
        var task = NewTask();
        task.ClearDomainEvents();

        task.Reopen();

        Assert.Equal(TaskStatus.Pending, task.Status);
        Assert.Empty(task.DomainEvents);
    }
}
