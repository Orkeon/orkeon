using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// The plan of the crew run in progress (<c>planning: true</c>, GAP-31). The orchestrator opens a scope
/// around the strategy of every run — with the run's plan, or <see cref="ExecutionPlan.Empty"/> when
/// the crew does not plan — and the execution of each task reads the plan of that task here when it
/// composes the task's prompt. One writer, one reader: no strategy carries the plan, so it reaches a
/// task the same way in all six modes, and a task's own description is never touched.
/// </summary>
/// <remarks>
/// Backed by <see cref="AsyncLocal{T}"/>, like <c>LlmUsageScope</c>: the plan follows the run's flow
/// across awaits and into the tasks it starts (<c>Task.Run</c>: parallel waves, consensual candidates),
/// and a scope opened inside an async method never leaks to its caller — two runs of the same crew
/// (<c>KickoffForEachAsync</c>) each read their own. A crew run from inside a task opens its own scope,
/// so it sees its own plan only, and the outer one comes back when it ends.
/// </remarks>
public static class CrewPlanScope
{
    private static readonly AsyncLocal<ExecutionPlan?> Ambient = new();

    /// <summary>Opens the scope of a run; disposing the handle restores the enclosing one.</summary>
    /// <param name="plan">The run's plan; <see cref="ExecutionPlan.Empty"/> when the crew does not plan.</param>
    /// <returns>The handle that restores the enclosing scope.</returns>
    public static IDisposable Begin(ExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var enclosing = Ambient.Value;
        Ambient.Value = plan;
        return new Scope(enclosing);
    }

    /// <summary>The plan the run's planner wrote for <paramref name="taskId"/>; null outside a run, or when it wrote none.</summary>
    /// <param name="taskId">The task being run.</param>
    public static string? InstructionsFor(TaskId taskId)
    {
        ArgumentNullException.ThrowIfNull(taskId);
        return Ambient.Value?.InstructionsFor(taskId);
    }

    private sealed class Scope(ExecutionPlan? enclosing) : IDisposable
    {
        public void Dispose() => Ambient.Value = enclosing;
    }
}
