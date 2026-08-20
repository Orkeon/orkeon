using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Validation;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Launch;

namespace Orkeon.Studio.Wpf.Tests;

public sealed class TargetSelectionViewModelTests
{
    [Fact]
    public void Should_ResolveAYamlFile_From_ItsExtension()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var selection = new TargetSelectionViewModel(probe);

        selection.Select("/crews/team.yaml");

        Assert.True(selection.IsResolved);
        Assert.Equal(RunTargetKind.YamlFile, selection.Target!.Kind);
        Assert.Equal(RunTargetDialect.Yaml, selection.Target.Dialect);
    }

    [Fact]
    public void Should_ResolveAScriptFile_From_ItsExtension()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.ork.ts");
        var selection = new TargetSelectionViewModel(probe);

        selection.Select("/crews/team.ork.ts");

        Assert.Equal(RunTargetDialect.Script, selection.Target!.Dialect);
    }

    [Fact]
    public void Should_ResolveAMultiFileCrew_From_ItsAgentsFolder()
    {
        var probe = new FakeTargetProbe().WithDirectory("/crews/team").WithDirectory("/crews/team/agents");
        var selection = new TargetSelectionViewModel(probe);

        selection.Select("/crews/team");

        Assert.Equal(RunTargetKind.MultiFileCrewDirectory, selection.Target!.Kind);
        Assert.Equal("/crews/team", selection.RunPath);
    }

    [Fact]
    public void Should_ShowTheVersionRequirement_When_TheTargetIsAMultiFileCrew()
    {
        // §6: `orkeon run <directory>` is a dependency Studio may ship ahead of.
        var probe = new FakeTargetProbe().WithDirectory("/crews/team").WithDirectory("/crews/team/tasks");
        var selection = new TargetSelectionViewModel(probe);

        selection.Select("/crews/team");

        Assert.Equal(RunTargetRequirements.DirectoryRunNotice, selection.DirectoryRunNotice);
        Assert.Contains(
            RunTargetRequirements.MinimumCliVersion,
            selection.DirectoryRunNotice!,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Should_ResolveAScriptDirectory_From_ItsCrewEntryPoint()
    {
        var probe = new FakeTargetProbe().WithDirectory("/crews/ts").WithFile("/crews/ts/crew.ork.ts");
        var selection = new TargetSelectionViewModel(probe);

        selection.Select("/crews/ts");

        Assert.Equal(RunTargetKind.ScriptDirectory, selection.Target!.Kind);
        Assert.Equal("/crews/ts/crew.ork.ts", selection.RunPath);
    }

    [Fact]
    public void Should_RefuseToGuess_When_ADirectoryMatchesBothShapes()
    {
        var probe = new FakeTargetProbe()
            .WithDirectory("/crews/both")
            .WithDirectory("/crews/both/agents")
            .WithFile("/crews/both/crew.ork.ts");
        var selection = new TargetSelectionViewModel(probe);

        selection.Select("/crews/both");

        Assert.False(selection.IsResolved);
        Assert.True(selection.IsAmbiguous);
        Assert.Equal(2, selection.Candidates.Count);
    }

    [Fact]
    public void Should_Resolve_When_TheUserPicksTheShape()
    {
        var probe = new FakeTargetProbe()
            .WithDirectory("/crews/both")
            .WithDirectory("/crews/both/agents")
            .WithFile("/crews/both/crew.ork.ts");
        var selection = new TargetSelectionViewModel(probe);
        selection.Select("/crews/both");

        selection.PreferredDirectoryKind = RunTargetKind.ScriptDirectory;

        Assert.True(selection.IsResolved);
        Assert.Equal("/crews/both/crew.ork.ts", selection.RunPath);
    }

    [Fact]
    public void Should_OfferTheScripts_When_ADirectoryHasSeveralAndNoEntryPoint()
    {
        var probe = new FakeTargetProbe()
            .WithDirectory("/crews/many")
            .WithFile("/crews/many/a.ork.ts")
            .WithFile("/crews/many/b.ork.ts");
        var selection = new TargetSelectionViewModel(probe);

        selection.Select("/crews/many");

        Assert.True(selection.NeedsSelection);
        Assert.Equal(2, selection.Candidates.Count);
    }

    [Fact]
    public void Should_Resolve_When_TheUserPicksACandidate()
    {
        var probe = new FakeTargetProbe()
            .WithDirectory("/crews/many")
            .WithFile("/crews/many/a.ork.ts")
            .WithFile("/crews/many/b.ork.ts");
        var selection = new TargetSelectionViewModel(probe);
        selection.Select("/crews/many");
        selection.SelectedCandidate = "/crews/many/b.ork.ts";

        selection.UseCandidateCommand.Execute(null);

        Assert.True(selection.IsResolved);
        Assert.Equal("/crews/many/b.ork.ts", selection.RunPath);
    }

    [Fact]
    public void Should_ReportTheReason_When_ThePathDoesNotExist()
    {
        var selection = new TargetSelectionViewModel(new FakeTargetProbe());

        selection.Select("/nowhere");

        Assert.Equal(RunTargetCodes.PathNotFound, selection.ErrorCode);
    }

    [Fact]
    public void Should_ReportTheReason_When_TheExtensionIsNotSupported()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/notes.txt");
        var selection = new TargetSelectionViewModel(probe);

        selection.Select("/crews/notes.txt");

        Assert.Equal(RunTargetCodes.UnsupportedExtension, selection.ErrorCode);
    }
}

