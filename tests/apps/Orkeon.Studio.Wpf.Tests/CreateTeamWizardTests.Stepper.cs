using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The stepper of the redesign (lot 3): validated steps, the way back, the session's one state
/// at the end of the line, the step-2 action bar's sentence and the verdict's summary.
/// </summary>
public partial class CreateTeamWizardTests
{
    private const string BriefTwoCriteria =
        """{"v":2,"seq":2,"ts":"t","kind":"brief.ready","brief":{"goal":"g","acceptance":[{"id":"A1","statement":"Le resume cite ses sources","kind":"must"},{"id":"A2","statement":"Moins d'une page","kind":"should"}]}}""";

    private static (CreateTeamViewModel Vm, FakeProcessLauncher Processes) ComposedAndPaused()
    {
        var (vm, processes, _) = Build();
        processes.OutputToEmit.Add(Out(SessionStarted));
        processes.OutputToEmit.Add(Out(ReadingWritingBlueprint()));
        processes.OutputToEmit.Add(Out(Paused));
        FillStepOne(vm);
        return (vm, processes);
    }

    [Fact]
    public async Task A_composed_session_checks_step_one_and_opens_no_step_it_has_not_reached()
    {
        var (vm, _) = ComposedAndPaused();

        await Compose(vm);

        Assert.Equal(2, vm.Step);
        Assert.True(vm.IsStep1Done);
        Assert.False(vm.IsStep2Done);
        Assert.True(vm.IsStep2Reached);
        Assert.False(vm.IsStep3Reached);
        Assert.True(vm.GoStep1Command.CanExecute(null));
        Assert.False(vm.GoStep3Command.CanExecute(null));
        Assert.False(vm.GoStep4Command.CanExecute(null));
    }

    [Fact]
    public async Task Back_shows_the_step_before_and_is_unavailable_on_step_one()
    {
        var (vm, _) = ComposedAndPaused();
        await Compose(vm);

        Assert.True(vm.GoBackCommand.CanExecute(null));
        vm.GoBackCommand.Execute(null);

        Assert.Equal(1, vm.Step);
        Assert.False(vm.GoBackCommand.CanExecute(null));
        // Going back keeps what the session reached: step 2 opens again from its pill.
        Assert.True(vm.IsStep1Done);
        Assert.True(vm.GoStep2Command.CanExecute(null));
    }

    [Fact]
    public async Task A_session_the_engine_left_on_disk_reads_as_a_draft_saved_at_a_time()
    {
        var (vm, _) = ComposedAndPaused();

        await Compose(vm);

        Assert.True(vm.IsDraftSaved);
        Assert.False(vm.IsSessionPaused);
        Assert.False(vm.IsSessionWorking);
        Assert.True(vm.HasSessionState);
        var prefix = EnglishStudioStrings.Instance[StudioStringKeys.WizardDraftSavedAt].Replace("{0}", "", StringComparison.Ordinal);
        Assert.StartsWith(prefix, vm.SessionStateLabel, StringComparison.Ordinal);
        Assert.Matches(@"\d\d:\d\d$", vm.SessionStateLabel);
    }

