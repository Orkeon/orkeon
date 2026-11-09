using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Refonte lot 10: step 2 in four tabs (R14) — the team, its folders, the instruction and the
/// composition, read only — each with its state and subtitle; « Try the team » that waits for
/// a folder still to choose; the « Can: » pills folded behind « + N ».
/// </summary>
public partial class CreateTeamWizardTests
{
    [Fact]
    public async Task A_folder_to_choose_opens_the_folders_tab_and_holds_the_trial_until_it_is_answered()
    {
        var (vm, _) = ComposedAndPaused();

        await Compose(vm);

        Assert.Equal(2, vm.Step);
        Assert.Equal(CreateTeamViewModel.Step2FoldersTab, vm.Step2TabIndex);
        Assert.Equal("Done", vm.TeamTabState);
        Assert.Equal($"{vm.Agents.Count} {(vm.Agents.Count == 1 ? "agent" : "agents")} · proposed", vm.TeamTabSubtitle);
        Assert.Equal("Expected", vm.Step2FoldersTabState);
        Assert.Contains("to choose", vm.Step2FoldersTabSubtitle, StringComparison.Ordinal);
        Assert.True(vm.CanTryTeam);
        Assert.False(vm.CanTryTeamNow);
        Assert.False(vm.TryTeamCommand.CanExecute(null));
        Assert.Equal(vm.Step2Hint, vm.TryTeamTip);
        Assert.True(vm.IsStep2HintWarning);
        Assert.True(vm.AdoptWithoutTrialCommand.CanExecute(null));

        vm.CreateAllInsideTeamCommand.Execute(null);

        Assert.Equal("Done", vm.Step2FoldersTabState);
        Assert.EndsWith("· inside the team", vm.Step2FoldersTabSubtitle, StringComparison.Ordinal);
        Assert.True(vm.TryTeamCommand.CanExecute(null));
        Assert.Equal(string.Empty, vm.TryTeamTip);
        Assert.False(vm.IsStep2HintWarning);
        Assert.Equal("Team composed — you can try it.", vm.Step2Hint);
        Assert.True(vm.HasStep2Hint);
    }

    [Fact]
    public async Task The_composition_tab_reports_the_stopped_composition_read_only()
    {
        var (vm, _) = ComposedAndPaused();

        await Compose(vm);

        Assert.False(vm.IsComposing);
        Assert.Equal(vm.ValidationOk switch { true => "Done", false => "Warning", null => "Empty" }, vm.CompositionTabState);
        Assert.StartsWith(vm.ValidationOk switch { true => "Composed · ", false => "To fix · ", null => "Stopped · " }, vm.CompositionTabSubtitle, StringComparison.Ordinal);
        Assert.StartsWith(vm.ValidationOk switch { true => "Composed", false => "Composition to fix", null => "Composition stopped" }, vm.CompositionStateText, StringComparison.Ordinal);
        Assert.Equal($"{vm.Files.Count} {(vm.Files.Count == 1 ? "file" : "files")} in the team's draft", vm.CompositionFilesText);
        Assert.Equal("Composition finished.", vm.CompositionFooter);
        Assert.False(vm.IsCompositionTabEmpty);
    }

    /// <summary>UX lot 11: the composition tab's subtitle and state follow the validation (true / false / null).</summary>
    [Theory]
    [InlineData("true", "Composed · ", "Done")]
    [InlineData("false", "To fix · ", "Warning")]
    [InlineData(null, "Stopped · ", "Empty")]
    public async Task The_composition_subtitle_and_state_follow_the_validation(string? ok, string subtitle, string state)
    {
        var (vm, processes, _) = Build();
        processes.OutputToEmit.Add(Out(SessionStarted));
        processes.OutputToEmit.Add(Out(ReadingWritingBlueprint()));
        if (ok is not null)
            processes.OutputToEmit.Add(Out($$$"""{"v":2,"seq":8,"ts":"t","kind":"validation.result","ok":{{{ok}}},"errors":[]}"""));
        processes.OutputToEmit.Add(Out(Paused));
        FillStepOne(vm);

        await Compose(vm);

        Assert.Equal(ok is null ? null : ok == "true", vm.ValidationOk);
        Assert.StartsWith(subtitle, vm.CompositionTabSubtitle, StringComparison.Ordinal);
        Assert.Contains($"{vm.Files.Count} ", vm.CompositionTabSubtitle, StringComparison.Ordinal);
        Assert.Equal(state, vm.CompositionTabState);
    }