public sealed class LaunchOptionsViewModelTests
{
    private static RunTarget Yaml => new()
    {
        Kind = RunTargetKind.YamlFile,
        SelectedPath = "/crews/team.yaml",
        RunPath = "/crews/team.yaml",
    };

    private static RunTarget Script => new()
    {
        Kind = RunTargetKind.ScriptFile,
        SelectedPath = "/crews/team.ork.ts",
        RunPath = "/crews/team.ork.ts",
    };

    [Fact]
    public void Should_OfferVariables_When_TheTargetIsYaml()
    {
        var options = new LaunchOptionsViewModel { Target = Yaml };

        Assert.True(options.AreVariablesAvailable);
        Assert.True(options.IsInitialContextAvailable);
        Assert.False(options.AreInputsAvailable);
    }

    [Fact]
    public void Should_OfferInputs_When_TheTargetIsAScript()
    {
        var options = new LaunchOptionsViewModel { Target = Script };

        Assert.True(options.AreInputsAvailable);
        Assert.False(options.AreVariablesAvailable);
    }

    [Fact]
    public void Should_DropTheYamlOptions_When_TheTargetIsAScript()
    {
        // A stale -V left over from a YAML target must not reach a command line that would ignore it.
        var options = new LaunchOptionsViewModel { Target = Yaml };
        options.AddVariable("KEY", "value");
        options.InitialContext = "context";

        options.Target = Script;

        var built = options.ToOptions();
        Assert.Empty(built.Variables);
        Assert.Null(built.InitialContext);
    }

    [Fact]
    public void Should_PassNoSettings_When_ResolutionIsAutomatic()
    {
        var options = new LaunchOptionsViewModel { Target = Yaml, SettingsPath = "/etc/appsettings.json" };

        Assert.Null(options.EffectiveSettingsPath);
        Assert.Null(options.ToOptions().SettingsPath);
    }

    [Fact]
    public void Should_PassTheSettings_When_AnExplicitPathIsChosen()
    {
        var options = new LaunchOptionsViewModel
        {
            Target = Yaml,
            SettingsPath = "/etc/appsettings.json",
            SettingsMode = SettingsSelectionMode.ExplicitPath,
        };

        Assert.Equal("/etc/appsettings.json", options.ToOptions().SettingsPath);
    }

    [Fact]
    public void Should_OfferOnlyTheThreeVerbosityLevels()
    {
        Assert.Equal([0, 1, 2], LaunchOptionsViewModel.VerbosityChoices);
    }
}

public sealed class LaunchMountsViewModelTests
{
    private static LaunchMountsViewModel Panel(params string[] existingDirectories)
    {
        var mounts = new LaunchMountsViewModel(new FakeDirectoryProbe(existingDirectories))
        {
            // What the tab publishes once a crew is resolved: the runner's own mount for the
            // crew's directory, which occupies index 0 before any --mount is appended.
            AutoInjection = new MountAutoInjection { Mounts = ["/crews:/crews:ro"] },
        };

        return mounts;
    }

