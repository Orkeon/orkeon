using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The v3 shell chrome: the window-wide Novice/Expert switch and the about-the-app overlay.
/// Both are pure ViewModel state — the XAML only binds to them — so everything the pill and
/// the dialog do is asserted here, on Linux, without a window.
/// </summary>
public sealed class ShellChromeViewModelTests
{
    // ── the mode switch ──

    [Fact]
    public void Should_DefaultToNovice_When_NothingWasPersisted()
    {
        var mode = new UiModeViewModel();

        Assert.True(mode.IsNovice);
        Assert.False(mode.IsExpert);
        Assert.True(mode.HelpOn);
    }

    [Fact]
    public void Should_ReadAnythingUnknownAsNovice_So_ACorruptPreferenceNeverLandsInExpert()
    {
        // Expert exposes the machinery; it must be an explicit choice, never a parsing accident.
        Assert.True(new UiModeViewModel("EXPERT").IsExpert);
        Assert.True(new UiModeViewModel("guru").IsNovice);
        Assert.True(new UiModeViewModel("").IsNovice);
        Assert.True(new UiModeViewModel(null).IsNovice);
    }

    [Fact]
    public void Should_PersistAndNotify_When_TheUserSwitches()
    {
        var persisted = new List<string>();
        var mode = new UiModeViewModel(persist: persisted.Add);
        var raised = new List<string>();
        mode.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        mode.SetExpertCommand.Execute(null);

        Assert.Equal([UiModeViewModel.Expert], persisted);
        Assert.True(mode.IsExpert);
        Assert.False(mode.HelpOn);
        Assert.Contains(nameof(UiModeViewModel.IsNovice), raised);
        Assert.Contains(nameof(UiModeViewModel.IsExpert), raised);
        Assert.Contains(nameof(UiModeViewModel.HelpOn), raised);
    }

    [Fact]
    public void Should_NotRePersist_When_TheModeDoesNotActuallyChange()
    {
        var persisted = new List<string>();
        var mode = new UiModeViewModel(persist: persisted.Add);

        mode.SetNoviceCommand.Execute(null);

        Assert.Empty(persisted);
    }

    // ── the about overlay ──

    private static (AboutViewModel About, FakeProcessLauncher Launcher) BuildAbout(
        params string[] versionOutput)
    {
        var launcher = new FakeProcessLauncher();
        foreach (var line in versionOutput)
            launcher.OutputToEmit.Add(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, line));

        var runner = new OrkeonProcessRunner(
            launcher, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled()));

        return (new AboutViewModel(runner, dispatcher: null, studioVersion: "1.0.0-rc.2"), launcher);
    }

    [Fact]
    public void Should_AskTheCliItsVersionOnceOnFirstOpen_And_ShowItInTheLine()
    {
        var (about, launcher) = BuildAbout("1.0.0-rc.2+8377b60772dc");

        about.OpenCommand.Execute(null);
        about.CloseCommand.Execute(null);
        about.OpenCommand.Execute(null);

        Assert.True(about.IsOpen);
        Assert.Single(launcher.Requests);
        Assert.Equal(["--version"], launcher.Requests[0].Arguments);
        Assert.Equal("1.0.0-rc.2", about.CliVersion);
        Assert.Equal($"Studio 1.0.0-rc.2 · CLI 1.0.0-rc.2 · .NET {Environment.Version.Major}", about.VersionLine);
    }

    [Fact]
    public void Should_LeaveTheCliSegmentOut_When_TheBinaryAnswersNothing()
    {
        // An About box is the one place an error message helps nobody: no CLI, no segment.
        var (about, _) = BuildAbout();

        about.OpenCommand.Execute(null);

        Assert.Null(about.CliVersion);
        Assert.Equal($"Studio 1.0.0-rc.2 · .NET {Environment.Version.Major}", about.VersionLine);
    }

    // ── the shell exposes both ──

    [Fact]
    public void Should_ExposeModeAndAbout_And_HandTheInitialModeThrough()
    {
        var window = new MainWindowViewModel(
            new StudioServices
            {
                SettingsStore = new FakeAppSettingsStore(),
                Directories = new FakeDirectoryProbe(),
                TargetProbe = new FakeTargetProbe(),
                Picker = new FakePathPicker(),
                ProcessRunner = new OrkeonProcessRunner(
                    new FakeProcessLauncher(),
                    new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                HistoryStore = new FakeLaunchHistoryStore(),
            },
            new StudioUiPreferences { InitialMode = "expert" },
            globalPathOverride: "/home/user/.config/Orkeon/appsettings.json");

        Assert.True(window.Mode.IsExpert);
        Assert.False(window.About.IsOpen);
    }
}
