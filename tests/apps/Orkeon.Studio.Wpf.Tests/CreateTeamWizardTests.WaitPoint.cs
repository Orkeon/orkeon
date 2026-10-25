using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Lot 6 of the redesign: the single named wait point (R13) and step 1 in three tabs (R14) —
/// the folders tab that opens with the amber dot, the fiches' flags, the panes' footers, the
/// assistant's bubble that runs the card's command, and Enter that no longer sends.
/// </summary>
public partial class CreateTeamWizardTests
{
    [Fact]
    public void Before_the_composition_the_need_tab_leads_and_the_folders_tab_says_when_they_come()
    {
        var (vm, _, _) = Build();

        Assert.Equal(CreateTeamViewModel.Step1NeedTab, vm.Step1TabIndex);
        Assert.Equal("Empty", vm.NeedTabState);
        Assert.Equal("Describe the work to continue.", vm.NeedFooter);
        Assert.Equal("Empty", vm.FoldersTabState);
        Assert.True(vm.IsFoldersTabEmpty);
        Assert.Equal("Proposed after the composition", vm.FoldersTabSubtitle);
        Assert.Equal("Empty", vm.NotesTabState);
        Assert.Equal("No instruction yet.", vm.NotesFooter);
        Assert.False(vm.Step1BackCommand.CanExecute(null));

        FillStepOne(vm);
        vm.ComposeNotes.Consigne = "Cite every source.";

        Assert.Equal("Done", vm.NeedTabState);
        Assert.Equal("Need described.", vm.NeedFooter);
        Assert.Equal("Done", vm.NotesTabState);
        Assert.Equal("Instruction taken as written.", vm.NotesFooter);

        vm.ShowFoldersTabCommand.Execute(null);
        Assert.Equal(CreateTeamViewModel.Step1FoldersTab, vm.Step1TabIndex);
        vm.Step1BackCommand.Execute(null);
        Assert.Equal(CreateTeamViewModel.Step1NeedTab, vm.Step1TabIndex);
    }

    [Fact]
    public void The_brief_card_reads_the_need_as_typed_and_follows_every_edit()
    {
        var (vm, _, _) = Build();
        var raised = new List<string?>();
        vm.Chat.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Need = "  Summarise the weekly PDF reports.  ";

        Assert.Contains(nameof(ChatThreadViewModel.Brief), raised);
        Assert.Equal("Summarise the weekly PDF reports.", vm.Chat.Brief);

        vm.Need = "Summarise the monthly PDF reports.";
        Assert.Equal("Summarise the monthly PDF reports.", vm.Chat.Brief);
    }

    [Fact]
    public async Task Folders_waiting_for_the_confirm_are_the_one_named_wait_point()
    {
        var (vm, processes, _) = Build();
        FillStepOne(vm);
        var reached = new List<WizardWaitPoint>();
        vm.WaitPointReached += (_, _) => reached.Add(vm.WaitPoint);
        Exception? failure = null;
        ScriptFoldersStep(vm, processes, PdfFolders, wizard => failure = Record.Exception(() =>
        {
            Assert.Equal(WizardWaitPoint.ConfirmFolders, wizard.WaitPoint);
            Assert.Equal("Waiting for you: confirm the folders", wizard.SessionStateLabel);
            Assert.False(wizard.IsSessionWorking);
            Assert.Equal("Confirm the folders to move on to the composition.", wizard.Step1Hint);
            Assert.Equal("Waiting for you: confirm the folders", wizard.Progress.Title);

            // The tab opens itself, with the amber dot.
            Assert.Equal(CreateTeamViewModel.Step1FoldersTab, wizard.Step1TabIndex);
            Assert.Equal("Expected", wizard.FoldersTabState);
            Assert.Equal("2 folders to confirm", wizard.FoldersTabSubtitle);

            // The assistant stops its indicator, names the action and holds Enter back.
            Assert.False(wizard.Chat.IsBusy);
            Assert.True(wizard.Chat.HasPendingAction);
            Assert.Equal("Confirm the folders", wizard.Chat.PendingAction!.ActionLabel);
            Assert.Equal("Confirm the folders to continue, or ask a question", wizard.Chat.Placeholder);
            Assert.False(wizard.Chat.EnterSends);

            // The bubble's button is the card's own command.
            Assert.Same(wizard.ConfirmFoldersCommand, wizard.Chat.PendingAction.Action);
            wizard.Chat.PendingActionCommand.Execute(null);
        }), PdfBlueprint(), Paused);

        await Compose(vm);

        Assert.Null(failure);
        Assert.Equal(WizardWaitPoint.ConfirmFolders, reached[0]);
        Assert.False(vm.IsFoldersStep);
        Assert.Equal("Done", vm.FoldersTabState);
        Assert.Equal("2 folders confirmed", vm.FoldersTabSubtitle);
        // The next wait point takes over: /inpdf is still to choose before the trial.
        Assert.Equal(WizardWaitPoint.ChooseFolder, vm.WaitPoint);
        Assert.Equal("Show the folder", vm.Chat.PendingAction!.ActionLabel);
    }

