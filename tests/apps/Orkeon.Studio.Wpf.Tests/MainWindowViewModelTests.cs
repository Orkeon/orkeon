using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests;

public sealed class MainWindowViewModelTests
{
    private static MainWindowViewModel Build() =>
        new(new FakeAppSettingsStore(),
            new FakeDirectoryProbe(),
            new FakeTargetProbe(),
            new FakePathPicker(),
            new OrkeonProcessRunner(
                new FakeProcessLauncher(),
                new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            new FakeLaunchHistoryStore(),
            dispatcher: null,
            globalPathOverride: "/home/user/.config/Orkeon/appsettings.json");

    [Fact]
    public void Should_ExposeTheTwoTabs()
    {
        var window = Build();

        Assert.NotNull(window.Config);
        Assert.NotNull(window.Launch);
        Assert.Equal(0, window.SelectedTabIndex);
    }

    [Fact]
    public void Should_ConstructWithoutAWindow_So_TheSuiteRunsOnLinux()
    {
        // This is the whole point of the split: the ViewModels never touch a WPF type, so the entire
        // behaviour of both tabs is asserted on the Linux runner and only the XAML needs Windows.
        var window = Build();

        Assert.True(window.Config.Location.CanSave);
        Assert.False(window.Launch.RunCommand.CanExecute(null));
    }

    [Fact]
    public async Task Should_LocateTheCliAndLoadTheHistory_When_Initialized()
    {
        var window = Build();

        await window.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(window.Launch.IsBinaryAvailable);
    }

    [Fact]
    public void Should_ShareTheProcessRunner_Between_TheDiagnosticAndTheLauncher()
    {
        // Both tabs invoke the same co-installed CLI; wiring two runners would let them disagree
        // about which binary is in use.
        var launcher = new FakeProcessLauncher();
        var window = new MainWindowViewModel(
            new FakeAppSettingsStore(),
            new FakeDirectoryProbe(),
            new FakeTargetProbe(),
            new FakePathPicker(),
            new OrkeonProcessRunner(launcher, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            new FakeLaunchHistoryStore(),
            dispatcher: null,
            globalPathOverride: "/tmp/appsettings.json");

        window.Config.Diagnostic.RunCommand.Execute(null);

        Assert.Single(launcher.Requests);
    }

    [Fact]
    public void Should_NameItselfOrkeonStudio()
    {
        Assert.Equal("Orkeon Studio", MainWindowViewModel.Title);
    }
}
