using Orkeon.Studio.Wpf.ViewModels.Capture;
using Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;
using Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests.Capture;

/// <summary>
/// The campaign, replayed without a window.
/// <para>
/// This is the keystone of the design. The catalogue's arrange bodies touch only ViewModels and one
/// narrow surface, so the whole walk runs on the Linux runner — and every stop's <c>Covers</c> is
/// checked. A stop that quietly stops reaching the state it names fails here, by name, instead of
/// writing a confident picture of the wrong screen on a machine nobody is watching.
/// </para>
/// <para>
/// The walk runs IN ORDER, with the real teardowns, which is also exactly the test for state
/// leaking from one stop into the next: a modal left open by stop 12 breaks stop 13's claim.
/// </para>
/// </summary>
public sealed class CaptureCampaignSemanticTests : IAsyncLifetime
{
    private CaptureWorlds _worlds = null!;

    public async ValueTask InitializeAsync() => _worlds = await CaptureWorlds.CreateAsync();

    public ValueTask DisposeAsync()
    {
        _worlds.Dispose();
        return ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData(UiModeViewModel.Novice)]
    [InlineData(UiModeViewModel.Expert)]
    public async Task Every_stop_reaches_the_state_it_claims_when_the_whole_campaign_runs_in_order(string mode)
    {
        var appearance = new CaptureAppearance("fr", IsDark: false, mode);
        var stops = CaptureCatalog.For(appearance, CaptureMatrix.Default);
        var shells = await BuildShellsAsync(appearance);
        var surface = new RecordingCaptureSurface();
        var failures = new List<string>();

        foreach (var stop in stops)
        {
            var shell = shells[stop.World];
            var context = new CaptureContext
            {
                Shell = shell,
                World = _worlds.For(stop.World),
                Surface = surface,
                Appearance = appearance,
            };

            try
            {
                await surface.ShowAsync(stop.Screen);
                await stop.Arrange(context);
            }
            catch (Exception exception)
            {
                failures.Add($"{stop.Name}: the arrange threw — {exception.Message}");
                continue;
            }

            foreach (var gate in stop.Covers.Where(gate => !GateReader.IsTrue(shell, gate)))
                failures.Add($"{stop.Name}: claims {gate}, and it is false after the arrange");

            foreach (var gate in stop.CoversFalse.Where(gate => GateReader.IsTrue(shell, gate)))
                failures.Add($"{stop.Name}: claims NOT {gate}, and it is true after the arrange");

            await stop.Teardown(context);
            await context.DrainHeldAsync();
        }

        Assert.True(
            failures.Count == 0,
            "A stop that no longer reaches its state still writes a PNG — of the wrong screen, "
            + "silently, on a machine nobody is watching. " + string.Join(" ; ", failures));
    }

    /// <summary>
    /// No stop may leave a scrim up behind it. The next shot would carry a modal nobody asked for,
    /// and at three hundred images nobody would notice which one started it.
    /// </summary>
    [Fact]
    public async Task No_stop_leaves_a_modal_open_behind_it()
    {
        var appearance = new CaptureAppearance("fr", IsDark: false, UiModeViewModel.Expert);
        var shells = await BuildShellsAsync(appearance);
        var surface = new RecordingCaptureSurface();
        var offenders = new List<string>();

        string[] modals =
        [
            "Settings.Profiles.IsEditorOpen",
            "TeamMounts.IsOpen",
            "CreateTeam.AgentEditor.IsOpen",
            "AllowedFolders.IsOpen",
            "FolderPicker.IsOpen",
            "About.IsOpen",
        ];

        foreach (var stop in CaptureCatalog.For(appearance, CaptureMatrix.Default))
        {
            var shell = shells[stop.World];
            var context = new CaptureContext
            {
                Shell = shell,
                World = _worlds.For(stop.World),
                Surface = surface,
                Appearance = appearance,
            };

            await stop.Arrange(context);
            await stop.Teardown(context);
            await context.DrainHeldAsync();

            offenders.AddRange(modals
                .Where(modal => GateReader.IsTrue(shell, modal))
                .Select(modal => $"{stop.Name} left {modal} up"));
        }

        Assert.True(offenders.Count == 0, string.Join(" ; ", offenders));
    }

    private async Task<Dictionary<CaptureWorldKind, MainWindowViewModel>> BuildShellsAsync(
        CaptureAppearance appearance)
    {
        var shells = new Dictionary<CaptureWorldKind, MainWindowViewModel>();

        foreach (var kind in new[] { CaptureWorldKind.Seeded, CaptureWorldKind.Pristine })
        {
            var shell = CaptureShellBuilder.Build(_worlds.For(kind), new CaptureHost
            {
                Dispatcher = ImmediateUiDispatcher.Instance,
                Language = appearance.Language,
                Mode = appearance.Mode,
            });

            await CaptureShellBuilder.PrepareAsync(
                shell, _worlds.For(kind), TestContext.Current.CancellationToken);

            if (string.Equals(appearance.Mode, UiModeViewModel.Expert, StringComparison.Ordinal))
                shell.Mode.SetExpertCommand.Execute(null);
            else
                shell.Mode.SetNoviceCommand.Execute(null);

            shells[kind] = shell;
        }

        return shells;
    }
}
