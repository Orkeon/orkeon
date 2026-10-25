using Orkeon.Studio.Core.Profiles;
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
    public void A_studio_with_no_model_waits_on_one_and_the_election_ends_the_wait()
    {
        var (vm, _, profiles) = Build(withAssistant: false);

        Assert.Equal(WizardWaitPoint.ChooseModel, vm.WaitPoint);
        Assert.Equal("Waiting for you: choose a model", vm.WaitPointLabel);
        // No session is under way: the gate names the wait, the status bar stays at rest.
        Assert.Null(vm.Progress.WaitPhrase);

        profiles.CommitEdit(
            new ModelProfile
            {
                Name = "Local", Provider = "Ollama", Model = "qwen2.5:14b", BaseUrl = "http://localhost:11434/v1",
            },
            previousName: null);
        profiles.StudioProfileName = "Local";

        Assert.Equal(WizardWaitPoint.None, vm.WaitPoint);
        Assert.Null(vm.Progress.WaitPhrase);
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
            Assert.Equal("2 folders · 1 to choose", wizard.FoldersTabSubtitle);

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
        // STUDIO-57: the wizard waits on the fill question; nothing to drop here.
        vm.FillFoldersNoCommand.Execute(null);

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
            Assert.Equal("2 folders · 1 to choose", wizard.FoldersTabSubtitle);
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
        Assert.Equal("Waiting for you: choose a folder", vm.Progress.WaitPhrase);
        Assert.True(vm.Chat.HasPendingAction);
        Assert.Equal("Choose the folder to continue, or ask a question", vm.Chat.Placeholder);

        // From another step, « Waiting for you: … » brings step 2 back.
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.GoStep1Command.Execute(null);
        Assert.False(vm.IsWaitOnChooseFolder);
        Assert.Contains(nameof(CreateTeamViewModel.IsWaitOnChooseFolder), raised);
        vm.Chat.PendingRevealCommand.Execute(null);
        Assert.Equal(2, vm.Step);

        vm.CreateAllInsideTeamCommand.Execute(null);

        Assert.Equal(WizardWaitPoint.None, vm.WaitPoint);
        Assert.False(vm.Chat.HasPendingAction);
    }

    /// <summary>
    /// STUDIO-57. The engine waits on the fill step: the session says it waits on the user, the
    /// question is the wait point, and « No » sends <c>folders.filled</c> — only then does the
    /// engine compose, and the wizard follows it to step 2.
    /// </summary>
    [Fact]
    public async Task The_engine_waits_on_the_fill_step_and_the_answer_lets_it_compose()
    {
        var (vm, processes, _) = Build();
        FillStepOne(vm);
        Exception? failure = null;
        processes.WhileRunning = () =>
        {
            processes.WhileRunning = null;
            processes.Emit(Out(SessionStarted));
            processes.Emit(Out(FoldersProposedLine(PdfFolders)));
            vm.ConfirmFoldersCommand.Execute(null);
        };
        processes.OnInputLine = line =>
        {
            if (line.Contains("\"folders.confirmed\"", StringComparison.Ordinal))
            {
                processes.Emit(Out(
                    """{"v":2,"seq":3,"ts":"t","kind":"brief.ready","brief":{"goal":"g","folders":[{"path":"/inpdf","role":"input"},{"path":"/outmd","role":"output"}]},"heldFolders":[{"path":"/outmd","role":"output","dir":"/ws/.orkeon/forge/veille/folders/outmd"}]}"""));
                failure = Record.Exception(() =>
                {
                    Assert.True(vm.IsSessionWaiting);
                    Assert.False(vm.IsEngineWorking);
                    Assert.Equal(WizardWaitPoint.AnswerAssistant, vm.WaitPoint);
                    Assert.Equal(1, vm.Step);
                    vm.Chat.Question!.Replies[1].ReplyCommand.Execute(null);
                });
            }
            else if (line.Contains("\"folders.filled\"", StringComparison.Ordinal))
            {
                processes.Emit(Out("""{"v":2,"seq":4,"ts":"t","kind":"folders.filled","folders":[{"path":"/outmd","entries":0}]}"""));
                processes.Emit(Out(PdfBlueprint()));
                processes.Emit(Out(Paused));
            }
        };

        await Compose(vm);

        Assert.Null(failure);
        Assert.Single(processes.InputLines, line => line == """{"kind":"folders.filled"}""");
        Assert.False(vm.IsFillFoldersStep);
        Assert.Equal(2, vm.Step);
        Assert.True(vm.CanTryTeam);
    }
}
