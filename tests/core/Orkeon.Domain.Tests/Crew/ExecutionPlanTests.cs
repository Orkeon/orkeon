using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;

namespace Orkeon.Domain.Tests.Crew;

/// <summary>
/// The crew's plan (GAP-31): one step-by-step plan per task, nothing else. The order, the agent
/// assignments, the parallel groups and the dependencies it used to carry are gone — the plan
/// decides none of them — and so are their members and tests.
/// </summary>
public class ExecutionPlanTests
{
    [Fact]
    public void A_plan_gives_each_task_its_own_instructions()
    {
        var first = TaskId.Create();
        var second = TaskId.Create();

        var plan = ExecutionPlan.Create([new PlannedTask(first, "1. Outline."), new PlannedTask(second, "1. Draft.")]);

        Assert.Equal(2, plan.Tasks.Count);
        Assert.Equal("1. Outline.", plan.InstructionsFor(first));
        Assert.Equal("1. Draft.", plan.InstructionsFor(second));
    }

    [Fact]
    public void A_task_the_plan_does_not_name_has_no_instructions()
    {
        var plan = ExecutionPlan.Create([new PlannedTask(TaskId.Create(), "1. Outline.")]);

        Assert.Null(plan.InstructionsFor(TaskId.Create()));
        Assert.Null(ExecutionPlan.Empty.InstructionsFor(TaskId.Create()));
    }

    [Fact]
    public void The_empty_plan_has_no_task()
    {
        Assert.Empty(ExecutionPlan.Empty.Tasks);
        Assert.Empty(ExecutionPlan.Create([]).Tasks);
    }

    [Fact]
    public void A_plan_holds_one_plan_per_task()
    {
        var task = TaskId.Create();

        var exception = Assert.Throws<ArgumentException>(
            () => ExecutionPlan.Create([new PlannedTask(task, "A"), new PlannedTask(task, "B")]));

        Assert.Contains(task.ToString(), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Its_arguments_are_guarded()
    {
        Assert.Throws<ArgumentNullException>(() => ExecutionPlan.Create(null!));
        Assert.Throws<ArgumentNullException>(() => new PlannedTask(null!, "A"));
        Assert.Throws<ArgumentNullException>(() => new PlannedTask(TaskId.Create(), null!));
        Assert.Throws<ArgumentNullException>(() => ExecutionPlan.Empty.InstructionsFor(null!));
    }

    [Fact]
    public void Two_plans_with_the_same_instructions_for_the_same_tasks_are_equal()
    {
        var task = TaskId.Create();

        var plan = ExecutionPlan.Create([new PlannedTask(task, "A")]);

        Assert.Equal(plan, ExecutionPlan.Create([new PlannedTask(task, "A")]));
        Assert.Equal(plan.GetHashCode(), ExecutionPlan.Create([new PlannedTask(task, "A")]).GetHashCode());
        Assert.NotEqual(plan, ExecutionPlan.Create([new PlannedTask(task, "B")]));
        Assert.NotEqual(plan, ExecutionPlan.Empty);
    }

    [Fact]
    public void The_plan_s_tasks_cannot_be_changed_from_outside()
    {
        var source = new List<PlannedTask> { new(TaskId.Create(), "A") };

        var plan = ExecutionPlan.Create(source);
        source.Add(new PlannedTask(TaskId.Create(), "B"));

        Assert.Single(plan.Tasks);
        Assert.False(plan.Tasks is List<PlannedTask>);
    }
}