    [Fact]
    public async Task A_session_the_user_stopped_reads_paused_with_resume_and_no_failure()
    {
        var (vm, processes, _) = Build();
        processes.HonourCancellation = true;
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(SessionStarted));
            vm.StopCommand.Execute(null);
        };
        FillStepOne(vm);

        await Compose(vm);

        Assert.False(vm.HasFailure);
        Assert.True(vm.IsSessionPaused);
        Assert.False(vm.IsDraftSaved);
        Assert.True(vm.CanResumeHere);
        Assert.True(vm.ResumeHereCommand.CanExecute(null));
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.WizardSessionPaused], vm.SessionStateLabel);
        // The old « Stopped — resume from My teams » sentence is gone from the status line.
        Assert.DoesNotContain("Stopped", vm.StatusMessage ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restarting_forgets_the_session_state_of_the_previous_creation()
    {
        var (vm, _) = ComposedAndPaused();
        await Compose(vm);
        Assert.True(vm.HasSessionState);

        vm.RestartCommand.Execute(null);

        Assert.False(vm.HasSessionState);
        Assert.Equal(string.Empty, vm.SessionStateLabel);
    }

    [Fact]
    public async Task The_step_two_bar_counts_the_folders_left_to_choose()
    {
        var (vm, _) = ComposedAndPaused();

        await Compose(vm);

        var count = vm.MountRows.Count(row => row.CanChooseFolder);
        Assert.Equal(count, vm.FoldersToChooseCount);
        Assert.Equal(count > 0, vm.HasFoldersToChoose);
        var expected = count > 0
            ? EnglishStudioStrings.Instance.Format(StudioStringKeys.WizardFoldersToChoose, count)
            : EnglishStudioStrings.Instance[StudioStringKeys.WizardPausedHint];
        Assert.Equal(expected, vm.Step2Hint);
    }

    [Fact]
    public async Task Choosing_every_folder_inside_the_team_leaves_the_pause_sentence()
    {
        var (vm, _) = ComposedAndPaused();
        await Compose(vm);
        Assert.True(vm.HasFoldersToChoose);

        vm.CreateAllInsideTeamCommand.Execute(null);

        Assert.Equal(0, vm.FoldersToChooseCount);
        Assert.False(vm.HasFoldersToChoose);
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.WizardPausedHint], vm.Step2Hint);
    }

    [Fact]
    public async Task A_criterion_with_a_finding_that_blocks_nothing_is_a_remark_not_a_failure()
    {
        var (vm, processes, _) = Build();
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(BriefTwoCriteria));
            processes.Emit(Out(
                """{"v":2,"seq":9,"ts":"t","kind":"verdict.ready","score":0.8,"passing":true,"judge":"llm","findings":[{"id":"F1","severity":"minor","acceptance":"A1","statement":"Une source manque"}],"suggestions":[]}"""));
        };
        FillStepOne(vm);

        await Compose(vm);

        Assert.Equal(2, vm.Checklist.Count);
        var remark = vm.Checklist[0];
        Assert.True(remark.IsRemark);
        Assert.False(remark.IsFailure);
        Assert.True(vm.Checklist[1].Passed);
        Assert.Equal(EnglishStudioStrings.Instance.Format(StudioStringKeys.WizardVerdictSummary, 2, 2), vm.VerdictSummary);
        Assert.True(vm.HasVerdictSummary);
    }

    [Fact]
    public async Task A_finding_that_names_no_criterion_is_shown_but_not_counted_as_a_criterion()
    {
        var (vm, processes, _) = Build();
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(BriefTwoCriteria));
            processes.Emit(Out(
                """{"v":2,"seq":9,"ts":"t","kind":"verdict.ready","score":0.8,"passing":true,"judge":"llm","findings":[{"id":"F1","severity":"minor","statement":"Un fichier en trop"}],"suggestions":[]}"""));
        };
        FillStepOne(vm);

        await Compose(vm);

        Assert.Equal(3, vm.Checklist.Count);
        Assert.Equal(EnglishStudioStrings.Instance.Format(StudioStringKeys.WizardVerdictSummary, 2, 2), vm.VerdictSummary);
    }

    [Fact]
    public async Task A_blocking_finding_is_a_failure()
    {
        var (vm, processes, _) = Build();
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(BriefTwoCriteria));
            processes.Emit(Out(
                """{"v":2,"seq":9,"ts":"t","kind":"verdict.ready","score":0.3,"passing":false,"judge":"llm","findings":[{"id":"F1","severity":"blocking","acceptance":"A1","statement":"Aucune source"}],"suggestions":[]}"""));
        };
        FillStepOne(vm);

        await Compose(vm);

        Assert.True(vm.Checklist[0].IsFailure);
        Assert.False(vm.Checklist[0].IsRemark);
    }

    [Fact]
    public async Task A_mechanical_verdict_has_no_summary_sentence()
    {
        var (vm, processes, _) = Build();
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(BriefTwoCriteria));
            processes.Emit(Out(
                """{"v":2,"seq":9,"ts":"t","kind":"verdict.ready","score":0.7,"passing":true,"judge":"deterministic","findings":[],"suggestions":[]}"""));
        };
        FillStepOne(vm);

        await Compose(vm);

        Assert.True(vm.VerdictIsMechanical);
        Assert.False(vm.HasVerdictSummary);
    }

    [Fact]
    public void The_need_summary_folds_its_line_breaks()
    {
        var (vm, _, _) = Build();

        vm.Need = "Lire les factures\n\n  et   les resumer";

        Assert.Equal("Lire les factures et les resumer", vm.NeedSummary);
    }
}
