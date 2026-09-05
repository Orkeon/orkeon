using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Launch;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The behaviours the WPF launcher owes the terminal one (spec §10: one Core surface, three
/// renderings). Each one was a real divergence: a dry run whose verdict was not shown, a dry
/// run recorded as if it had launched a crew, and a "replay" that only refilled the form.
/// </summary>
public sealed class LaunchParityTests
{
    private const string Crew = "/crews/team.yaml";

    private static (LaunchTabViewModel Tab, FakeProcessLauncher Launcher, FakeLaunchHistoryStore History) Build(
        int exitCode = OrkeonExitCodes.Success)
    {
        var launcher = new FakeProcessLauncher { ExitCode = exitCode };
        var history = new FakeLaunchHistoryStore();

        var tab = new LaunchTabViewModel(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(
                launcher, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            TargetProbe = new FakeTargetProbe().WithFile(Crew),
            Directories = new FakeDirectoryProbe(),
            HistoryStore = history,
            SettingsStore = new FakeAppSettingsStore(),
        });

        tab.Target.Select(Crew);
        return (tab, launcher, history);
    }

    [Fact]
    public async Task Should_ShowTheValidationVerdict_When_TheDryRunSucceeds()
    {
        var (tab, _, _) = Build();

        await tab.ValidateAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith(LaunchOutcomeFormatter.ValidationOk, tab.StatusMessage!, StringComparison.Ordinal);
        Assert.Contains(tab.Log.Lines, line => line.Text.Contains(LaunchOutcomeFormatter.ValidationOk, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_ShowTheValidationVerdict_When_TheDryRunFails()
    {
        var (tab, _, _) = Build(OrkeonExitCodes.ScriptError);

        await tab.ValidateAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith(LaunchOutcomeFormatter.ValidationFailed, tab.StatusMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ShowTheExitCode_When_TheRunIsReal()
    {
        var (tab, _, _) = Build(OrkeonExitCodes.RuntimeError);

        await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("VALIDATION", tab.StatusMessage!, StringComparison.Ordinal);
        Assert.Contains("Exit code", tab.StatusMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_KeepDryRunsOutOfTheHistory_Because_TheyLaunchedNothing()
    {
        var (tab, _, history) = Build();

        await tab.ValidateAsync(TestContext.Current.CancellationToken);

        Assert.Empty(history.Recorded);
        Assert.Empty(tab.History.Entries);
    }

    [Fact]
    public async Task Should_RecordRealRunsInTheHistory()
    {
        var (tab, _, history) = Build();

        await tab.RunAsync(TestContext.Current.CancellationToken);

        var recorded = Assert.Single(history.Recorded);
        Assert.Equal(Crew, recorded.Target);
    }

    [Fact]
    public async Task Should_ReplayTheRecordedArgumentsVerbatim_Even_WhenTheFormHasSinceChanged()
    {
        var (tab, launcher, _) = Build();
        var entry = LaunchHistoryEntry.Starting(
            Crew,
            ["run", Crew, "-V", "TOPIC=recorded"],
            settingsPath: null,
            workingDirectory: "/crews");

        // The form now says something else entirely; the replay must ignore it.
        tab.Options.AddVariable("TOPIC", "edited-since");

        await tab.ReplayCommand.ExecuteAsync(entry);

        var request = Assert.Single(launcher.Requests);
        Assert.Equal(["run", Crew, "-V", "TOPIC=recorded"], request.Arguments);
        Assert.Equal("/crews", request.WorkingDirectory);
    }

    [Fact]
    public async Task Should_RunOnASingleClick_When_TheHistoryPanelAsksForAReplay()
    {
        // The terminal launcher replays on one keypress; the WPF one used to fill the form in
        // and wait for a second click on Run.
        var (tab, launcher, history) = Build();
        var entry = LaunchHistoryEntry.Starting(Crew, ["run", Crew], null, "/crews");
        history.History = LaunchHistory.Empty.Add(entry);

        await tab.History.LoadAsync(TestContext.Current.CancellationToken);
        tab.History.ReplayCommand.Execute(null);

        var request = Assert.Single(launcher.Requests);
        Assert.Equal(["run", Crew], request.Arguments);
    }

    [Fact]
    public async Task Should_RefuseToReplay_When_TheEntryRecordedNoArguments()
    {
        var (tab, launcher, _) = Build();

        var result = await tab.ReplayAsync(
            LaunchHistoryEntry.Starting(Crew, [], null, null),
            TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Empty(launcher.Requests);
        Assert.Contains("Nothing to replay", tab.StatusMessage!, StringComparison.Ordinal);
    }
}

/// <summary>
/// The shape chooser of §5.1 once the user has answered it with the shape the CLI refuses.
/// </summary>
public sealed class ContestedDirectoryTests
{
    private static TargetSelectionViewModel Contested()
    {
        var probe = new FakeTargetProbe()
            .WithDirectory("/crews/both")
            .WithDirectory("/crews/both/agents")
            .WithFile("/crews/both/crew.ork.ts");

        var selection = new TargetSelectionViewModel(probe);
        selection.Select("/crews/both");
        return selection;
    }

    [Fact]
    public void Should_OfferTheChoice_When_ADirectoryMatchesBothShapes()
    {
        var selection = Contested();

        Assert.True(selection.IsAmbiguous);
        Assert.True(selection.NeedsShapeChoice);
        Assert.False(selection.IsResolved);
    }

    [Fact]
    public void Should_ReportTheRemediation_When_TheYamlLayoutIsBlockedByAScript()
    {
        var selection = Contested();

        selection.PreferredDirectoryKind = RunTargetKind.MultiFileCrewDirectory;

        Assert.False(selection.IsResolved);
        Assert.Equal(RunTargetCodes.YamlLayoutBlockedByScript, selection.ErrorCode);
        Assert.Contains("Move or remove the script(s)", selection.YamlLayoutBlockedMessage!, StringComparison.Ordinal);
        Assert.Contains("Move or remove the script(s)", selection.StatusDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_KeepTheChooserOnScreen_When_TheYamlLayoutIsBlocked()
    {
        // Otherwise the answer that cannot work also removes the way to give the other one.
        var selection = Contested();

        selection.PreferredDirectoryKind = RunTargetKind.MultiFileCrewDirectory;

        Assert.True(selection.NeedsShapeChoice);
        Assert.False(selection.IsAmbiguous);
    }

    [Fact]
    public void Should_ResolveTheScriptDirectory_When_ThatShapeIsPreferred()
    {
        var selection = Contested();

        selection.PreferredDirectoryKind = RunTargetKind.ScriptDirectory;

        Assert.True(selection.IsResolved);
        Assert.Equal(RunTargetKind.ScriptDirectory, selection.Target!.Kind);
        Assert.False(selection.NeedsShapeChoice);
        Assert.Null(selection.YamlLayoutBlockedMessage);
    }
}
