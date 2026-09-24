using Orkeon.Studio.Wpf.ViewModels.Capture;
using Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;
using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests.Capture;

/// <summary>
/// The status bar as the campaign photographs it (STUDIO-34): the Run screen's in-flight shot is
/// the one place the bar carries a group, and it has to show a run in flight — not one that
/// already said it was over — for the same duration in every pass.
/// </summary>
public sealed class StatusBarCaptureTests : IAsyncLifetime
{
    private CaptureWorlds _worlds = null!;

    public async ValueTask InitializeAsync() => _worlds = await CaptureWorlds.CreateAsync();

    public ValueTask DisposeAsync()
    {
        _worlds.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task The_in_flight_run_shot_shows_a_run_in_flight_on_the_bar_two_minutes_in()
    {
        var appearance = new CaptureAppearance("fr", IsDark: false, UiModeViewModel.Expert);
        var shell = CaptureShellBuilder.Build(_worlds.Seeded, new CaptureHost
        {
            Dispatcher = ImmediateUiDispatcher.Instance,
            Language = appearance.Language,
            Mode = appearance.Mode,
        });
        await CaptureShellBuilder.PrepareAsync(shell, _worlds.Seeded, TestContext.Current.CancellationToken);
        var context = new CaptureContext
        {
            Shell = shell,
            World = _worlds.Seeded,
            Surface = new RecordingCaptureSurface(),
            Appearance = appearance,
        };
        var stop = CaptureCatalog.All.Single(candidate => candidate.Name == "executer-en-cours");

        await stop.Arrange(context);
        var (tone, duration, active) = (shell.StatusBar.Launch.Tone, shell.StatusBar.Launch.Duration, shell.StatusBar.Launch.IsActive);
        await stop.Teardown(context);
        await context.DrainHeldAsync();

        // The seeded stream starts at 07:10:00 and the campaign's clock stops at 07:12:00.
        Assert.True(active);
        Assert.Equal("running", tone);
        Assert.Equal("2 min 0 s", duration);
        Assert.True(shell.StatusBar.IsAtRest);
    }
}
