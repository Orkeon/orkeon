namespace Orkeon.Application.Telemetry;

/// <summary>
/// The names and tags of the spans of a crew run, beside <see cref="OrkeonActivitySources"/>:
/// one <c>crew.run</c> per run, one <c>task.run</c> per task under it, and the memory operations of
/// a task. A tag never carries a task description, a prompt or an output.
/// </summary>
public static class OrkeonRunSpans
{
    /// <summary>The span of a crew run.</summary>
    public const string CrewRun = "crew.run";

    /// <summary>The span of a task of a run.</summary>
    public const string TaskRun = "task.run";

    /// <summary>The span of the recall of a task's memories.</summary>
    public const string MemoryRecall = "memory.recall";

    /// <summary>The span of the store of a task's result in memory.</summary>
    public const string MemoryStore = "memory.store";

    /// <summary>The id of the crew.</summary>
    public const string CrewId = "orkeon.crew.id";

    /// <summary>The name of the crew.</summary>
    public const string CrewName = "orkeon.crew.name";

    /// <summary>The process (mode) of the crew.</summary>
    public const string CrewProcess = "orkeon.crew.process";

    /// <summary>The number of tasks of the crew.</summary>
    public const string CrewTaskCount = "orkeon.crew.task_count";

    /// <summary>The id of the task.</summary>
    public const string TaskId = "orkeon.task.id";

    /// <summary>How the task ended: completed, failed or cancelled.</summary>
    public const string TaskStatus = "orkeon.task.status";

    /// <summary>The memory operation: recall or store.</summary>
    public const string MemoryOperation = "orkeon.memory.operation";

    /// <summary>The memory provider the operation went through.</summary>
    public const string MemoryProvider = "orkeon.memory.provider";

    /// <summary>The number of memories the operation read or wrote.</summary>
    public const string MemoryItemCount = "orkeon.memory.item_count";

    /// <summary>The event of a task handed over to another agent.</summary>
    public const string HandOverEvent = "hand_over";

    /// <summary>The tag of a hand-over: the agent the task goes to.</summary>
    public const string HandOverTo = "orkeon.handover.to";

    /// <summary>The <c>error.type</c> of a task span that ended failed.</summary>
    public const string ErrorTaskFailed = "task_failed";

    /// <summary>The <c>error.type</c> of a task span that ended cancelled.</summary>
    public const string ErrorTaskCancelled = "task_cancelled";

    /// <summary>The <c>error.type</c> of a run that failed without an exception.</summary>
    public const string ErrorRunFailure = "run_failure";

    /// <summary>The <c>error.type</c> of a crew run that failed without an exception type to name.</summary>
    public const string ErrorCrewFailed = "crew_failed";
}
