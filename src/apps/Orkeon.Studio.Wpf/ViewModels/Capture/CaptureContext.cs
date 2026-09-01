using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.ViewModels.Capture;

/// <summary>What an arrange is handed. The window is deliberately absent.</summary>
internal sealed class CaptureContext
{
    /// <summary>How long held work has to finish before the stop is called faulty.</summary>
    private static readonly TimeSpan DrainLimit = TimeSpan.FromSeconds(15);

    private readonly List<Task> _held = [];

    /// <summary>The window's ViewModel, over a seeded world.</summary>
    public required MainWindowViewModel Shell { get; init; }

    /// <summary>The machine underneath it — its scripted CLI, its paths, its seeded content.</summary>
    public required CaptureWorld World { get; init; }

    /// <summary>The one seam onto WPF.</summary>
    public required ICaptureSurface Surface { get; init; }

    /// <summary>Which language, theme and mode this pass is in.</summary>
    public required CaptureAppearance Appearance { get; init; }

    /// <summary>
    /// Registers a task the executor awaits AFTER the teardown — how a stop keeps a run parked
    /// across several shots without leaking it into the next one.
    /// </summary>
    public void HoldUntilTeardown(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        _held.Add(task);
    }

    /// <summary>Awaits and forgets whatever was held.</summary>
    public async Task DrainHeldAsync()
    {
        if (_held.Count == 0)
            return;

        var held = _held.ToArray();
        _held.Clear();

        // A parked run that ends in a fault is the stop's business, not the campaign's: the
        // per-stop barrier upstream already reported it, and re-throwing here would take the
        // teardown down with it. The timeout is the other half: a stop that parks a run and never
        // releases it is a bug in the stop, and it has to read as one instead of hanging a
        // campaign somebody left running.
        var all = Task.WhenAll(held).ContinueWith(static _ => { }, TaskScheduler.Default);
        if (await Task.WhenAny(all, Task.Delay(DrainLimit)) != all)
            throw new TimeoutException("a stop parked work it never released");
    }
}
