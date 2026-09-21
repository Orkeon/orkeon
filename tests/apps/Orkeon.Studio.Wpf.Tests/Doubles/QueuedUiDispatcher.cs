using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>
/// An <see cref="IUiDispatcher"/> that holds every post until the test drains it — the shape
/// of the WPF dispatcher seen from a background thread, where a post is a queued operation
/// and not a call. It shows what <see cref="ImmediateUiDispatcher"/> hides: the continuation
/// of an await can run before the posts made ahead of it have landed.
/// </summary>
public sealed class QueuedUiDispatcher : IUiDispatcher
{
    private readonly Queue<Action> _queue = new();

    /// <summary>How many posts are waiting.</summary>
    public int Pending => _queue.Count;

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _queue.Enqueue(action);
    }

    /// <summary>Runs every waiting post in order, including those posted while draining.</summary>
    public int Drain()
    {
        var ran = 0;
        while (_queue.TryDequeue(out var action))
        {
            action();
            ran++;
        }

        return ran;
    }
}
