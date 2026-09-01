using Orkeon.Studio.Wpf.ViewModels.Capture;

namespace Orkeon.Studio.Wpf.Tests.Capture;

/// <summary>
/// The campaign's WPF seam, without WPF. It records where the walk stood and answers every
/// settle instantly, which is what lets the entire catalogue be replayed on the Linux runner.
/// </summary>
internal sealed class RecordingCaptureSurface : ICaptureSurface
{
    /// <summary>Every screen the walk asked for, in order.</summary>
    public List<CaptureScreen> Visited { get; } = [];

    /// <summary>Whether the startup plate is currently up.</summary>
    public bool SplashVisible { get; private set; }

    /// <summary>The tour stop currently shown, or null.</summary>
    public int? TourStep { get; private set; }

    public Task ShowAsync(CaptureScreen screen)
    {
        Visited.Add(screen);
        return Task.CompletedTask;
    }

    public void ShowSplash() => SplashVisible = true;

    public void HideSplash() => SplashVisible = false;

    public void StartTourAt(int stepIndex) => TourStep = stepIndex;

    public void EndTour() => TourStep = null;

    public Task SettleAsync() => Task.CompletedTask;
}
