namespace Orkeon.Infrastructure.Checkpointing;

/// <summary>
/// The task whose questions a human is asked, on the control flow that runs it (FWK-23): the checkpoint key of the task
/// invocation and a counter of the questions it asked. A question's id is <c>{key}.{q}</c>, so a resumed run, which replays
/// the task from its start, asks the same questions under the same ids.
/// </summary>
internal sealed class TaskInputScope
{
    private static readonly AsyncLocal<TaskInputScope?> Slot = new();
    private int _questions;

    private TaskInputScope(string key) => Key = key;

    /// <summary>The checkpoint key of the running task invocation.</summary>
    public string Key { get; }

    /// <summary>The scope of the task running on this control flow, or null.</summary>
    public static TaskInputScope? Current => Slot.Value;

    /// <summary>Makes <paramref name="key"/> the running task until the returned token is disposed.</summary>
    public static IDisposable Enter(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var previous = Slot.Value;
        Slot.Value = new TaskInputScope(key);
        return new Pop(previous);
    }

    /// <summary>The id of the next question this task asks.</summary>
    public string NextRequestId() => $"{Key}.{Interlocked.Increment(ref _questions)}";

    private sealed class Pop(TaskInputScope? previous) : IDisposable
    {
        public void Dispose() => Slot.Value = previous;
    }
}
