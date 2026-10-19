namespace Orkeon.Scripting.Runtime;

/// <summary>
/// The single start of a root pump (a synchronous drain of a JS engine). The pump runs on a dedicated
/// thread, not a thread-pool worker: a pump holds its thread for the whole drain, so pool workers
/// would be capped and starved by concurrent pumps (GAP-57).
/// </summary>
internal static class RootPump
{
    /// <summary>Runs <paramref name="pump"/> to completion on one dedicated thread. DenyChildAttach keeps the Task.Run semantics: no child task attaches to the pump.</summary>
    internal static Task<T> Start<T>(Func<T> pump, CancellationToken cancellationToken) =>
        Task.Factory.StartNew(pump, cancellationToken, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
}
