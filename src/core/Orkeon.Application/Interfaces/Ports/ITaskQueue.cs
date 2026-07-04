namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Interface for a high-performance asynchronous task queue.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "The 'Queue' suffix accurately describes this FIFO enqueue/dequeue port abstraction; it is the correct domain term, not a claim of deriving from System.Collections.Queue.")]
public interface ITaskQueue<T>
{
    /// <summary>
    /// Enqueues an item to the queue asynchronously.
    /// </summary>
    ValueTask EnqueueAsync(T item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dequeues all items as an async enumerable stream.
    /// </summary>
    IAsyncEnumerable<T> DequeueAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current number of items in the queue.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets whether the queue has been marked as complete for adding.
    /// </summary>
    bool IsCompleted { get; }

    /// <summary>
    /// Marks the queue as complete for adding new items.
    /// </summary>
    void CompleteAdding();
}

/// <summary>
/// Work item for the task pipeline.
/// </summary>
public record WorkItem<T>(
    T Task,
    string Id,
    HashSet<string> Dependencies,
    TaskCompletionSource<object?> CompletionSource)
{
    /// <summary>
    /// Creates a work item without dependencies.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000", Justification = "Idiomatic static factory on a generic type.")]
    public static WorkItem<T> Create(T task, string id)
    {
        return new WorkItem<T>(
            task,
            id,
            [],
            new TaskCompletionSource<object?>());
    }

    /// <summary>
    /// Creates a work item with dependencies.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1000", Justification = "Idiomatic static factory on a generic type.")]
    public static WorkItem<T> Create(T task, string id, IEnumerable<string> dependencies)
    {
        return new WorkItem<T>(
            task,
            id,
            [.. dependencies],
            new TaskCompletionSource<object?>());
    }
}
