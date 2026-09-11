using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Orkeon.Studio.Wpf.Services.Capture;

/// <summary>What the settle managed to confirm, so a doubtful shot can say so rather than look sure.</summary>
/// <param name="LayoutStable">Layout stopped dirtying itself within the pass budget.</param>
/// <param name="ImagesReady">No bitmap was still downloading when the shot was taken.</param>
/// <param name="Ticked">At least one composition frame was observed.</param>
internal sealed record SettleReport(bool LayoutStable, bool ImagesReady, bool Ticked)
{
    /// <summary>The one-line form that goes into the manifest.</summary>
    public bool IsClean => LayoutStable && ImagesReady;
}

/// <summary>
/// Getting the window to stand still before it is photographed.
/// <para>
/// The mental model that decides every line here: <see cref="RenderTargetBitmap.Render"/> is NOT a
/// screen grab. It re-walks the visual tree and rasterises it in software, so it does not need the
/// compositor to have presented anything — but it does need the tree to have been measured and
/// arranged, and it samples animated properties at whatever value the last tick wrote. Hence:
/// force layout and confirm it stopped moving, then bound the wait on frames rather than sleeping.
/// </para>
/// <para>
/// What this replaces: two dispatcher fences and <c>Task.Delay(120)</c>. The fences proved nothing
/// — WPF orders Loaded below Render, so draining to Loaded already drains Render, and the second
/// await returned immediately. The delay was the only thing creating slack, and a wall clock is
/// not a guarantee at a hundred stops on a cold window.
/// </para>
/// </summary>
internal static class CaptureSettle
{
    /// <summary>How many times layout may dirty itself again before we call it unstable.</summary>
    private const int MaxLayoutPasses = 8;

    /// <summary>
    /// A frame must arrive within this or the media context has gone quiet.
    /// <para>
    /// Not defensive padding: WPF only ticks when something is dirty or an animation is running,
    /// so once <see cref="CapturePose"/> has posed every storyboard a still window stops ticking
    /// altogether and an unbounded wait would hang the campaign.
    /// </para>
    /// </summary>
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>How long a still-downloading bitmap is given before the shot goes ahead anyway.</summary>
    private static readonly TimeSpan ImageTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The full settle, in the order the pipeline actually runs.</summary>
    public static async Task<SettleReport> SettleAsync(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var dispatcher = window.Dispatcher;

        // 1. Bindings. An Item[] invalidation from a language switch, or a PropertyChanged hop out
        //    of a ViewModel, is serviced at DataBind.
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);

        // 2. Layout, forced and confirmed stable.
        var layoutStable = await LayoutStableAsync(window);

        // 3. Everything that waits for "the screen is up" has now run — the icons' geometry
        //    rebuild, the tour's deferred Layout, the panels' own Loaded triggers.
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);

        // 4. Deferred content the layout pass does not cover.
        var imagesReady = await ImagesReadyAsync(window);

        // 5. Two composition ticks: the first flushes what step 2 dirtied, the second says nothing
        //    dirtied it again. A miss is recorded, never fatal — see FrameTimeout.
        var ticked = await NextFrameAsync(dispatcher);
        if (ticked)
            await NextFrameAsync(dispatcher);

        // 6. Anything the app deferred for itself.
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

        // 7. The pose goes LAST: steps 2-6 may have materialised new items, each re-firing its own
        //    Loaded trigger and starting its own copy of an endless storyboard.
        CapturePose.Apply(window);
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        window.UpdateLayout();

        return new SettleReport(layoutStable, imagesReady, ticked);
    }

    /// <summary>
    /// Forces measure and arrange, and repeats until a pass stops dirtying the tree. One
    /// <c>UpdateLayout()</c> is not enough on a screen shown for the first time: a template applied
    /// during arrange can invalidate its own parent.
    /// </summary>
    public static async Task<bool> LayoutStableAsync(FrameworkElement root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var moved = false;
        void OnLayoutUpdated(object? sender, EventArgs e) => moved = true;

        root.LayoutUpdated += OnLayoutUpdated;
        try
        {
            for (var pass = 0; pass < MaxLayoutPasses; pass++)
            {
                moved = false;
                root.UpdateLayout();

                // LayoutUpdated is raised from the layout pass itself; the hop lets any pass that
                // pass QUEUED run before we ask again.
                await root.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
                if (!moved)
                    return true;
            }

            return false;
        }
        finally
        {
            root.LayoutUpdated -= OnLayoutUpdated;
        }
    }

    /// <summary>
    /// Waits for one composition frame, or reports that none came.
    /// <c>CompositionTarget.Rendering</c> fires just BEFORE the frame is composed, so the animated
    /// values for it are already written; the dispatcher hop lands after it is done.
    /// </summary>
    public static async Task<bool> NextFrameAsync(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? onRendering = null;
        onRendering = (_, _) =>
        {
            CompositionTarget.Rendering -= onRendering;
            _ = dispatcher.BeginInvoke(DispatcherPriority.Render, () => arrived.TrySetResult());
        };

        CompositionTarget.Rendering += onRendering;
        try
        {
            return await CompletedWithinAsync(arrived.Task, FrameTimeout);
        }
        finally
        {
            CompositionTarget.Rendering -= onRendering;
        }
    }

    /// <summary>
    /// Awaits every bitmap still downloading. Today every image is a pack Resource whose stream is
    /// already in memory, so this passes straight through — it is here because the splash and the
    /// About plate are precisely the two shots where a half-drawn image would be most obvious, and
    /// a source that is ever anything but a Resource would decode for real.
    /// </summary>
    private static async Task<bool> ImagesReadyAsync(DependencyObject root)
    {
        var pending = CaptureVisualTree.Descendants(root)
            .OfType<Image>()
            .Select(image => image.Source)
            .OfType<BitmapImage>()
            .Where(bitmap => bitmap.IsDownloading)
            .ToList();

        if (pending.Count == 0)
            return true;

        return await CompletedWithinAsync(Task.WhenAll(pending.Select(WaitOneAsync)), ImageTimeout);
    }

    // WaitAsync rather than WhenAny + Delay: a Delay that lost the race keeps its timer
    // alive until it fires (CA2027, .NET 11 SDK analyzers).
    private static async Task<bool> CompletedWithinAsync(Task task, TimeSpan timeout)
    {
        try
        {
            await task.WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static Task WaitOneAsync(BitmapImage bitmap)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? onDone = null;
        EventHandler<ExceptionEventArgs>? onFailed = null;

        onDone = (_, _) =>
        {
            bitmap.DownloadCompleted -= onDone;
            bitmap.DownloadFailed -= onFailed;
            done.TrySetResult();
        };
        onFailed = (_, _) =>
        {
            bitmap.DownloadCompleted -= onDone;
            bitmap.DownloadFailed -= onFailed;
            // A failed decode is not a reason to stall the campaign: the shot will show whatever
            // the broken image shows, which is itself worth photographing.
            done.TrySetResult();
        };

        bitmap.DownloadCompleted += onDone;
        bitmap.DownloadFailed += onFailed;
        return done.Task;
    }
}
