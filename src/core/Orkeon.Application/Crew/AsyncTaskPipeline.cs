
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Task;

namespace Orkeon.Application.Crew;

/// <summary>
/// High-performance asynchronous task pipeline with dependency management.
/// </summary>
public partial class AsyncTaskPipeline<T> : ITaskQueue<T>, IDisposable
{
    private readonly Channel<WorkItem<T>> _channel;
    private readonly ConcurrentDictionary<string, WorkItem<T>> _pendingTasks;
    private readonly ConcurrentDictionary<string, bool> _completedTasks;
    private readonly ILogger<AsyncTaskPipeline<T>> _logger;
    private readonly int _maxConcurrency;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private long _isCompleted;

    /// <summary>
    /// Initializes a new instance of <see cref="AsyncTaskPipeline{T}"/>.
    /// </summary>
    public AsyncTaskPipeline(
        int capacity = 100,
        int maxConcurrency = -1, // -1 means use processor count
        ILogger<AsyncTaskPipeline<T>>? logger = null)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };

        _channel = Channel.CreateBounded<WorkItem<T>>(options);
        _pendingTasks = new ConcurrentDictionary<string, WorkItem<T>>();
        _completedTasks = new ConcurrentDictionary<string, bool>();
        _maxConcurrency = maxConcurrency == -1 ? Environment.ProcessorCount : maxConcurrency;
        _concurrencySemaphore = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<AsyncTaskPipeline<T>>.Instance;
    }

    /// <summary>
    /// Gets or sets the count.
    /// </summary>
    public int Count => _pendingTasks.Count + _channel.Reader.Count;

    /// <summary>
    /// Gets or sets a value indicating whether is completed.
    /// </summary>
    public bool IsCompleted => Interlocked.Read(ref _isCompleted) == 1;

    /// <summary>
    /// Enqueue Async.
    /// </summary>
    public async ValueTask EnqueueAsync(T item, CancellationToken cancellationToken = default)
    {
        if (IsCompleted)
            throw new InvalidOperationException("Cannot enqueue items after completing the pipeline");

        var workItem = item as WorkItem<T> ?? WorkItem<T>.Create(item, Guid.NewGuid().ToString());
        _pendingTasks.TryAdd(workItem.Id, workItem);

        // Check if dependencies are satisfied
        if (AreDependenciesSatisfied(workItem))
        {
            await _channel.Writer.WriteAsync(workItem, cancellationToken).ConfigureAwait(false);
            _pendingTasks.TryRemove(workItem.Id, out _);
        }

        LogWorkItemEnqueued(workItem.Id, workItem.Dependencies.Count);
    }

    /// <summary>
    /// Dequeue All Async.
    /// </summary>
    public async IAsyncEnumerable<T> DequeueAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var workItem in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return workItem.Task;

            // Mark as completed and check for newly ready tasks
            MarkAsCompleted(workItem.Id);
            await CheckAndEnqueueReadyTasks(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Complete Adding.
    /// </summary>
    public void CompleteAdding()
    {
        if (Interlocked.CompareExchange(ref _isCompleted, 1, 0) == 0)
        {
            // Only complete if all pending tasks have been processed
            if (_pendingTasks.IsEmpty)
            {
                _channel.Writer.TryComplete();
            }

            LogPipelineCompleted();
        }
    }

    /// <summary>
    /// Creates a processing pipeline that executes work items with proper dependency management.
    /// </summary>
    public async System.Threading.Tasks.Task<List<TResult>> ProcessAsync<TResult>(
        Func<T, CancellationToken, System.Threading.Tasks.Task<TResult>> processor,
        CancellationToken cancellationToken = default)
    {
        var results = new ConcurrentBag<TResult>();
        var tasks = new List<System.Threading.Tasks.Task>();

        // Start consumer tasks
        for (int i = 0; i < _maxConcurrency; i++)
        {
            tasks.Add(System.Threading.Tasks.Task.Run(async () =>
            {
                await foreach (var item in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    await ProcessSingleItemAsync(item, processor, results, cancellationToken).ConfigureAwait(false);
                }
            }, cancellationToken));
        }

        // Wait for all processors to complete
        await System.Threading.Tasks.Task.WhenAll(tasks).ConfigureAwait(false);

        return results.ToList();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Per-item fault barrier: a failing work item's exception is captured onto its CompletionSource so one faulty item cannot abort the shared consumer tasks processing the channel.")]
    private async System.Threading.Tasks.Task ProcessSingleItemAsync<TResult>(
        WorkItem<T> item,
        Func<T, CancellationToken, System.Threading.Tasks.Task<TResult>> processor,
        ConcurrentBag<TResult> results,
        CancellationToken cancellationToken)
    {
        await _concurrencySemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LogProcessingWorkItem(item.Id);
            var result = await processor(item.Task, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            await CompleteWorkItemAsync(item, result, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogWorkItemError(ex);
            if (item is WorkItem<T> errorWorkItem)
            {
                errorWorkItem.CompletionSource.TrySetException(ex);
            }
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    private async System.Threading.Tasks.Task CompleteWorkItemAsync<TResult>(
        WorkItem<T> item,
        TResult result,
        CancellationToken cancellationToken)
    {
        if (item is WorkItem<T> workItem)
        {
            workItem.CompletionSource.TrySetResult(result);
            MarkAsCompleted(workItem.Id);
            await CheckAndEnqueueReadyTasks(cancellationToken).ConfigureAwait(false);
        }
    }

    private bool AreDependenciesSatisfied(WorkItem<T> workItem)
    {
        return workItem.Dependencies.All(dep => _completedTasks.ContainsKey(dep));
    }

    private void MarkAsCompleted(string taskId)
    {
        _completedTasks.TryAdd(taskId, true);
        LogTaskMarkedCompleted(taskId);
    }

    private async System.Threading.Tasks.Task CheckAndEnqueueReadyTasks(CancellationToken cancellationToken)
    {
        var readyTasks = _pendingTasks.Values
            .Where(AreDependenciesSatisfied)
            .ToList();

        foreach (var task in readyTasks)
        {
            if (_pendingTasks.TryRemove(task.Id, out _))
            {
                await _channel.Writer.WriteAsync(task, cancellationToken).ConfigureAwait(false);
                LogBlockedTaskEnqueued(task.Id);
            }
        }

        // Check if we can complete the channel
        if (IsCompleted && _pendingTasks.IsEmpty)
        {
            _channel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Dispose.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Dispose(bool).</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _concurrencySemaphore?.Dispose();
            CompleteAdding();
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Enqueued work item {Id} with {DependencyCount} dependencies")]
    private partial void LogWorkItemEnqueued(string id, int dependencyCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pipeline marked as complete for adding")]
    private partial void LogPipelineCompleted();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processing work item {Id}")]
    private partial void LogProcessingWorkItem(string id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error processing work item")]
    private partial void LogWorkItemError(Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Marked task {TaskId} as completed")]
    private partial void LogTaskMarkedCompleted(string taskId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Enqueued previously blocked task {TaskId}")]
    private partial void LogBlockedTaskEnqueued(string taskId);
}

/// <summary>
/// Specialized task pipeline for Task processing.
/// </summary>
public sealed class CrewTaskPipeline : AsyncTaskPipeline<CrewTask>
{
    /// <summary>
    /// Initializes a new instance of <see cref="CrewTaskPipeline"/>.
    /// </summary>
    public CrewTaskPipeline(
        int capacity = 100,
        int maxConcurrency = -1, // -1 means use processor count
        ILogger<CrewTaskPipeline>? logger = null)
        : base(capacity, maxConcurrency, logger as ILogger<AsyncTaskPipeline<CrewTask>>)
    {
    }

    /// <summary>
    /// Enqueues a crew task with its dependencies.
    /// </summary>
    public ValueTask EnqueueTaskAsync(
        CrewTask task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        return EnqueueTaskCoreAsync();

        async ValueTask EnqueueTaskCoreAsync()
        {
            var dependencies = task.Dependencies?.Select(d => d.Value.ToString()).ToHashSet() ?? [];
            var workItem = WorkItem<CrewTask>.Create(task, task.Id.Value.ToString(), dependencies);

            await EnqueueAsync(workItem.Task, cancellationToken).ConfigureAwait(false);
        }
    }
}