    [Fact]
    public void Should_ShowNoEffectiveMount_When_NoCrewIsSelected()
    {
        // Without a target the runner's own mounts are unknown, and so is the index every
        // --mount would occupy; showing a table anyway would be off by one or two rows.
        var mounts = new LaunchMountsViewModel(new FakeDirectoryProbe("/a"));

        mounts.SetSettingsMounts(["/a:/workspace:ro"]);

        Assert.False(mounts.HasAutoInjection);
        Assert.Empty(mounts.EffectiveMounts);
        Assert.Equal(LaunchMountsViewModel.AutoInjectionUnknownNotice, mounts.Summary);
    }

    [Fact]
    public void Should_ShowTheAutoInjectedMountMaskingTheFirstSettingsEntry()
    {
        // The runner writes its own mount at index 0, so the first appsettings entry is masked
        // whatever the user does — a launcher that hid that would be showing a mount list the
        // runtime never sees.
        var mounts = Panel("/a");

        mounts.SetSettingsMounts(["/a:/workspace:ro", "/a:/output:rw"]);

        Assert.Equal(2, mounts.EffectiveMounts.Count);
        Assert.Equal(MountOrigin.AutoInjected, mounts.EffectiveMounts[0].Origin);
        Assert.True(mounts.EffectiveMounts[0].OverridesSettings);
        Assert.Equal("/a:/workspace:ro", mounts.EffectiveMounts[0].ReplacedSettingsMount);
        Assert.Equal(MountOrigin.Settings, mounts.EffectiveMounts[1].Origin);
        Assert.Equal(1, mounts.OverriddenCount);
    }

    [Fact]
    public void Should_ReplaceBySameIndex_When_ALaunchMountIsAdded()
    {
        // The override is positional, not a merge — this is the semantic the panel has to
        // display, and the position starts after the mounts the runner injects itself.
        var mounts = Panel("/a", "/b");
        mounts.SetSettingsMounts(["/a:/workspace:ro", "/a:/output:rw", "/a:/extra:ro"]);

        var added = mounts.LaunchMounts.AddMount();
        added.PhysicalPath = "/b";

        Assert.Equal(3, mounts.EffectiveMounts.Count);
        Assert.Equal(MountOrigin.AutoInjected, mounts.EffectiveMounts[0].Origin);
        Assert.Equal(MountOrigin.CommandLine, mounts.EffectiveMounts[1].Origin);
        Assert.True(mounts.EffectiveMounts[1].OverridesSettings);
        Assert.Equal("/a:/output:rw", mounts.EffectiveMounts[1].ReplacedSettingsMount);
        Assert.Equal(MountOrigin.Settings, mounts.EffectiveMounts[2].Origin);
        Assert.Equal(2, mounts.OverriddenCount);
    }

    [Fact]
    public void Should_NameTheAutoInjectedOrigin_So_ItIsNotReadAsTheUsersOwn()
    {
        var mounts = Panel("/a");
        mounts.SetSettingsMounts([]);

        Assert.Contains("auto", mounts.EffectiveMounts[0].OriginDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_NameTheConfigurationKey_So_TheIndexRuleIsVisible()
    {
        var mounts = Panel("/a");
        mounts.SetSettingsMounts(["/a:/workspace:ro", "/a:/output:rw"]);

        Assert.Equal("Orkeon:FileSystem:Mounts:0", mounts.EffectiveMounts[0].ConfigurationKey);
        Assert.Equal("Orkeon:FileSystem:Mounts:1", mounts.EffectiveMounts[1].ConfigurationKey);
    }

    [Fact]
    public void Should_CarryASecurityWarning_For_ExternalMounts()
    {
        var panel = new LaunchMountsViewModel();

        Assert.Contains("Security", panel.ExternalMountsWarning, StringComparison.Ordinal);
        Assert.Contains(
            "PathSecurity:AdditionalAllowedDirectories",
            panel.ExternalMountsExplanation,
            StringComparison.Ordinal);
    }
}

public sealed class RunLogViewModelTests
{
    [Fact]
    public void Should_KeepTheChannel_When_ALineIsAppended()
    {
        var log = new RunLogViewModel();

        log.Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, "boom"));

        Assert.True(Assert.Single(log.Lines).IsError);
    }