    [Fact]
    public async Task A_fiche_says_it_was_deduced_until_edited_and_a_read_folder_to_choose_names_the_footer()
    {
        var (vm, processes, _) = Build();
        FillStepOne(vm);
        ScriptFoldersStep(vm, processes, PdfFolders, wizard =>
        {
            var input = wizard.FolderRows[0];
            var output = wizard.FolderRows[1];

            Assert.True(input.ShowsDeduced);
            Assert.True(input.NeedsChoice);
            Assert.True(input.IsOnMachine);
            Assert.False(output.NeedsChoice);
            Assert.False(output.IsOnMachine);
            Assert.StartsWith("1 folder to choose. You can confirm now:", wizard.FoldersFooter, StringComparison.Ordinal);
            Assert.Contains("« inpdf »", wizard.FoldersFooter, StringComparison.Ordinal);

            input.Purpose = "The PDFs to summarise";
            Assert.False(input.ShowsDeduced);
            Assert.True(output.ShowsDeduced);

            wizard.ConfirmFoldersCommand.Execute(null);
        }, PdfBlueprint(), Paused);

        await Compose(vm);

        Assert.False(vm.IsFoldersStep);
    }

    [Fact]
    public async Task A_read_folder_the_definition_places_is_not_to_choose_and_is_bound_on_confirm()
    {
        var (vm, processes, _) = Build();
        FillStepOne(vm);
        const string proposed =
            """{"v":2,"seq":2,"ts":"t","kind":"folders.proposed","folders":[{"path":"/inpdf","role":"input","purpose":"p","dir":"/data/pdf"},{"path":"/outmd","role":"output","purpose":"p"}],"defaults":false}""";
        ScriptFoldersStep(vm, processes, proposed, wizard =>
        {
            var input = wizard.FolderRows[0];
            Assert.False(input.IsUnanswered);
            Assert.False(input.NeedsChoice);
            Assert.Equal("/data/pdf", input.Directory);
            Assert.Equal("Every folder has its place. Confirm to continue.", wizard.FoldersFooter);

            wizard.ConfirmFoldersCommand.Execute(null);
        }, [PdfBlueprint(), Paused]);

        await Compose(vm);

        Assert.Contains(vm.TeamMounts, entry => entry.Contains("/data/pdf", StringComparison.Ordinal) && entry.Contains("/inpdf", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.MountRows, row => row.VirtualPath == "/inpdf" && row.CanChooseFolder);
    }

    [Fact]
    public async Task A_folder_still_to_choose_on_step_two_is_the_wait_point_the_bubble_reveals()
    {
        var (vm, _) = ComposedAndPaused();

        await Compose(vm);

        Assert.Equal(2, vm.Step);
        Assert.Equal(WizardWaitPoint.ChooseFolder, vm.WaitPoint);
        Assert.True(vm.IsWaitOnChooseFolder);
        Assert.True(vm.Chat.HasPendingAction);
        Assert.Equal("Choose the folder to continue, or ask a question", vm.Chat.Placeholder);

        // From another step, « Waiting for you: … » brings step 2 back.
        vm.GoStep1Command.Execute(null);
        Assert.False(vm.IsWaitOnChooseFolder);
        vm.Chat.PendingRevealCommand.Execute(null);
        Assert.Equal(2, vm.Step);

        vm.CreateAllInsideTeamCommand.Execute(null);

        Assert.Equal(WizardWaitPoint.None, vm.WaitPoint);
        Assert.False(vm.Chat.HasPendingAction);
    }
}
