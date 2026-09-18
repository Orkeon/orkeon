using Orkeon.Domain.Common;
using Orkeon.Domain.Task;

namespace Orkeon.Domain.Tests.Task;

/// <summary>
/// The execution order of a crew's tasks when no plan decides it (STUDIO-12 C2): a stable
/// topological sort on the declared dependencies. The multi-file layout lists the tasks in
/// the ordinal order of their file names, which is how <c>consolidate.yaml</c> ran before the
/// <c>extract.yaml</c> it depends on — the sort is what makes both layouts run the same.
/// </summary>
public class TaskExecutionOrderTests
{
    private static CrewTask Task(string name) =>
        new CrewTaskBuilder().Description($"STEP {name}").ExpectedOutput(name).Build();

    private static IReadOnlyList<string> Order(TaskExecutionOrderResult<CrewTask> result) =>
        result.Tasks.Select(t => t.Description.Value).ToList();

    [Fact]
    public void ADependentTaskRunsAfterItsDependency_WhateverTheDeclaredOrder()
    {
        // The sheet's deterministic repro: the file names sort the dependent task first.
        var first = Task("ONE");
        var second = Task("TWO");
        second.AddDependency(first.Id);

        var result = TaskExecutionOrder.Resolve([second, first]);

        Assert.Equal(["STEP ONE", "STEP TWO"], Order(result));
        Assert.False(result.HasCycle);
    }

    [Fact]
    public void TheDeclaredOrderIsKept_WhenNothingDependsOnAnything()
    {
        var c = Task("C");
        var a = Task("A");
        var b = Task("B");

        var result = TaskExecutionOrder.Resolve([c, a, b]);

        Assert.Equal(["STEP C", "STEP A", "STEP B"], Order(result));
    }

    [Fact]
    public void TheDeclaredOrderIsKept_WhereverTheDependenciesAllowIt()
    {
        // D depends on A only: the sort moves A ahead of D and leaves everything else where it was.
        var d = Task("D");
        var b = Task("B");
        var a = Task("A");
        var c = Task("C");
        d.AddDependency(a.Id);

        var result = TaskExecutionOrder.Resolve([d, b, a, c]);

        Assert.Equal(["STEP B", "STEP A", "STEP D", "STEP C"], Order(result));
    }

    [Fact]
    public void ADependencyOnAnUnknownTaskIsIgnored()
    {
        var a = Task("A");
        var b = Task("B");
        a.AddDependency(TaskId.Create()); // a task this crew does not carry

        var result = TaskExecutionOrder.Resolve([a, b]);

        Assert.Equal(["STEP A", "STEP B"], Order(result));
        Assert.False(result.HasCycle);
    }

    [Fact]
    public void ACycleKeepsTheDeclaredOrderForTheTasksCaughtInIt_AndNamesThem()
    {
        var a = Task("A");
        var b = Task("B");
        var c = Task("C");
        a.AddDependency(b.Id);
        b.AddDependency(a.Id);
        c.AddDependency(b.Id);

        var result = TaskExecutionOrder.Resolve([a, b, c]);

        // A and B wait on each other: A, first in declared order, is placed anyway; B then
        // becomes ready, and C after it. Nothing throws.
        Assert.Equal(["STEP A", "STEP B", "STEP C"], Order(result));
        Assert.True(result.HasCycle);
        Assert.Equal([a.Id], result.ForcedTaskIds);
    }

    [Fact]
    public void AnEmptyListResolvesToAnEmptyOrder()
    {
        var result = TaskExecutionOrder.Resolve(Array.Empty<CrewTask>());

        Assert.Empty(result.Tasks);
        Assert.False(result.HasCycle);
    }

    [Fact]
    public void ANullListIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => TaskExecutionOrder.Resolve<CrewTask>(null!));
    }
}