    [Fact]
    public void Should_DropTheOldest_When_TheCapIsExceeded()
    {
        // The view virtualizes the rendering; the cap is what bounds the memory.
        var log = new RunLogViewModel();
        for (var i = 0; i < RunLogViewModel.MaxLines + 5; i++)
            log.AppendNotice(i.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(RunLogViewModel.MaxLines, log.Lines.Count);
        Assert.Equal(5, log.DroppedLines);
        Assert.Equal("5", log.Lines[0].Text);
    }

    [Fact]
    public void Should_Empty_When_Cleared()
    {
        var log = new RunLogViewModel();
        log.AppendNotice("one");

        log.Clear();

        Assert.Empty(log.Lines);
        Assert.Equal(0, log.DroppedLines);
    }
}

public sealed class LaunchTabViewModelTests
{
    private static (LaunchTabViewModel Tab, FakeProcessLauncher Launcher, FakeLaunchHistoryStore History) Build(
        FakeTargetProbe? targetProbe = null,
        FakeDirectoryProbe? directories = null,
        FakeAppSettingsStore? settingsStore = null)
    {
        var launcher = new FakeProcessLauncher();
        var history = new FakeLaunchHistoryStore();

        var tab = new LaunchTabViewModel(
            new OrkeonProcessRunner(launcher, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            targetProbe ?? new FakeTargetProbe(),
            directories ?? new FakeDirectoryProbe(),
            picker: null,
            history,
            settingsStore ?? new FakeAppSettingsStore());

        return (tab, launcher, history);
    }

    [Fact]
    public void Should_RefuseToRun_When_NoTargetIsResolved()
    {
        var (tab, _, _) = Build();

        Assert.False(tab.RunCommand.CanExecute(null));
    }

    [Fact]
    public void Should_PreviewTheCommandLine_When_ATargetIsResolved()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var (tab, _, _) = Build(probe);

        tab.Target.Select("/crews/team.yaml");

        Assert.NotNull(tab.CommandLinePreview);
        Assert.Contains("run", tab.CommandLinePreview!, StringComparison.Ordinal);
        Assert.Contains("/crews/team.yaml", tab.CommandLinePreview!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_RunTheCli_When_TheRunCommandIsInvoked()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var (tab, launcher, _) = Build(probe);
        tab.Target.Select("/crews/team.yaml");

        var result = await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(RunOutcome.Success, result!.Outcome);

        // BUS-06: the screen watches the run rather than tailing it, so the protocol flags
        // are part of a normal launch. Turning the option off gives the old argv back.
        Assert.Equal(
            ["run", "/crews/team.yaml", "--events", "jsonl", "--client", "studio"],
            launcher.LastRequest!.Arguments);
    }

    [Fact]
    public async Task Should_KeepThePlainArgv_When_ProgressWatchingIsTurnedOff()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var (tab, launcher, _) = Build(probe);
        tab.Target.Select("/crews/team.yaml");
        tab.Options.WatchProgress = false;

        await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["run", "/crews/team.yaml"], launcher.LastRequest!.Arguments);
    }

    [Fact]
    public async Task Should_AddTheValidateFlag_When_TheDryRunIsInvoked()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var (tab, launcher, _) = Build(probe);
        tab.Target.Select("/crews/team.yaml");

        await tab.ValidateAsync(TestContext.Current.CancellationToken);

