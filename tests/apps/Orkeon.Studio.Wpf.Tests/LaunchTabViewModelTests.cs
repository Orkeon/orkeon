using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Core.Validation;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Core.Localization;

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
            // crew's directory, appended after the settings entries before any --mount.
            AutoInjection = new MountAutoInjection { Mounts = ["/crews:/crew:ro"] },
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
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.MountsSelectCrewFirst], mounts.Summary);
    }

    [Fact]
    public void Should_ShowTheSettingsEntriesInPlace_And_TheAutoInjectedMountAfterThem()
    {
        // The runner's own mount is on a root the settings can never declare, so it is
        // appended after every settings entry and replaces none of them — a launcher that
        // showed it masking the first entry would be showing a mount list the runtime never sees.
        var mounts = Panel("/a");

        mounts.SetSettingsMounts(["/a:/workspace:ro", "/a:/output:rw"]);

        Assert.Equal(3, mounts.EffectiveMounts.Count);
        Assert.Equal(MountOrigin.Settings, mounts.EffectiveMounts[0].Origin);
        Assert.Equal(MountOrigin.Settings, mounts.EffectiveMounts[1].Origin);
        Assert.Equal(MountOrigin.AutoInjected, mounts.EffectiveMounts[2].Origin);
        Assert.False(mounts.EffectiveMounts[2].OverridesSettings);
        Assert.Equal(0, mounts.OverriddenCount);
    }

    [Fact]
    public void Should_ReplaceBySameRoot_When_ALaunchMountIsAdded()
    {
        // The override is by virtual root, not positional — this is the semantic the panel
        // has to display: a launch mount on a root the settings declare takes that entry's
        // place, at that entry's own index.
        var mounts = Panel("/a", "/b");
        mounts.SetSettingsMounts(["/a:/workspace:ro", "/a:/output:rw", "/a:/extra:ro"]);

        var added = mounts.LaunchMounts.AddMount();
        added.PhysicalPath = "/b";
        added.VirtualPath = "/output";

        Assert.Equal(4, mounts.EffectiveMounts.Count);
        Assert.Equal(MountOrigin.Settings, mounts.EffectiveMounts[0].Origin);
        Assert.Equal(MountOrigin.CommandLine, mounts.EffectiveMounts[1].Origin);
        Assert.True(mounts.EffectiveMounts[1].OverridesSettings);
        Assert.Equal("/a:/output:rw", mounts.EffectiveMounts[1].ReplacedSettingsMount);
        Assert.Equal(MountOrigin.Settings, mounts.EffectiveMounts[2].Origin);
        Assert.Equal(MountOrigin.AutoInjected, mounts.EffectiveMounts[3].Origin);
        Assert.Equal(1, mounts.OverriddenCount);
        Assert.Contains("replaces", mounts.EffectiveMounts[1].OriginDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_AppendAfterEverySettingsEntry_When_ALaunchMountNamesANewRoot()
    {
        var mounts = Panel("/a", "/b");
        mounts.SetSettingsMounts(["/a:/workspace:ro"]);

        var added = mounts.LaunchMounts.AddMount();
        added.PhysicalPath = "/b";
        added.VirtualPath = "/scratch";

        Assert.Equal(3, mounts.EffectiveMounts.Count);
        Assert.Equal(MountOrigin.Settings, mounts.EffectiveMounts[0].Origin);
        Assert.Equal(MountOrigin.AutoInjected, mounts.EffectiveMounts[1].Origin);
        Assert.Equal(MountOrigin.CommandLine, mounts.EffectiveMounts[2].Origin);
        Assert.Equal("Orkeon:FileSystem:Mounts:2", mounts.EffectiveMounts[2].ConfigurationKey);
        Assert.Equal(0, mounts.OverriddenCount);
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
    public void Should_NotColourAnInfoLine_AsAnError_When_ItCameFromStderr()
    {
        // Under --events every log of the CLI goes to stderr: the level decides, not the stream.
        var log = new RunLogViewModel();

        log.Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, "13:55:05.057 info: Orkeon.Verbose[0] Agent loop started"));

        var line = Assert.Single(log.Lines);
        Assert.False(line.IsError);
        Assert.False(line.IsWarning);
    }

    [Fact]
    public void Should_ColourByLevel_When_TheLineIsACliLogLine()
    {
        var log = new RunLogViewModel();

        log.Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, "13:55:05.057 warn: Orkeon.Hosting[3] No default"));
        log.Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, "13:55:05.058 fail: Orkeon.Tools[1] Error calling tool x"));
        log.Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, "13:55:05.059 crit: Orkeon.Host[2] Down"));

        Assert.True(log.Lines[0].IsWarning);
        Assert.False(log.Lines[0].IsError);
        Assert.True(log.Lines[1].IsError);
        Assert.True(log.Lines[2].IsError);
    }

    [Fact]
    public void Should_ColourAContinuationLine_AsTheLogEntryItFollows()
    {
        // A multi-line message and the « === Crew Output === » block reach stderr without a level.
        var log = new RunLogViewModel();

        log.Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, "13:55:16.532 info: Orkeon.Verbose[0] LLM response for [x]:"));
        log.Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, "# /log/journal.md"));
        log.Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, "=== Crew Output ==="));
        log.Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, "13:55:16.600 fail: Orkeon.Tools[1] Error calling tool x"));
        log.Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, "   at Somewhere()"));

        Assert.False(log.Lines[1].IsError);
        Assert.False(log.Lines[2].IsError);
        Assert.Equal(CliLogLevel.Information, log.Lines[2].Level);
        Assert.True(log.Lines[3].IsError);
        Assert.True(log.Lines[4].IsError);
    }

    [Fact]
    public void Should_StartAfresh_When_ANewCommandIsEchoed()
    {
        var log = new RunLogViewModel();

        log.Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, "13:55:16.532 info: Orkeon.Hosting[0] ready"));
        log.AppendCommand("orkeon run ./crew");
        log.Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, "Unhandled exception. boom"));

        Assert.True(log.Lines[2].IsError);
    }

    [Fact]
    public void Should_KeepStudiosOwnErrors_Red_After_AnInfoLine()
    {
        var log = new RunLogViewModel();

        log.Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, "13:55:16.532 info: Orkeon.Hosting[0] ready"));
        log.AppendError("The run could not start.");

        Assert.True(log.Lines[1].IsError);
    }

    [Fact]
    public void Should_ShowOneTimestamp_When_TheCliLoggerStampedTheLine()
    {
        var log = new RunLogViewModel();

        log.Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, "13:55:05.057 info: Orkeon.Verbose[0] Agent loop started"));

        var line = Assert.Single(log.Lines);
        Assert.Equal("info: Orkeon.Verbose[0] Agent loop started", line.Text);
        Assert.Equal($"{line.Time}  info: Orkeon.Verbose[0] Agent loop started", log.BuildText());
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

    [Fact]
    public void Should_StampEachLine_WithTheTimeItWasRead()
    {
        // STUDIO-17: captured since the first version, shown at last. Local time, to the second.
        var log = new RunLogViewModel();
        var read = new DateTimeOffset(2026, 9, 19, 8, 32, 19, TimeSpan.Zero);

        log.Append(new ProcessOutputLine(ProcessOutputChannel.StandardOutput, "Using settings: x", read));

        var line = Assert.Single(log.Lines);
        Assert.Equal(read.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), line.Time);
        Assert.Matches("^[0-9]{2}:[0-9]{2}:[0-9]{2}$", line.Time);
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

        var tab = new LaunchTabViewModel(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(
                launcher, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            TargetProbe = targetProbe ?? new FakeTargetProbe(),
            Directories = directories ?? new FakeDirectoryProbe(),
            HistoryStore = history,
            SettingsStore = settingsStore ?? new FakeAppSettingsStore(),
        });

        return (tab, launcher, history);
    }

    /// <summary>
    /// The owner's screenshot of 2026-09-20: the COMMANDE well was readable and not selectable,
    /// so reproducing a Studio launch in a terminal meant retyping a ULID. The button puts the
    /// exact preview on the clipboard and says so until the command changes.
    /// </summary>
    [Fact]
    public void Should_CopyTheExactCommand_When_TheCopyButtonIsClicked()
    {
        var clipboard = new Orkeon.Studio.Wpf.ViewModels.Services.InMemoryClipboardService();
        var tab = new LaunchTabViewModel(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(
                new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            TargetProbe = new FakeTargetProbe().WithFile("/crews/team.yaml"),
            Directories = new FakeDirectoryProbe(),
            HistoryStore = new FakeLaunchHistoryStore(),
            SettingsStore = new FakeAppSettingsStore(),
            Clipboard = clipboard,
        });
        Assert.False(tab.CopyCommandLineCommand.CanExecute(null));

        tab.Target.Select("/crews/team.yaml");
        Assert.True(tab.CopyCommandLineCommand.CanExecute(null));

        tab.CopyCommandLineCommand.Execute(null);

        Assert.Equal(tab.CommandLinePreview, clipboard.LastText);
        Assert.Contains("orkeon run", clipboard.LastText, StringComparison.Ordinal);
        Assert.True(tab.CommandLineCopied);

        // A new command is not the one that was copied.
        tab.Options.Verbosity = 2;
        Assert.False(tab.CommandLineCopied);
        Assert.NotEqual(tab.CommandLinePreview, clipboard.LastText);
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
            ["run", "/crews/team.yaml", "--events", "jsonl", "--client=studio"],
            launcher.LastRequest!.Arguments);
    }

    [Fact]
    public async Task Should_LayTheTeamsProfileOverTheEnvironment_When_TheResolverNamesOne()
    {
        var probe = new FakeTargetProbe().WithFile("/teams/veille/crew.yaml");
        var launcher = new FakeProcessLauncher();
        var tab = new LaunchTabViewModel(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(
                launcher, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            TargetProbe = probe,
            Directories = new FakeDirectoryProbe(),
            SettingsStore = new FakeAppSettingsStore(),
            EnvironmentForTarget = (_, _, _) => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ORKEON_Llm__Model"] = "qwen2.5:32b",
            },
        });
        tab.Target.Select("/teams/veille/crew.yaml");

        await tab.RunAsync(TestContext.Current.CancellationToken);

        // The sidecar's profile rides the launch as ORKEON_Llm__*, never inside a file.
        Assert.Equal("qwen2.5:32b", launcher.LastRequest!.Environment["ORKEON_Llm__Model"]);
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
        mount.VirtualPath = "/docs";
        tab.Mounts.AllowExternalMounts = true;

        await tab.RunAsync(TestContext.Current.CancellationToken);

        var arguments = launcher.LastRequest!.Arguments;
        Assert.Contains("-V", arguments);
        Assert.Contains("ENV=prod", arguments);
        Assert.Contains("--verbose", arguments);
        Assert.Contains("2", arguments);
        Assert.Contains("--llm-log", arguments);
        Assert.Contains("--mount", arguments);
        Assert.Contains("/data:/docs:ro", arguments);
        Assert.Contains("--allow-external-mounts", arguments);
    }

    [Fact]
    public async Task A_reply_to_an_agent_goes_down_stdin_in_the_bridge_wire_form()
    {
        // The client://studio seat, taken: an agent's send is answered from the screen, and
        // the line written is byte-for-byte what the bridge's ReplyFromPeer requires. Text
        // that parses as JSON travels as that JSON; anything else as a plain JSON string.
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var (tab, launcher, _) = Build(probe);
        tab.Target.Select("/crews/team.yaml");

        launcher.WhileRunning = () =>
        {
            tab.Progress.TryApply("""{"v":2,"seq":1,"ts":"t","kind":"hub.message","correlationId":"r-1","expectsReply":true,"payload":{"question":"shape?"}}""");
            tab.Progress.ReplyText = """{"answer":42}""";
            tab.Progress.ReplyCommand.Execute(null);

            tab.Progress.TryApply("""{"v":2,"seq":2,"ts":"t","kind":"hub.message","correlationId":"r-2","expectsReply":true,"payload":{"question":"words?"}}""");
            tab.Progress.ReplyText = "yes, go";
            tab.Progress.ReplyCommand.Execute(null);
        };

        await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.Contains("""{"kind":"reply","correlationId":"r-1","payload":{"answer":42}}""", launcher.InputLines);
        Assert.Contains("""{"kind":"reply","correlationId":"r-2","payload":"yes, go"}""", launcher.InputLines);
    }

    [Fact]
    public async Task A_second_launch_starts_from_a_clean_journal_and_no_stale_verdict()
    {
        // STUDIO-17: the journal used to accumulate across runs, and the previous run's verdict
        // — exit badge, result row, « Open the result » — stayed on screen while the new run
        // was in flight. Every launch now begins clean; « Copy » is how a journal survives.
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var (tab, launcher, _) = Build(probe);
        launcher.OutputToEmit.Add(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, "first run"));
        tab.Target.Select("/crews/team.yaml");

        await tab.RunAsync(TestContext.Current.CancellationToken);
        Assert.True(tab.HasResult);
        Assert.Contains(tab.Log.Lines, l => l.Text == "first run");

        launcher.OutputToEmit.Clear();
        launcher.OutputToEmit.Add(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, "second run"));
        bool? hadResultWhileRunning = null;
        int? linesWhileRunning = null;
        launcher.WhileRunning = () =>
        {
            hadResultWhileRunning = tab.HasResult;
            linesWhileRunning = tab.Log.Lines.Count;
        };

        await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.False(hadResultWhileRunning);
        Assert.Equal(1, linesWhileRunning);   // the echoed command line, nothing older
        Assert.DoesNotContain(tab.Log.Lines, l => l.Text == "first run");
        Assert.Contains(tab.Log.Lines, l => l.Text == "second run");
        Assert.True(tab.HasResult);
    }

    [Fact]
    public async Task A_validate_first_launch_keeps_both_passes_in_one_journal()
    {
        // The clean start is per click, not per pass: the dry run's verdict must still be
        // readable next to the real run it protected.
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var (tab, launcher, _) = Build(probe);
        tab.Target.Select("/crews/team.yaml");
        tab.Options.ValidateFirst = true;

        await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, launcher.Requests.Count);
        Assert.Equal(2, tab.Log.Lines.Count(l => l.IsCommand));
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
    public async Task The_outcome_sentence_is_not_repeated_on_the_status_line()
    {
        // #117: the verdict line shows the badge and the exit code; the status line keeps
        // whatever else the tab has to say.
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var (tab, _, _) = Build(probe);
        tab.Target.Select("/crews/team.yaml");

        await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.True(tab.HasResult);
        Assert.False(string.IsNullOrWhiteSpace(tab.StatusMessage));
        Assert.False(tab.ShowsStatusMessage);
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
    public void Should_SayWhatTheRootCarried_When_ACrewSubFolderWinsOverIt()
    {
        // STUDIO-59: a workshop team keeps its crew under crew/ and a mount point named
        // agents/ at its root; the crew runs, and the list says the root folder was not it.
        var probe = new FakeTargetProbe()
            .WithDirectory("/teams/t").WithDirectory("/teams/t/agents")
            .WithDirectory("/teams/t/crew").WithDirectory("/teams/t/crew/agents")
            .WithFile("/teams/t/crew/config.yaml");
        var (tab, _, _) = Build(probe);

        tab.Target.Select("/teams/t");

        Assert.Equal("/teams/t/crew", tab.Target.RunPath!.Replace('\\', '/'));
        var notice = Assert.Single(
            tab.ValidationMessages,
            m => m.Code == RunTargetCodes.RootShadowedByPromotedCrew);
        Assert.Equal(ValidationSeverity.Information, notice.Severity);
        Assert.Contains("/teams/t/agents", notice.Text.Replace('\\', '/'), StringComparison.Ordinal);
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
        Assert.Empty(tab.BuildArguments());
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
        var tab = new LaunchTabViewModel(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(
                new FakeProcessLauncher(),
                new OrkeonBinaryLocator(new FakeExecutableProbe("/opt/orkeon"))),
            TargetProbe = new FakeTargetProbe(),
            Directories = new FakeDirectoryProbe(),
        });

        await tab.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.False(tab.IsBinaryAvailable);
        Assert.Contains("was not located on this machine", tab.BinaryStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_ReportNotStarted_When_TheCliIsMissingAndARunIsAttempted()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var tab = new LaunchTabViewModel(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(
                new FakeProcessLauncher(),
                new OrkeonBinaryLocator(new FakeExecutableProbe("/opt/orkeon"))),
            TargetProbe = probe,
            Directories = new FakeDirectoryProbe(),
        });
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

    // ---- STUDIO-60: the team's own folders are prepared before the launch -------------

    private static (string Root, string Team) TeamOnDisk(string name, params string[] mounts)
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-studio-60-{Guid.NewGuid():N}");
        var team = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(team, "agents"));
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata { Name = name, Mounts = mounts });
        return (root, team);
    }

    [Fact]
    [Trait("BR", "CH-13")]
    public void ShouldReadTheCoreRule_WhenTheLaunchTabDecidesTheFlag()
    {
        string[] settings = ["docs|/srv/docs:/workspace:ro"];
        var cases = new (string[] Mounts, bool Outside)[]
        {
            (["./output:/output:rw", "./../x:/x:rw"], true),
            (["./output:/output:rw", "/srv/elsewhere:/e:rw"], true),
            (["./output:/output:rw", "./input:/workspace:ro"], false),
            (["docs"], false),
        };

        foreach (var (mounts, outside) in cases)
        {
            var (root, team) = TeamOnDisk("veille", mounts);
            try
            {
                var tab = new LaunchTabViewModel(new LaunchTabDependencies
                {
                    ProcessRunner = new OrkeonProcessRunner(
                        new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                    TargetProbe = TeamProbe(team),
                    Directories = new FakeDirectoryProbe(root, team),
                    DeclaredMounts = () => settings,
                });

                tab.Target.Select(team);

                var described = TeamCatalog.DescribeTarget(team, settings);
                Assert.Equal(Orkeon.Studio.Core.FileSystem.DeclaredMounts.ReachesOutsideTheTeam(described.Mounts, team), tab.Mounts.AllowExternalMounts);
                Assert.Equal(outside, tab.Mounts.AllowExternalMounts);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static FakeTargetProbe TeamProbe(string team) =>
        new FakeTargetProbe().WithDirectory(team).WithDirectory(Path.Combine(team, "agents"));

    [Fact]
    public async Task Should_CreateAMissingWritableFolder_BeforeTheProcessStarts_When_TheCardDeclaresIt()
    {
        var (root, team) = TeamOnDisk("veille", "./output:/output:rw");
        try
        {
            // The probe knows the team folder only: the sidecar's write created output/ on the
            // real disk, but the launch reasons on the probe, which says it is missing.
            var directories = new FakeDirectoryProbe(team);
            var (tab, launcher, _) = Build(TeamProbe(team), directories);
            tab.Target.Select(team);
            var output = Path.Combine(team, "output");
            List<string>? createdWhenStarted = null;
            launcher.WhileRunning = () => createdWhenStarted = [.. directories.Created];

            await tab.RunAsync(TestContext.Current.CancellationToken);

            Assert.Single(launcher.Requests);
            Assert.Equal([output], createdWhenStarted);
            Assert.Contains(tab.Log.Lines, line => line.Text == $"Created in the team folder: {output}");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Should_RefuseTheLaunch_When_AReadOnlyFolderOfTheTeamIsMissing()
    {
        var (root, team) = TeamOnDisk("veille", "./output:/output:rw", "./input:/workspace:ro");
        try
        {
            var directories = new FakeDirectoryProbe(team, Path.Combine(team, "output"));
            var (tab, launcher, _) = Build(TeamProbe(team), directories);
            tab.Target.Select(team);

            var result = await tab.RunAsync(TestContext.Current.CancellationToken);

            Assert.Null(result);
            Assert.Empty(launcher.Requests);
            Assert.Empty(directories.Created);
            Assert.Equal(
                $"Nothing to read: the team's folder '{Path.Combine(team, "input")}' (mount point /workspace) does not exist. Create it and put the inputs there.",
                tab.StatusMessage);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Should_RefuseTheLaunch_When_TheDiskRefusesToCreateTheFolder()
    {
        var (root, team) = TeamOnDisk("veille", "./output:/output:rw");
        try
        {
            var directories = new FakeDirectoryProbe(team)
            {
                CreateFault = _ => new UnauthorizedAccessException("Read-only media."),
            };
            var (tab, launcher, _) = Build(TeamProbe(team), directories);
            tab.Target.Select(team);

            var result = await tab.RunAsync(TestContext.Current.CancellationToken);

            Assert.Null(result);
            Assert.Empty(launcher.Requests);
            Assert.Equal(
                $"The team's folder '{Path.Combine(team, "output")}' could not be created: Read-only media.",
                tab.StatusMessage);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Should_LeaveTheFoldersAlone_When_TheyAlreadyExist()
    {
        var (root, team) = TeamOnDisk("veille", "./output:/output:rw", "./input:/workspace:ro");
        try
        {
            var directories = new FakeDirectoryProbe(team, Path.Combine(team, "output"), Path.Combine(team, "input"));
            var (tab, launcher, _) = Build(TeamProbe(team), directories);
            tab.Target.Select(team);

            await tab.RunAsync(TestContext.Current.CancellationToken);

            Assert.Single(launcher.Requests);
            Assert.Empty(directories.Created);
            Assert.DoesNotContain(tab.Log.Lines, line => line.Text.StartsWith("Created in the team folder", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Should_PrepareTheFoldersTheSameWay_When_ALaunchIsReplayed()
    {
        var (root, team) = TeamOnDisk("veille", "./output:/output:rw", "./input:/workspace:ro");
        try
        {
            var directories = new FakeDirectoryProbe(team);
            var (tab, launcher, _) = Build(TeamProbe(team), directories);
            var entry = LaunchHistoryEntry.Starting(team, ["run", team], null, team);

            var refused = await tab.ReplayAsync(entry, TestContext.Current.CancellationToken);

            // The read-only folder is missing: refused before any process, the writable one created.
            Assert.Null(refused);
            Assert.Empty(launcher.Requests);
            Assert.Equal([Path.Combine(team, "output")], directories.Created);
            Assert.Contains("Nothing to read", tab.StatusMessage!, StringComparison.Ordinal);

            directories.Directories.Add(Path.Combine(team, "input"));

            await tab.ReplayAsync(entry, TestContext.Current.CancellationToken);

            // The argv is replayed as recorded; the folders were prepared first.
            var request = Assert.Single(launcher.Requests);
            Assert.Equal(["run", team], request.Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Should_NameTheFoldersCreated_When_AReplayGoesOn()
    {
        var (root, team) = TeamOnDisk("veille", "./output:/output:rw", "./input:/workspace:ro");
        try
        {
            // The input exists and the output does not: a replay creates it and carries on — and its
            // journal, begun before the preparation, still says so once the process has started.
            var directories = new FakeDirectoryProbe(team, Path.Combine(team, "input"));
            var (tab, launcher, _) = Build(TeamProbe(team), directories);
            var entry = LaunchHistoryEntry.Starting(team, ["run", team], null, team);

            await tab.ReplayAsync(entry, TestContext.Current.CancellationToken);

            Assert.Single(launcher.Requests);
            Assert.Contains(tab.Log.Lines, line => line.Text == $"Created in the team folder: {Path.Combine(team, "output")}");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ---- STUDIO-62: a workshop team runs on settings/<slug>/appsettings.json unless a file is pinned ----

    private const string WorkshopTeamsRoot = "/ws/teams";

    private const string VeilleCrew = "/ws/teams/veille/crew.yaml";

    private static readonly string VeilleSettingsFile = Orkeon.Studio.Core.Teams.WorkshopLayout.SettingsFileOf(WorkshopTeamsRoot, "veille");

    /// <summary>
    /// A tab over a workshop: the teams root, a team folder right under it, and the workshop's
    /// settings file for the team declared in the settings store — the seam the file is looked up
    /// through. Nothing on the disk.
    /// </summary>
    private static (LaunchTabViewModel Tab, FakeProcessLauncher Launcher, FakeLaunchHistoryStore History, FakeAppSettingsStore Settings) BuildOverWorkshop(
        string? teamsRoot = WorkshopTeamsRoot,
        string settingsJson = "{}",
        Func<IReadOnlyList<string>, string?, Task<string?>>? prepareLaunch = null)
    {
        var launcher = new FakeProcessLauncher();
        var history = new FakeLaunchHistoryStore();
        var settings = new FakeAppSettingsStore();
        settings.Files[VeilleSettingsFile] = settingsJson;

        var tab = new LaunchTabViewModel(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(
                launcher, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            TargetProbe = new FakeTargetProbe().WithDirectory(WorkshopTeamsRoot).WithDirectory("/ws/teams/veille").WithFile(VeilleCrew),
            Directories = new FakeDirectoryProbe(WorkshopTeamsRoot, "/ws/teams/veille"),
            HistoryStore = history,
            SettingsStore = settings,
            TeamsRoot = teamsRoot,
            PrepareLaunch = prepareLaunch,
        });

        return (tab, launcher, history, settings);
    }

    [Fact]
    public void Should_PassTheTeamSettingsFile_When_NothingIsPinned()
    {
        var (tab, _, _, _) = BuildOverWorkshop();

        tab.Target.Select(VeilleCrew);

        Assert.Equal(VeilleSettingsFile, tab.TeamSettingsPath);
        Assert.Equal(VeilleSettingsFile, tab.ResolvedSettingsPath);
        Assert.Null(tab.Options.EffectiveSettingsPath);
        Assert.Contains("--settings=" + VeilleSettingsFile, tab.BuildArguments());
        Assert.Contains("--settings=", tab.CommandLinePreview!, StringComparison.Ordinal);
        Assert.Contains(VeilleSettingsFile, tab.CommandLinePreview!, StringComparison.Ordinal);
        Assert.NotNull(tab.TeamSettingsLine);
        Assert.StartsWith("Team settings file: " + VeilleSettingsFile, tab.TeamSettingsLine, StringComparison.Ordinal);
        // No card names a setting: the precedence note is not there.
        Assert.DoesNotContain("model setting", tab.TeamSettingsLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_PreferThePin_When_AFileIsPinnedInExpertMode()
    {
        var (tab, _, _, _) = BuildOverWorkshop();
        tab.Target.Select(VeilleCrew);

        tab.Options.SettingsPath = "/etc/orkeon/appsettings.json";
        tab.Options.SettingsMode = SettingsSelectionMode.ExplicitPath;

        Assert.Equal("/etc/orkeon/appsettings.json", tab.ResolvedSettingsPath);
        Assert.Contains("--settings=/etc/orkeon/appsettings.json", tab.BuildArguments());
        Assert.DoesNotContain(tab.BuildArguments(), a => a.Contains(VeilleSettingsFile, StringComparison.Ordinal));
        // The team file is still known, but the line that says the run reads it is gone: it would
        // name a file this run does not read.
        Assert.Equal(VeilleSettingsFile, tab.TeamSettingsPath);
        Assert.Null(tab.TeamSettingsLine);

        // Back to automatic: the team file applies again, and the line with it.
        tab.Options.SettingsMode = SettingsSelectionMode.Automatic;
        Assert.Equal(VeilleSettingsFile, tab.ResolvedSettingsPath);
        Assert.NotNull(tab.TeamSettingsLine);
    }

    [Fact]
    public void Should_ReadABlankPinAsNoPin_When_ExpertModeIsOnWithoutAPath()
    {
        var (tab, _, _, _) = BuildOverWorkshop();
        tab.Target.Select(VeilleCrew);

        // Explicit mode, the path typed then cleared: nothing is pinned, as the Core model reads it.
        tab.Options.SettingsPath = "";
        tab.Options.SettingsMode = SettingsSelectionMode.ExplicitPath;

        Assert.Equal(VeilleSettingsFile, tab.ResolvedSettingsPath);
        Assert.Contains("--settings=" + VeilleSettingsFile, tab.BuildArguments());
        Assert.NotNull(tab.TeamSettingsLine);
    }

    [Fact]
    public async Task Should_RecordTheTeamFileInTheHistoryEntry_When_TheRunReadsIt()
    {
        var (tab, launcher, history, _) = BuildOverWorkshop();
        tab.Target.Select(VeilleCrew);

        await tab.RunAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(launcher.Requests);
        Assert.Contains("--settings=" + VeilleSettingsFile, request.Arguments);
        var recorded = Assert.Single(history.Recorded);
        Assert.Equal(VeilleSettingsFile, recorded.SettingsPath);
    }

    [Fact]
    public async Task Should_StayAutomatic_When_AnEntryRecordedOnTheTeamFileIsReplayed()
    {
        var (tab, launcher, history, _) = BuildOverWorkshop();
        var entry = LaunchHistoryEntry.Starting(VeilleCrew, ["run", VeilleCrew, "--settings=" + VeilleSettingsFile], VeilleSettingsFile);
        history.History = LaunchHistory.Empty.Add(entry);
        await tab.History.LoadAsync(TestContext.Current.CancellationToken);

        await tab.ReplayAsync(entry, TestContext.Current.CancellationToken);

        Assert.True(tab.Options.IsSettingsAutomatic);
        Assert.Null(tab.Options.EffectiveSettingsPath);
        Assert.Equal(VeilleSettingsFile, tab.ResolvedSettingsPath);
        Assert.Equal(entry.Arguments, Assert.Single(launcher.Requests).Arguments);

        // A pin recorded as a pin still loads as one.
        var pinned = LaunchHistoryEntry.Starting(VeilleCrew, ["run", VeilleCrew, "--settings=/etc/orkeon/appsettings.json"], "/etc/orkeon/appsettings.json");
        await tab.ReplayAsync(pinned, TestContext.Current.CancellationToken);
        Assert.True(tab.Options.IsSettingsExplicit);
        Assert.Equal("/etc/orkeon/appsettings.json", tab.Options.EffectiveSettingsPath);

        // And an entry recorded on the team's file lets go of the pin the form was holding: the form
        // shows what the replayed command line reads.
        await tab.ReplayAsync(entry, TestContext.Current.CancellationToken);
        Assert.True(tab.Options.IsSettingsAutomatic);
        Assert.Equal(VeilleSettingsFile, tab.ResolvedSettingsPath);
    }

    [Fact]
    public async Task Should_ReadTheSettingsMountsFromTheTeamFile_When_ItApplies()
    {
        var (tab, _, _, _) = BuildOverWorkshop(
            settingsJson: """{"Orkeon":{"FileSystem":{"Mounts":["/ws/mounts.docs/veille:/docs:ro"]}}}""");
        tab.Target.Select(VeilleCrew);

        await tab.RefreshSettingsMountsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("/ws/mounts.docs/veille:/docs:ro", Assert.Single(tab.Mounts.SettingsMounts));
    }

    [Fact]
    public void Should_HaveNoTeamSettingsLine_When_OutsideAWorkshop()
    {
        // No teams root: the Test screen's launcher, or a plain catalogue.
        var (rootless, _, _, _) = BuildOverWorkshop(teamsRoot: null);
        rootless.Target.Select(VeilleCrew);
        Assert.Null(rootless.TeamSettingsPath);
        Assert.Null(rootless.TeamSettingsLine);
        Assert.DoesNotContain(rootless.BuildArguments(), a => a.StartsWith("--settings", StringComparison.Ordinal));

        // A team under the root whose workshop keeps no file for it.
        var (tab, _, _, settings) = BuildOverWorkshop();
        settings.Files.Remove(VeilleSettingsFile);
        tab.Target.Select(VeilleCrew);
        Assert.Null(tab.TeamSettingsPath);
        Assert.Null(tab.TeamSettingsLine);
        Assert.DoesNotContain(tab.BuildArguments(), a => a.StartsWith("--settings", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Should_HandTheTeamFileToThePreparation_So_NothingIsSavedIntoTheMachinesFile()
    {
        // A launch that reads another file than the machine's saves nothing into the machine's
        // (STUDIO-52, decision 4): the preparation receives the team file as it receives a pin.
        var declared = Orkeon.Domain.Common.MountId.Create();
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-studio-62-{Guid.NewGuid():N}");
        var teamsRoot = Path.Combine(root, "teams");
        var team = Path.Combine(teamsRoot, "veille");
        var settingsFile = Orkeon.Studio.Core.Teams.WorkshopLayout.SettingsFileOf(teamsRoot, "veille");
        try
        {
            Directory.CreateDirectory(Path.Combine(team, "agents"));
            TeamCatalog.SaveMetadata(team, new StudioTeamMetadata { Name = "veille", Mounts = [$"{declared}|/srv/docs:/workspace:ro"] });
            string? handed = "unset";
            var settings = new FakeAppSettingsStore();
            settings.Files[settingsFile] = "{}";
            var tab = new LaunchTabViewModel(new LaunchTabDependencies
            {
                ProcessRunner = new OrkeonProcessRunner(
                    new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                TargetProbe = TeamProbe(team),
                Directories = new FakeDirectoryProbe(teamsRoot, team),
                SettingsStore = settings,
                TeamsRoot = teamsRoot,
                DeclaredMounts = () => [$"{declared}|/srv/docs:/workspace:ro"],
                PrepareLaunch = (_, settingsPath) =>
                {
                    handed = settingsPath;
                    return Task.FromResult<string?>(null);
                },
            });
            tab.Target.Select(team);
            Assert.Equal(settingsFile, tab.TeamSettingsPath);

            await tab.RunAsync(TestContext.Current.CancellationToken);

            Assert.Equal(settingsFile, handed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Should_SayTheCardsSettingOverridesTheFile_When_TheCardNamesAKnownSetting()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-studio-62-{Guid.NewGuid():N}");
        var teamsRoot = Path.Combine(root, "teams");
        var team = Path.Combine(teamsRoot, "veille");
        var settingsFile = Orkeon.Studio.Core.Teams.WorkshopLayout.SettingsFileOf(teamsRoot, "veille");
        try
        {
            Directory.CreateDirectory(Path.Combine(team, "agents"));
            TeamCatalog.SaveMetadata(team, new StudioTeamMetadata { Name = "veille", Profile = "Z.AI" });
            var settings = new FakeAppSettingsStore();
            settings.Files[settingsFile] = "{}";
            var known = Orkeon.Studio.Core.Profiles.ModelProfileSet.Empty.Upsert(
                new Orkeon.Studio.Core.Profiles.ModelProfile { Name = "Z.AI", Provider = "ZAI", Model = "glm-4.6" });

            LaunchTabViewModel BuildWith(Func<Orkeon.Studio.Core.Profiles.ModelProfileSet>? modelSettings) => new(new LaunchTabDependencies
            {
                ProcessRunner = new OrkeonProcessRunner(
                    new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                TargetProbe = TeamProbe(team),
                Directories = new FakeDirectoryProbe(teamsRoot, team),
                SettingsStore = settings,
                TeamsRoot = teamsRoot,
                ModelSettings = modelSettings,
            });

            // The setting is one of this machine: the precedence « card's setting > team file » is said.
            var tab = BuildWith(() => known);
            tab.Target.Select(team);
            Assert.EndsWith("The card's model setting overrides its Llm section.", tab.TeamSettingsLine, StringComparison.Ordinal);

            // STUDIO-73: this run follows the default — the file's Llm section decides, nothing overrides it.
            tab.RunUsesDefaultLlm = true;
            Assert.DoesNotContain("overrides", tab.TeamSettingsLine, StringComparison.Ordinal);
            Assert.Equal("This run: Default of the team's settings file", tab.RunLlmLine);

            // The setting is absent from this machine: the default runs, nothing overrides the file.
            var missing = BuildWith(() => Orkeon.Studio.Core.Profiles.ModelProfileSet.Empty);
            missing.Target.Select(team);
            Assert.NotNull(missing.TeamSettingsLine);
            Assert.DoesNotContain("overrides", missing.TeamSettingsLine, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ---- STUDIO-73: the model a run takes — the team's, changed here for this run, remembered on demand ----

    private static readonly Orkeon.Studio.Core.Profiles.ModelProfile Glm =
        new() { Name = "GLM", Provider = "zai", Model = "glm-5.2" };

    private static readonly Orkeon.Studio.Core.Profiles.ModelProfile Qwen =
        new() { Name = "Qwen", Provider = "ollama", Model = "qwen3" };

    /// <summary>GLM elected default (the first upserted), Qwen beside it.</summary>
    private static readonly Orkeon.Studio.Core.Profiles.ModelProfileSet TwoSettings =
        Orkeon.Studio.Core.Profiles.ModelProfileSet.Empty.Upsert(Glm).Upsert(Qwen);

    /// <summary>What the environment seam was asked: the target, the run's model and the settings file.</summary>
    private sealed record EnvironmentAsk(string Target, TeamLlmChoice? Choice, string? SettingsPath);

    private static (LaunchTabViewModel Tab, List<EnvironmentAsk> Asks, FakeLaunchHistoryStore History) BuildForModel(
        string team,
        Orkeon.Studio.Core.Profiles.ModelProfileSet? settings = null,
        Func<string, TeamLlmChoice, string?>? remember = null)
    {
        var asks = new List<EnvironmentAsk>();
        var history = new FakeLaunchHistoryStore();
        var tab = new LaunchTabViewModel(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(
                new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            TargetProbe = TeamProbe(team),
            Directories = new FakeDirectoryProbe(team),
            SettingsStore = new FakeAppSettingsStore(),
            HistoryStore = history,
            ModelSettings = () => settings ?? TwoSettings,
            RememberLlmChoice = remember,
            EnvironmentForTarget = (target, choice, settingsPath) =>
            {
                asks.Add(new EnvironmentAsk(target, choice, settingsPath));
                return new Dictionary<string, string>(StringComparer.Ordinal);
            },
        });
        return (tab, asks, history);
    }

    private static (string Root, string Team) ModelTeamOnDisk(string? profile, bool? useDefault)
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-studio-73-{Guid.NewGuid():N}");
        var team = Path.Combine(root, "veille");
        Directory.CreateDirectory(Path.Combine(team, "agents"));
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata { Name = "veille", Profile = profile, UseDefaultLlm = useDefault });
        return (root, team);
    }

    /// <summary>
    /// A pick in the « Model » list keeps the list itself: a list rebuilt and raised on each pick
    /// was a new items source for the combo box, which wrote its selection back, which raised the
    /// list again until the stack overflowed.
    /// </summary>
    [Fact]
    public void Should_KeepTheModelList_When_AModelIsPicked()
    {
        var (root, pinned) = ModelTeamOnDisk("Qwen", useDefault: null);
        try
        {
            var (tab, _, _) = BuildForModel(pinned);
            tab.Target.Select(pinned);
            var list = tab.ModelChoices;
            var raised = new List<string?>();
            tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            tab.SelectedModelChoice = list.First(choice => choice.Profile == "GLM");

            Assert.Same(list, tab.ModelChoices);
            Assert.DoesNotContain(nameof(LaunchTabViewModel.ModelChoices), raised);
            Assert.Equal("GLM", tab.RunLlmProfile);

            raised.Clear();
            tab.SelectedModelChoice = tab.SelectedModelChoice;
            Assert.Empty(raised);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The combo of the team tab selects by key (SelectedValuePath) over one list instance the
    /// view model changes in place: the key follows the choice, picks it back, ignores an unknown
    /// or empty one, and the list is never replaced under the combo when the team resolves.
    /// </summary>
    [Fact]
    public void Should_FollowAndPickTheModelByKey_When_TheComboSelectsByValue()
    {
        var (root, pinned) = ModelTeamOnDisk("Qwen", useDefault: null);
        try
        {
            var (tab, _, _) = BuildForModel(pinned);
            var list = tab.ModelChoices;
            Assert.Equal("default", tab.SelectedModelKey);
            Assert.DoesNotContain(list, choice => choice.Kind == RunModelChoiceKind.Team);

            var raised = new List<string?>();
            tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            var counts = new List<int>();
            var selectedKeys = new List<string?>();
            ((System.Collections.Specialized.INotifyCollectionChanged)list).CollectionChanged += (_, _) =>
            {
                counts.Add(list.Count);
                selectedKeys.Add(tab.SelectedModelKey);
            };
            tab.Target.Select(pinned);

            // The team resolved: the same list gained the team's entry in second place, the
            // selection moved onto it, and the combo was told of the key, never of a new list.
            // The list never emptied on the way, and the selection could be read at every step:
            // the combo does both while the list moves.
            Assert.Same(list, tab.ModelChoices);
            Assert.DoesNotContain(0, counts);
            Assert.All(selectedKeys, key => Assert.NotNull(key));
            Assert.Equal(RunModelChoiceKind.Team, list[1].Kind);
            Assert.Equal("team", tab.SelectedModelKey);
            Assert.Contains(nameof(LaunchTabViewModel.SelectedModelKey), raised);
            Assert.DoesNotContain(nameof(LaunchTabViewModel.ModelChoices), raised);

            tab.SelectedModelKey = "named:GLM";
            Assert.Equal("GLM", tab.RunLlmProfile);
            Assert.Equal("GLM", tab.SelectedModelChoice?.Profile);
            Assert.True(tab.CanRememberLlmForTeam);

            raised.Clear();
            tab.SelectedModelKey = null;
            tab.SelectedModelKey = "named:nobody";
            Assert.Equal("named:GLM", tab.SelectedModelKey);
            Assert.Empty(raised);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Should_PrefillTheRunsModelWithTheTeams_When_ATeamIsSelected()
    {
        var (root, pinned) = ModelTeamOnDisk("Qwen", useDefault: null);
        try
        {
            var (tab, _, _) = BuildForModel(pinned);
            tab.Target.Select(pinned);

            Assert.False(tab.RunUsesDefaultLlm);
            Assert.Equal("Qwen", tab.RunLlmProfile);
            Assert.Equal("This run: Qwen", tab.RunLlmLine);
            Assert.Equal("Use the default model (GLM)", tab.UseDefaultLlmLabel);
            Assert.Equal(["GLM", "Qwen"], tab.LlmProfiles);
            Assert.False(tab.CanRememberLlmForTeam);

            TeamCatalog.SaveLlmChoice(pinned, TeamLlmChoice.Default);
            var (follower, _, _) = BuildForModel(pinned);
            follower.Target.Select(pinned);

            Assert.True(follower.RunUsesDefaultLlm);
            Assert.Equal("This run: Default (GLM)", follower.RunLlmLine);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Should_LaunchAndRecordTheModelChosenForThisRun_When_TheTeamIsLeftAsItWas()
    {
        var (root, team) = ModelTeamOnDisk(null, useDefault: true);
        try
        {
            var (tab, asks, history) = BuildForModel(team);
            tab.Target.Select(team);

            tab.RunUsesDefaultLlm = false;
            tab.RunLlmProfile = "Qwen";
            Assert.True(tab.CanRememberLlmForTeam);

            await tab.RunAsync(TestContext.Current.CancellationToken);

            Assert.Equal(TeamLlmChoice.Pinned("Qwen"), Assert.Single(asks).Choice);
            Assert.Equal(TeamLlmChoice.Pinned("Qwen"), Assert.Single(history.Recorded).LlmChoice);
            // The team still follows the default: the choice was this run's (criterion 8).
            Assert.Equal(TeamLlmChoice.Default, TeamCatalog.LlmChoiceFor(team));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Should_MakeTheRunsModelTheTeams_When_RememberIsChosen()
    {
        var (root, team) = ModelTeamOnDisk("Qwen", useDefault: null);
        try
        {
            var remembered = new List<(string Team, TeamLlmChoice Choice)>();
            var (tab, _, _) = BuildForModel(team, remember: (path, choice) =>
            {
                remembered.Add((path, choice));
                TeamCatalog.SaveLlmChoice(path, choice);
                return null;
            });
            tab.Target.Select(team);

            tab.RunUsesDefaultLlm = true;
            tab.RememberLlmForTeamCommand.Execute(null);

            Assert.Equal((team, TeamLlmChoice.Default), Assert.Single(remembered));
            Assert.Equal(TeamLlmChoice.Default, TeamCatalog.LlmChoiceFor(team));
            Assert.Equal("veille now runs on: Default (GLM).", tab.StatusMessage);
            Assert.False(tab.CanRememberLlmForTeam);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Should_ReplayOnTheModelTheRunTook_When_TheEntryRecordsOne()
    {
        var (root, team) = ModelTeamOnDisk(null, useDefault: true);
        try
        {
            var (tab, asks, _) = BuildForModel(team);
            var entry = LaunchHistoryEntry.Starting(team, ["run", team], null, team) with { LlmChoice = TeamLlmChoice.Pinned("Qwen") };

            await tab.ReplayAsync(entry, TestContext.Current.CancellationToken);

            Assert.Equal(TeamLlmChoice.Pinned("Qwen"), Assert.Single(asks).Choice);
            Assert.Equal("Qwen", tab.RunLlmProfile);

            // A setting since removed is named as missing: the run takes the default (STUDIO-52).
            var (gone, _, _) = BuildForModel(team, Orkeon.Studio.Core.Profiles.ModelProfileSet.Empty.Upsert(Glm));
            await gone.ReplayAsync(entry, TestContext.Current.CancellationToken);
            Assert.StartsWith("This run: ", gone.RunLlmLine, StringComparison.Ordinal);
            Assert.NotEqual("This run: Qwen", gone.RunLlmLine);
            // The model list keeps it, in the same words, instead of showing the default (refonte lot 12).
            var kept = Assert.IsType<RunModelChoice>(gone.SelectedModelChoice);
            Assert.Equal(RunModelChoiceKind.Named, kept.Kind);
            Assert.Equal("Qwen", kept.Profile);
            Assert.NotEqual("Qwen", kept.Label);
            Assert.Same(kept, gone.ModelChoices[^1]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Should_WarnThatTheChoiceStopsShort_When_AnAgentNamesItsOwnModel()
    {
        var (root, team) = ModelTeamOnDisk(null, useDefault: true);
        try
        {
            File.WriteAllText(Path.Combine(team, "agents", "writer.yaml"), "name: writer\nllm:\n  profile: careful\n");
            var (tab, _, _) = BuildForModel(team);
            tab.Target.Select(team);

            Assert.Equal(
                "1 agent(s) or task(s) of this team name their own model: the choice applies to the others only.",
                tab.CrewLlmWarning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Should_GreyTheChoice_When_ThisMachineHasNoSetting()
    {
        var (root, team) = ModelTeamOnDisk(null, useDefault: true);
        try
        {
            var (tab, _, _) = BuildForModel(team, Orkeon.Studio.Core.Profiles.ModelProfileSet.Empty);
            tab.Target.Select(team);

            Assert.False(tab.HasLlmProfiles);
            Assert.True(tab.RunUsesDefaultLlm);
            Assert.Equal("Use the default model", tab.UseDefaultLlmLabel);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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

/// <summary>
/// The remediated Run screen (audit 05/14): a team card, a plain-language progress
/// card, a localized banner when the CLI is missing, and an open-the-result action.
/// </summary>
public sealed class LaunchScreenFacetsTests
{
    private static LaunchTabViewModel Build(
        FakeTargetProbe? targetProbe = null,
        FakeExecutableProbe? executables = null,
        RecordingShellOpener? shellOpener = null,
        string[]? declaredMounts = null,
        FakeDirectoryProbe? directories = null)
        => new(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(
                new FakeProcessLauncher(),
                new OrkeonBinaryLocator(executables ?? FakeExecutableProbe.WithOrkeonInstalled())),
            TargetProbe = targetProbe ?? new FakeTargetProbe(),
            Directories = directories ?? new FakeDirectoryProbe(),
            SettingsStore = new FakeAppSettingsStore(),
            ShellOpener = shellOpener,
            DeclaredMounts = declaredMounts is null ? null : () => declaredMounts,
        });

    [Fact]
    public async Task The_progress_card_walks_from_ready_to_done_and_the_button_follows()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var tab = Build(probe);
        tab.Target.Select("/crews/team.yaml");

        Assert.Equal("idle", tab.RunBadgeTone);
        var launchLabel = tab.RunButtonLabel;
        var idleTitle = tab.RunStateTitle;

        await tab.RunAsync(TestContext.Current.CancellationToken);

        // The fake launcher exits 0: the badge turns "ok" and the button becomes "Relaunch".
        Assert.Equal("ok", tab.RunBadgeTone);
        Assert.NotEqual(launchLabel, tab.RunButtonLabel);
        Assert.NotEqual(idleTitle, tab.RunStateTitle);
        Assert.NotEqual(tab.RunBadgeText, string.Empty);
    }

    [Fact]
    public void The_team_card_appears_with_the_target_and_falls_back_to_the_file_name()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/veille.yaml");
        var tab = Build(probe);

        Assert.False(tab.HasTeamCard);

        tab.Target.Select("/crews/veille.yaml");

        Assert.True(tab.HasTeamCard);
        Assert.Equal("veille", tab.TeamHeadline);
    }

    [Fact]
    public void The_team_headline_is_the_normalized_name()
    {
        // STUDIO-16 (D-01/D-03): a sidecar whose name is a pasted README gives a one-line
        // headline, and the description carries the derived summary of the need, not the page.
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-headline-{Guid.NewGuid():N}");
        try
        {
            var team = Path.Combine(root, "factures");
            Directory.CreateDirectory(team);
            TeamCatalog.SaveMetadata(team, new StudioTeamMetadata
            {
                Name = "# Extraction des **factures**\n\n> un README entier\n\n- ligne\n- ligne",
                Description = "# Titre\n\nChaque matin, les factures sont lues.\n\nSecond paragraphe, jamais sur la carte.",
                Mounts = ["C:/docs:/docs:ro"],
            });
            // The probe sees the agents folder, so the target resolves as a multi-file crew.
            var tab = Build(new FakeTargetProbe().WithDirectory(team).WithDirectory(Path.Combine(team, "agents")));

            tab.Target.Select(team);

            Assert.True(tab.HasTeamCard);
            Assert.Equal("Extraction des factures", tab.TeamHeadline);
            // #117: the need has its own line under the meta line, never mixed into it.
            Assert.Equal("Chaque matin, les factures sont lues.", tab.TeamDescription);
            Assert.DoesNotContain("Chaque matin", tab.TeamMetaLine, StringComparison.Ordinal);
            Assert.DoesNotContain('\n', tab.TeamMetaLine!);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task The_missing_cli_raises_the_localized_banner_and_an_installed_one_does_not()
    {
        var missing = Build(executables: new FakeExecutableProbe());
        await missing.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(missing.CliBanner);

        var installed = Build();
        await installed.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Null(installed.CliBanner);
    }

    /// <summary>
    /// The banner alone is not enough. A run IS an invocation of the orkeon CLI, so with no CLI
    /// on the machine every launch button has to be dead — not live and apologetic after the
    /// click. This is the shape the failure took in the field: a shared Windows/WSL checkout put
    /// a Linux apphost where Studio looked, the launch went ahead, and the user got
    /// «The specified executable is not a valid application for this OS platform» from the
    /// middle of a run.
    /// </summary>
    [Fact]
    public void A_missing_cli_disables_every_command_that_would_invoke_it()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/veille.yaml");
        var tab = Build(probe, executables: new FakeExecutableProbe());
        tab.Target.Select("/crews/veille.yaml");

        // The target resolves - the only thing standing in the way is the absent tool.
        Assert.True(tab.Target.IsResolved);
        Assert.False(tab.IsBinaryAvailable);

        Assert.False(tab.RunCommand.CanExecute(null));
        Assert.False(tab.ValidateCommand.CanExecute(null));
        Assert.False(tab.ReplayCommand.CanExecute(null));

        // And it says so, naming the machine rather than some path the reader never chose.
        Assert.NotNull(tab.CliBanner);
        Assert.Contains("was not located on this machine", tab.BinaryStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void An_installed_cli_leaves_the_same_commands_live()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/veille.yaml");
        var tab = Build(probe);
        tab.Target.Select("/crews/veille.yaml");

        Assert.True(tab.IsBinaryAvailable);
        Assert.True(tab.RunCommand.CanExecute(null));
        Assert.True(tab.ValidateCommand.CanExecute(null));
        Assert.True(tab.ReplayCommand.CanExecute(null));
    }

    [Fact]
    public async Task Open_result_points_at_the_first_writable_mount_and_needs_a_finished_run()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var opener = new RecordingShellOpener();
        // The settings in force come from the declared list (Settings > Allowed folders) in
        // automatic settings mode: that is what the tab publishes to the mount panel.
        var tab = Build(probe, shellOpener: opener, declaredMounts: ["/srv/docs:/workspace:ro", "/srv/out:/output:rw"]);
        tab.Target.Select("/crews/team.yaml");
        Assert.Equal(["/srv/docs:/workspace:ro", "/srv/out:/output:rw"], tab.Mounts.SettingsMounts);

        // No run yet: nothing to open.
        Assert.False(tab.CanOpenResult);

        await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal("/srv/out", tab.ResultFolder());
        Assert.True(tab.CanOpenResult);
        Assert.Equal("Open the result", tab.OpenResultLabel);
        tab.OpenResultCommand.Execute(null);
        Assert.Equal(["/srv/out"], opener.Opened);
    }

    /// <summary>
    /// Owner's request of 2026-09-20: « Open the result » opens one Explorer window per folder
    /// the run could write to — the team's own first — and the button says how many.
    /// </summary>
    [Fact]
    public async Task Open_result_opens_every_writable_folder_in_force_once_the_teams_own_first()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-open-result-{Guid.NewGuid():N}");
        var team = Path.Combine(root, "veille");
        Directory.CreateDirectory(Path.Combine(team, "agents"));
        try
        {
            TeamCatalog.SaveMetadata(team, new StudioTeamMetadata
            {
                Name = "Veille",
                Mounts = ["./output:/output:rw", "./rapports:/rapports:rw", "./input:/workspace:ro"],
            });
            var probe = new FakeTargetProbe().WithDirectory(team).WithDirectory(Path.Combine(team, "agents"));
            var opener = new RecordingShellOpener();
            // The read-only input exists, as the sidecar's write created it: a launch refuses a
            // missing one (STUDIO-60), and this test is about the result folders.
            var tab = Build(probe, directories: new FakeDirectoryProbe(team, Path.Combine(team, "input")), shellOpener: opener,
                declaredMounts: ["/srv/docs:/docs:ro", "/srv/archive:/archive:rw"]);
            tab.Target.Select(team);

            await tab.RunAsync(TestContext.Current.CancellationToken);

            var output = Path.Combine(team, "output");
            var rapports = Path.Combine(team, "rapports");
            // The team's own two folders first, then the declared writable entry still in force.
            Assert.Equal([output, rapports, "/srv/archive"], tab.ResultFolders());
            Assert.Equal(output, tab.ResultFolder());
            Assert.Equal("Open the 3 result folders", tab.OpenResultLabel);
            tab.OpenResultCommand.Execute(null);
            Assert.Equal([output, rapports, "/srv/archive"], opener.Opened);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>VFS-90: a settings entry withdrawn for the run — its root went to the entry the team names — is no result folder.</summary>
    [Fact]
    public async Task Open_result_skips_a_declared_entry_the_run_did_not_mount()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-open-result-{Guid.NewGuid():N}");
        var team = Path.Combine(root, "crm");
        Directory.CreateDirectory(Path.Combine(team, "agents"));
        var a = Orkeon.Domain.Common.MountId.Create();
        var b = Orkeon.Domain.Common.MountId.Create();
        try
        {
            TeamCatalog.SaveMetadata(team, new StudioTeamMetadata { Name = "CRM", Mounts = [$"{b}|/srv/b:/output:rw"] });
            var probe = new FakeTargetProbe().WithDirectory(team).WithDirectory(Path.Combine(team, "agents"));
            var opener = new RecordingShellOpener();
            var tab = Build(probe, directories: new FakeDirectoryProbe(team), shellOpener: opener,
                declaredMounts: [$"{a}|/srv/a:/output:rw", $"{b}|/srv/b:/output:rw"]);
            tab.Target.Select(team);

            await tab.RunAsync(TestContext.Current.CancellationToken);

            Assert.Equal(["/srv/b"], tab.ResultFolders());
            tab.OpenResultCommand.Execute(null);
            Assert.Equal(["/srv/b"], opener.Opened);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("BR", "UC-04")]
    public async Task ShouldShowTheLogsButton_WhileARunHangsWithoutAResult()
    {
        var launcher = new FakeProcessLauncher();
        var tab = new LaunchTabViewModel(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(launcher, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            TargetProbe = new FakeTargetProbe().WithFile("/crews/team.yaml"),
            Directories = new FakeDirectoryProbe(),
            SettingsStore = new FakeAppSettingsStore(),
            ShellOpener = new RecordingShellOpener(),
        });
        tab.Target.Select("/crews/team.yaml");
        bool? running = null, shows = null, hasResult = null;
        launcher.WhileRunning = () =>
        {
            running = tab.IsRunning;
            shows = tab.ShowsOpenLogs;
            hasResult = tab.HasResult;
        };

        await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.True(running);
        Assert.False(hasResult);
        Assert.True(shows);
    }

    [Fact]
    [Trait("BR", "BR-10")]
    public async Task ShouldRefreshTheLogsReason_WhenTheFolderAppearsDuringTheRun()
    {
        var launcher = new FakeProcessLauncher();
        var directories = new FakeDirectoryProbe();
        var tab = new LaunchTabViewModel(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(launcher, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            TargetProbe = new FakeTargetProbe().WithFile("/crews/team.yaml"),
            Directories = directories,
            SettingsStore = new FakeAppSettingsStore(),
            ShellOpener = new RecordingShellOpener(),
        });
        tab.Target.Select("/crews/team.yaml");
        tab.Options.LlmLogEnabled = true;
        var raised = new List<string?>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        bool? running = null, before = null, canOpen = null;
        string? reasonBefore = null, reasonAfter = "unset";
        var raisedDuring = false;
        launcher.WhileRunning = () =>
        {
            running = tab.IsRunning;
            before = tab.OpenLogsCommand.CanExecute(null);
            reasonBefore = tab.OpenLogsUnavailableReason;
            directories.Directories.Add(DefaultLogFolder);
            raised.Clear();
            launcher.Emit(new ProcessOutputLine(ProcessOutputChannel.StandardOutput, "next line", DateTimeOffset.UnixEpoch));
            reasonAfter = tab.OpenLogsUnavailableReason;
            canOpen = tab.OpenLogsCommand.CanExecute(null);
            raisedDuring = raised.Contains(nameof(LaunchTabViewModel.OpenLogsUnavailableReason));
        };

        await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.True(running);
        Assert.False(before);
        Assert.NotNull(reasonBefore);
        Assert.True(canOpen);
        Assert.Null(reasonAfter);
        Assert.True(raisedDuring);
    }

    [Fact]
    [Trait("BR", "BR-10")]
    public async Task ShouldNotProbeTheLogsFolderPerStreamedDelta_NorAfterItIsFound()
    {
        var launcher = new FakeProcessLauncher();
        var directories = new FakeDirectoryProbe();
        var tab = new LaunchTabViewModel(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(launcher, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            TargetProbe = new FakeTargetProbe().WithFile("/crews/team.yaml"),
            Directories = directories,
            SettingsStore = new FakeAppSettingsStore(),
            ShellOpener = new RecordingShellOpener(),
        });
        tab.Target.Select("/crews/team.yaml");
        tab.Options.LlmLogEnabled = true;
        // What the view does: re-read the reason and the command each time either announces a change.
        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LaunchTabViewModel.OpenLogsUnavailableReason))
                _ = tab.OpenLogsUnavailableReason;
        };
        tab.OpenLogsCommand.CanExecuteChanged += (_, _) => _ = tab.OpenLogsCommand.CanExecute(null);
        int Calls() => directories.ExistsCalls.GetValueOrDefault(DefaultLogFolder);
        void Delta(int n) => launcher.Emit(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput,
            "{\"v\":2,\"seq\":" + n + ",\"ts\":\"2026-09-24T10:29:02Z\",\"kind\":\"llm.delta\",\"text\":\"x\"}"));
        int deltaCalls = -1, afterFound = -1, finalCalls = -1;
        bool? canOpen = null;
        launcher.WhileRunning = () =>
        {
            launcher.Emit(new ProcessOutputLine(ProcessOutputChannel.StandardOutput, "first line", DateTimeOffset.UnixEpoch));
            var baseline = Calls();
            for (var i = 0; i < 50; i++)
                Delta(i);
            deltaCalls = Calls() - baseline;

            directories.Directories.Add(DefaultLogFolder);
            launcher.Emit(new ProcessOutputLine(ProcessOutputChannel.StandardOutput, "folder appeared", DateTimeOffset.UnixEpoch));
            canOpen = tab.OpenLogsCommand.CanExecute(null);
            afterFound = Calls();
            for (var i = 0; i < 20; i++)
                Delta(100 + i);
            launcher.Emit(new ProcessOutputLine(ProcessOutputChannel.StandardOutput, "later line", DateTimeOffset.UnixEpoch));
            finalCalls = Calls() - afterFound;
        };

        await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, deltaCalls);
        Assert.True(canOpen);
        Assert.Equal(0, finalCalls);
    }

    private static string DefaultLogFolder => Path.GetFullPath(Path.Combine("/crews", "llm-logs"));

    [Fact]
    [Trait("BR", "UC-04")]
    public void ShouldOpenTheTeamsLogFolder_WhenTheButtonIsClicked()
    {
        var opener = new RecordingShellOpener();
        var tab = Build(
            new FakeTargetProbe().WithFile("/crews/team.yaml"),
            shellOpener: opener,
            directories: new FakeDirectoryProbe(DefaultLogFolder));
        tab.Target.Select("/crews/team.yaml");
        tab.Options.LlmLogEnabled = true;

        Assert.True(tab.OpenLogsCommand.CanExecute(null));
        Assert.Null(tab.OpenLogsUnavailableReason);
        tab.OpenLogsCommand.Execute(null);

        Assert.Equal([DefaultLogFolder], opener.Opened);
    }

    [Fact]
    [Trait("BR", "UC-04")]
    public void ShouldDisableTheButtonWithItsReason_WhenTheLogFolderDoesNotExist()
    {
        var opener = new RecordingShellOpener();
        var tab = Build(new FakeTargetProbe().WithFile("/crews/team.yaml"), shellOpener: opener);
        tab.Target.Select("/crews/team.yaml");
        tab.Options.LlmLogEnabled = true;

        Assert.False(tab.OpenLogsCommand.CanExecute(null));
        Assert.Equal(
            "The logs folder does not exist yet: it is created at the first logged call.",
            tab.OpenLogsUnavailableReason);
        Assert.Empty(opener.Opened);
    }

    [Fact]
    [Trait("BR", "BR-10")]
    public void ShouldResolveTheLogFolderAgainstTheInheritedDirectory_WhenTheTargetHasNone()
    {
        var inherited = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "llm-logs"));
        var opener = new RecordingShellOpener();
        var tab = Build(
            new FakeTargetProbe().WithFile("team.yaml"),
            shellOpener: opener,
            directories: new FakeDirectoryProbe(inherited));
        tab.Target.Select("team.yaml");
        tab.Options.LlmLogEnabled = true;

        Assert.Null(tab.Target.Target?.WorkingDirectory);
        Assert.True(tab.OpenLogsCommand.CanExecute(null));
        Assert.Null(tab.OpenLogsUnavailableReason);
        tab.OpenLogsCommand.Execute(null);

        Assert.Equal([inherited], opener.Opened);
    }

    [Fact]
    [Trait("BR", "BR-10")]
    public void ShouldDisableTheButton_WhenLoggingIsOff_EvenIfAnOldFolderExists()
    {
        var tab = Build(
            new FakeTargetProbe().WithFile("/crews/team.yaml"),
            shellOpener: new RecordingShellOpener(),
            directories: new FakeDirectoryProbe(DefaultLogFolder));
        tab.Target.Select("/crews/team.yaml");
        tab.Options.LlmLogEnabled = false;
        tab.Options.LlmLogPath = null;

        Assert.False(tab.OpenLogsCommand.CanExecute(null));
        Assert.Equal("LLM exchange logging is off for this launch.", tab.OpenLogsUnavailableReason);
    }
}

/// <summary>The history cards (audit 06/15): plain-language facets on each stored launch.</summary>
public sealed class LaunchHistoryCardTests
{
    [Fact]
    public void The_card_reads_team_name_duration_and_a_localized_outcome()
    {
        var entry = LaunchHistoryEntry
            .Starting("/teams/veille/crew.yaml", ["run", "/teams/veille/crew.yaml"])
            .WithResult(ProcessRunResult.FromExitCode(0, TimeSpan.FromSeconds(125)));

        var card = new LaunchHistoryEntryViewModel(entry);

        Assert.Equal("crew", card.TeamName);
        Assert.Contains("2 min 05 s", card.DateLine, StringComparison.Ordinal);
        Assert.Equal("Finished without errors.", card.OutcomeSentence);
        Assert.Equal("ok", card.OutcomeTone);
    }

    [Fact]
    public void A_failed_run_names_its_exit_code_and_the_result_folder_comes_from_the_argv()
    {
        var entry = LaunchHistoryEntry
            .Starting("t.yaml", ["run", "t.yaml", "--mount", "/srv/docs:/workspace:ro", "/srv/out:/output:rw"])
            .WithResult(ProcessRunResult.FromExitCode(3, TimeSpan.FromSeconds(4)));

        var opener = new RecordingShellOpener();
        var card = new LaunchHistoryEntryViewModel(entry, shellOpener: opener);

        Assert.Contains("3", card.OutcomeSentence, StringComparison.Ordinal);
        Assert.Equal("fail", card.OutcomeTone);
        Assert.True(card.HasResultFolder);
        card.OpenResultCommand.Execute(null);
        Assert.Equal(["/srv/out"], opener.Opened);
    }

    /// <summary>
    /// VFS-90: a Studio launch names a declared folder by <c>--mount-id</c>, so the card reads the
    /// folder from the declared list — and opens every writable folder of the argv, once each.
    /// </summary>
    [Fact]
    public void A_mount_id_in_the_argv_resolves_to_the_declared_folder_and_every_writable_folder_opens()
    {
        var id = Orkeon.Domain.Common.MountId.Create();
        var entry = LaunchHistoryEntry
            .Starting("t", ["run", "t", "--mount", "/teams/t/rapports:/rapports:rw", "/srv/docs:/workspace:ro", "--mount-id", id.ToString()])
            .WithResult(ProcessRunResult.FromExitCode(0, TimeSpan.FromSeconds(4)));

        var opener = new RecordingShellOpener();
        var card = new LaunchHistoryEntryViewModel(
            entry, shellOpener: opener, declaredMounts: () => [$"{id}|/srv/out:/output:rw", "/srv/docs:/workspace:ro"]);

        Assert.True(card.HasResultFolder);
        Assert.Equal(["/teams/t/rapports", "/srv/out"], card.ResultFolders());
        card.OpenResultCommand.Execute(null);
        Assert.Equal(["/teams/t/rapports", "/srv/out"], opener.Opened);

        // Without the declared list, an id names nothing this card can open — the --mount still does.
        Assert.Equal(["/teams/t/rapports"], new LaunchHistoryEntryViewModel(entry, shellOpener: opener).ResultFolders());
    }

    [Fact]
    public void An_entry_without_a_writable_mount_offers_no_result_button()
    {
        var entry = LaunchHistoryEntry.Starting("t.yaml", ["run", "t.yaml"]);
        var card = new LaunchHistoryEntryViewModel(entry, shellOpener: new RecordingShellOpener());

        Assert.False(card.HasResultFolder);
    }
}

/// <summary>The technical journal must leave the window: WPF text blocks are not selectable.</summary>
public sealed class JournalCopyTests
{
    [Fact]
    public void The_journal_copies_as_plain_text_and_leads_with_the_truncation_note()
    {
        var log = new RunLogViewModel();
        Assert.False(log.CanCopy);

        log.Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, "task 1 done"));
        log.Append(ProcessOutputLine.Now(ProcessOutputChannel.StandardError, "WARN something"));

        Assert.True(log.CanCopy);

        // Each line leads with the time it was read (STUDIO-17): a pasted journal has to say when.
        Assert.Equal(
            $"{log.Lines[0].Time}  task 1 done{Environment.NewLine}{log.Lines[1].Time}  WARN something",
            log.BuildText());
    }
}
