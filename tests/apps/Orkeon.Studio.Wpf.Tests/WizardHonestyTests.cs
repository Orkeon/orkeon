using System.ComponentModel;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// What the wizard is allowed to assert. Every test here stands for a sentence the screen
/// used to make and could not support: a green tick on a failed task, a spinner turning while
/// the engine waits on the user, a proposal card over an empty proposal, and chips the view
/// was never told about.
/// </summary>
public sealed class WizardHonestyTests
{
    private static ProcessOutputLine Out(string json) =>
        ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, json);

    private const string Blueprint =
        """{"v":2,"seq":5,"ts":"t","kind":"blueprint.ready","iteration":1,"blueprint":{"crew":{"name":"veille","goal":"g"},"agents":[{"key":"a","role":"Lecteur","goal":"lire","tools":["file_read"]}],"tasks":[{"key":"t1","description":"d","agent":"a","deliverable":{"path":"/output/note.md"}}]}}""";

    private static (CreateTeamViewModel Vm, FakeProcessLauncher Processes) Build()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var llm = new LlmSectionViewModel(() => document, () => { }, new FakeLlmEndpointProbe());
        var profiles = new ModelProfilesViewModel(new InMemoryModelProfileStore(), llm, probe: new FakeLlmEndpointProbe());
        profiles.CommitEdit(
            new ModelProfile { Name = "Local", Provider = "Ollama", Model = "qwen2.5:14b", BaseUrl = "http://localhost:11434/v1" },
            previousName: null);
        profiles.StudioProfileName = "Local";

        var processes = new FakeProcessLauncher();
        var vm = new CreateTeamViewModel(
            profiles,
            new ForgeClient(processes, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            dispatcher: null,
            strings: null,
            workspaceDirectory: "/ws",
            teamsRoot: "/teams");
        vm.Need = "une veille documentaire";
        vm.FrequencyChoices[1].SelectCommand.Execute(null);
        vm.SourceChoices[0].SelectCommand.Execute(null);
        vm.OutputChoices[0].SelectCommand.Execute(null);
        return (vm, processes);
    }

    /// <summary>
    /// The defect the owner photographed: the hint that explains the chips appeared while the
    /// chips did not. HasDerivedMounts was in the notification batch and re-evaluated;
    /// UnclaimedDerivedMounts — the one the list binds — was not, so WPF kept its empty
    /// snapshot until some unrelated gesture happened to refresh it.
    /// </summary>
    [Fact]
    public async Task The_view_is_told_about_the_mounts_the_blueprint_implies()
    {
        var (vm, processes) = Build();
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
        processes.WhileRunning = () =>
        {
            // Only what is raised AFTER the blueprint counts: ResetProjection refreshes the
            // mount surfaces when the compose starts, which would make a broken notification
            // path look healthy.
            raised.Clear();
            processes.Emit(Out(Blueprint));
        };

        await vm.ComposeCommand.ExecuteAsync();

        Assert.NotEmpty(vm.UnclaimedDerivedMounts);
        Assert.Contains(nameof(vm.UnclaimedDerivedMounts), raised, StringComparer.Ordinal);
        Assert.Contains(nameof(vm.HasDerivedMounts), raised, StringComparer.Ordinal);
    }

    [Fact]
    public async Task No_proposal_card_before_there_is_a_proposal()
    {
        var (vm, processes) = Build();
        var duringCompose = true;
        processes.WhileRunning = () =>
        {
            duringCompose = vm.HasProposal;
            processes.Emit(Out(Blueprint));
        };

        await vm.ComposeCommand.ExecuteAsync();

        Assert.False(duringCompose);   // the stepper reaches step 2 long before the blueprint
        Assert.True(vm.HasProposal);
    }

    /// <summary>
    /// The engine child stays alive while blocked on stdin, so «is it running» cannot mean
    /// «is it working». A spinner turning at the arbitration tells the user to wait for
    /// something that is waiting for them.
    /// </summary>
    [Fact]
    public async Task The_spinner_stops_when_the_engine_is_the_one_waiting()
    {
        var (vm, processes) = Build();
        bool workingWhileAsked = true, waiting = false;
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(
                """{"v":2,"seq":8,"ts":"t","kind":"decision.needed","options":["accept","retry","refine","abort"]}"""));
            workingWhileAsked = vm.IsEngineWorking;
            waiting = vm.IsEngineWaitingOnUser;
        };

        await vm.ComposeCommand.ExecuteAsync();

        Assert.True(waiting);
        Assert.False(workingWhileAsked);
    }

    /// <summary>
    /// RunInProgress is raised by run.started and cleared only by run.finished — which never
    /// arrives when the child dies. The indicator used to stay lit until «Recommencer».
    /// </summary>
    [Fact]
    public async Task A_trial_that_crashed_stops_claiming_to_be_running()
    {
        var (vm, processes) = Build();
        processes.ExitCode = 2;
        processes.WhileRunning = () => processes.Emit(Out(
            """{"v":2,"seq":6,"ts":"t","kind":"run.started","run":1,"target":"/crew"}"""));

        await vm.ComposeCommand.ExecuteAsync();

        Assert.True(vm.RunInProgress);      // the model never heard run.finished — that is true
        Assert.False(vm.TrialInProgress);   // but the screen must not keep spinning over it
    }

    [Fact]
    public async Task A_failed_task_is_not_drawn_as_a_success()
    {
        var (vm, processes) = Build();
        processes.WhileRunning = () => processes.Emit(Out(
            """{"v":2,"seq":7,"ts":"t","kind":"task.completed","taskId":"extraction","agentRole":"Lecteur","success":false,"durationMs":900}"""));

        await vm.ComposeCommand.ExecuteAsync();

        var line = Assert.Single(vm.Activity);
        Assert.False(line.Success);
        // The task id joins the role: one agent may own several tasks.
        Assert.Equal("extraction", line.Detail);
        Assert.True(line.HasDetail);
    }

    [Fact]
    public async Task The_engines_own_error_survives_the_generic_apology()
    {
        var (vm, processes) = Build();
        processes.ExitCode = 2;
        processes.WhileRunning = () =>
        {
            processes.Emit(ProcessOutputLine.Now(
                ProcessOutputChannel.StandardError, "orkeon forge: the model refused twice."));
            processes.Emit(Out(
                """{"v":2,"seq":9,"ts":"t","kind":"session.finished","status":"failed","exitCode":2}"""));
        };

        await vm.ComposeCommand.ExecuteAsync();

        Assert.Contains("refused twice", vm.StatusMessage, StringComparison.Ordinal);
    }
}