        Assert.Contains("--validate", launcher.LastRequest!.Arguments);
    }

    [Fact]
    public async Task Should_ForwardTheOptions_When_TheyApplyToTheShape()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var (tab, launcher, _) = Build(probe, new FakeDirectoryProbe("/data"));
        tab.Target.Select("/crews/team.yaml");
        tab.Options.AddVariable("ENV", "prod");
        tab.Options.Verbosity = 2;
        tab.Options.LlmLogEnabled = true;
        var mount = tab.Mounts.LaunchMounts.AddMount();
        mount.PhysicalPath = "/data";
        tab.Mounts.AllowExternalMounts = true;

        await tab.RunAsync(TestContext.Current.CancellationToken);

        var arguments = launcher.LastRequest!.Arguments;
        Assert.Contains("-V", arguments);
        Assert.Contains("ENV=prod", arguments);
        Assert.Contains("--verbose", arguments);
        Assert.Contains("2", arguments);
        Assert.Contains("--llm-log", arguments);
        Assert.Contains("--mount", arguments);
        Assert.Contains("/data:/workspace:ro", arguments);
        Assert.Contains("--allow-external-mounts", arguments);
    }

    [Fact]
    public async Task Should_StreamTheOutput_Into_TheLogPanel()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var (tab, launcher, _) = Build(probe);
        launcher.OutputToEmit.Add(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, "starting"));
        launcher.OutputToEmit.Add(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, "a warning"));
        tab.Target.Select("/crews/team.yaml");

        await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.Contains(tab.Log.Lines, l => l.Text == "starting");
        Assert.Contains(tab.Log.Lines, l => l is { Text: "a warning", IsError: true });
    }

    [Theory]
    [InlineData(0, RunOutcome.Success)]
    [InlineData(1, RunOutcome.ScriptError)]
    [InlineData(2, RunOutcome.RuntimeError)]
    [InlineData(130, RunOutcome.Cancelled)]
    public async Task Should_InterpretTheExitCode(int exitCode, RunOutcome expected)
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var (tab, launcher, _) = Build(probe);
        launcher.ExitCode = exitCode;
        tab.Target.Select("/crews/team.yaml");

        await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, tab.Outcome);
        Assert.Equal(exitCode, tab.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(tab.ExitDescription));
    }

    [Fact]
    public async Task Should_ReportCancellation_When_TheTokenIsAlreadyCancelled()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var (tab, launcher, _) = Build(probe);
        launcher.HonourCancellation = true;
        tab.Target.Select("/crews/team.yaml");

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var result = await tab.RunAsync(cancellation.Token);

        Assert.Equal(RunOutcome.Cancelled, result!.Outcome);
        Assert.Equal(OrkeonExitCodes.Cancelled, result.ExitCode);
    }

    [Fact]
    public async Task Should_RecordTheLaunch_In_TheHistory()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var (tab, _, history) = Build(probe);
        tab.Target.Select("/crews/team.yaml");

        await tab.RunAsync(TestContext.Current.CancellationToken);

        var recorded = Assert.Single(history.Recorded);
        Assert.Equal("/crews/team.yaml", recorded.Target);
        Assert.Equal(RunOutcome.Success, recorded.Outcome);
        Assert.Equal(0, recorded.ExitCode);
    }

    [Fact]
    public async Task Should_RestoreTheSelection_When_AnEntryIsReplayed()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var (tab, _, history) = Build(probe);
        history.History = LaunchHistory.Empty.Add(LaunchHistoryEntry.Starting(
            "/crews/team.yaml",
            ["run", "/crews/team.yaml"],
            "/etc/appsettings.json"));

        await tab.History.LoadAsync(TestContext.Current.CancellationToken);
        tab.History.ReplayCommand.Execute(null);

        Assert.True(tab.Target.IsResolved);
        Assert.Equal("/crews/team.yaml", tab.Target.RunPath);
        Assert.Equal("/etc/appsettings.json", tab.Options.EffectiveSettingsPath);
    }

    [Fact]
    public void Should_StateTheCliRequirement_When_TheTargetIsAMultiFileCrew()
    {
        var probe = new FakeTargetProbe().WithDirectory("/crews/team").WithDirectory("/crews/team/agents");
        var (tab, _, _) = Build(probe);

        tab.Target.Select("/crews/team");

        Assert.Contains(
            tab.ValidationMessages,
            m => m.Code == LaunchCodes.DirectoryRunNotice && m.Severity == ValidationSeverity.Information);
        Assert.False(tab.HasBlockingErrors);
    }

    [Fact]
    public void Should_BlockTheLaunch_When_AVariableNameIsMalformed()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var (tab, _, _) = Build(probe);
        tab.Target.Select("/crews/team.yaml");

        tab.Options.AddVariable("BAD=NAME", "value");
        tab.CheckOptions();

        Assert.Contains(tab.ValidationMessages, m => m.Code == LaunchCodes.InvalidVariable && m.IsError);
        Assert.True(tab.HasBlockingErrors);
        Assert.Null(tab.BuildArguments());
    }

    [Fact]
    public async Task Should_NotSpawnAnything_When_ValidationBlocksTheLaunch()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var (tab, launcher, _) = Build(probe);
        tab.Target.Select("/crews/team.yaml");
        tab.Options.AddVariable("BAD=NAME", "value");

        var result = await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Empty(launcher.Requests);
    }

    [Fact]
    public void Should_ReportTheError_When_TheTargetItselfIsInvalid()
    {
        var (tab, _, _) = Build();

        tab.Target.Select("/nowhere");

        Assert.Contains(tab.ValidationMessages, m => m.Code == RunTargetCodes.PathNotFound);
    }

    [Fact]
    public async Task Should_ShowTheSettingsMounts_When_AnAppsettingsIsSelected()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var settings = new FakeAppSettingsStore();
        settings.Files["/etc/appsettings.json"] =
            """{"Orkeon":{"FileSystem":{"Mounts":["/data:/workspace:ro"]}}}""";
        var (tab, _, _) = Build(probe, settingsStore: settings);
        tab.Options.SettingsPath = "/etc/appsettings.json";
        tab.Options.SettingsMode = SettingsSelectionMode.ExplicitPath;

        await tab.RefreshSettingsMountsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("/data:/workspace:ro", Assert.Single(tab.Mounts.SettingsMounts));
    }

    [Fact]
    public async Task Should_LocateTheCli_When_TheTabInitializes()
    {
        var (tab, _, _) = Build();

        await tab.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(tab.IsBinaryAvailable);
        Assert.Contains("orkeon", tab.BinaryStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ExplainHowToFixIt_When_TheCliIsMissing()
    {
        var tab = new LaunchTabViewModel(
            new OrkeonProcessRunner(
                new FakeProcessLauncher(),
                new OrkeonBinaryLocator(new FakeExecutableProbe("/opt/orkeon"))),
            new FakeTargetProbe(),
            new FakeDirectoryProbe());

        await tab.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.False(tab.IsBinaryAvailable);
        Assert.Contains("not found", tab.BinaryStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ReportNotStarted_When_TheCliIsMissingAndARunIsAttempted()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var tab = new LaunchTabViewModel(
            new OrkeonProcessRunner(
                new FakeProcessLauncher(),
                new OrkeonBinaryLocator(new FakeExecutableProbe("/opt/orkeon"))),
            probe,
            new FakeDirectoryProbe());
        tab.Target.Select("/crews/team.yaml");

        var result = await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RunOutcome.NotStarted, result!.Outcome);
    }

    [Fact]
    public async Task Should_RunFromTheCrewDirectory_So_RelativePathsResolveLikeTheCli()
    {
        var probe = new FakeTargetProbe().WithDirectory("/crews/ts").WithFile("/crews/ts/crew.ork.ts");
        var (tab, launcher, _) = Build(probe);
        tab.Target.Select("/crews/ts");

        await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal("/crews/ts", launcher.LastRequest!.WorkingDirectory);
    }
}

public sealed class ValidationMessageViewModelTests
{
    [Fact]
    public void Should_IncludeTheCodeAndPath_In_TheDisplayLine()
    {
        var message = new Orkeon.Studio.Wpf.ViewModels.Common.ValidationMessageViewModel(
            ValidationMessage.Error("WIN-01", "no Llm section", "Llm"));

        // The severity is on the line, not only in the glyph: the terminal editor prints
        // [ERROR]/[WARN] and the three front-ends must read the same.
        Assert.Contains("ERROR", message.Display, StringComparison.Ordinal);
        Assert.Contains("WIN-01", message.Display, StringComparison.Ordinal);
        Assert.Contains("Llm", message.Display, StringComparison.Ordinal);
        Assert.True(message.IsError);
    }

    [Fact]
    public void Should_OmitThePath_When_TheMessageHasNone()
    {
        var message = new Orkeon.Studio.Wpf.ViewModels.Common.ValidationMessageViewModel(
            ValidationMessage.Warning("CODE", "text"));

        Assert.Equal("[WARN ] CODE — text", message.Display);
        Assert.False(message.IsError);
    }
}