    [Fact]
    public async Task The_tab_on_screen_follows_the_footers_and_a_new_creation_brings_back_the_team()
    {
        var (vm, _) = ComposedAndPaused();
        await Compose(vm);

        vm.SeeNotesCommand.Execute(null);
        Assert.Equal(CreateTeamViewModel.Step2NotesTab, vm.Step2TabIndex);
        vm.SeeCompositionCommand.Execute(null);
        Assert.Equal(CreateTeamViewModel.Step2CompositionTab, vm.Step2TabIndex);
        vm.BackToTeamCommand.Execute(null);
        Assert.Equal(CreateTeamViewModel.Step2TeamTab, vm.Step2TabIndex);
        vm.SeeFoldersCommand.Execute(null);
        Assert.Equal(CreateTeamViewModel.Step2FoldersTab, vm.Step2TabIndex);

        vm.ShowYamlViewCommand.Execute(null);
        Assert.True(vm.IsYamlView);
        Assert.False(vm.IsProposalView);

        vm.RestartCommand.Execute(null);

        Assert.Equal(CreateTeamViewModel.Step2TeamTab, vm.Step2TabIndex);
        Assert.True(vm.IsProposalView);
        Assert.Equal("Empty", vm.TeamTabState);
        Assert.Equal("Not proposed yet", vm.TeamTabSubtitle);
        Assert.Equal("Empty", vm.CompositionTabState);
        Assert.Equal("Not started", vm.CompositionTabSubtitle);
        Assert.Equal("Empty", vm.Step2FoldersTabState);
    }

    [Fact]
    public void An_agent_folds_its_pills_after_five_and_plus_n_unfolds_them()
    {
        var tools = Enumerable.Range(1, 7).Select(i => new WizardToolChip($"tool {i}", "file")).ToList();
        var card = new WizardAgentCard("a", "Agent", "A", "Reads.", tools, edit: null, canEdit: static () => false);
        var raised = new List<string?>();
        card.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.Equal(WizardAgentCard.FoldedToolCount, card.VisibleTools.Count);
        Assert.True(card.HasHiddenTools);
        Assert.Equal("+ 2", card.MoreToolsLabel);

        card.ShowAllToolsCommand.Execute(null);

        Assert.Equal(7, card.VisibleTools.Count);
        Assert.False(card.HasHiddenTools);
        Assert.Equal(string.Empty, card.MoreToolsLabel);
        Assert.Contains(nameof(WizardAgentCard.VisibleTools), raised);
    }

    [Fact]
    public void An_agent_with_five_pills_or_fewer_folds_nothing()
    {
        var tools = Enumerable.Range(1, 5).Select(i => new WizardToolChip($"tool {i}", "file")).ToList();
        var card = new WizardAgentCard("a", "Agent", "A", "Reads.", tools, edit: null, canEdit: static () => false);

        Assert.Equal(5, card.VisibleTools.Count);
        Assert.False(card.HasHiddenTools);
    }

    [Fact]
    public async Task Adjust_the_team_brings_out_each_agents_edit_until_a_new_creation()
    {
        // UX lot 12 (016): the Novice sees one « Adjust the team… » rather than one « Edit » per agent.
        var (vm, _) = ComposedAndPaused();
        await Compose(vm);

        Assert.False(vm.IsAdjustingTeam);
        Assert.True(vm.OffersAdjustTeam);

        vm.AdjustTeamCommand.Execute(null);

        Assert.True(vm.IsAdjustingTeam);
        Assert.False(vm.OffersAdjustTeam);

        vm.RestartCommand.Execute(null);

        Assert.False(vm.IsAdjustingTeam);
        Assert.True(vm.OffersAdjustTeam);
    }
}
