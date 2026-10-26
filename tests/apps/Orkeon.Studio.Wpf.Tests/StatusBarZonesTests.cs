using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Shell;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// R03 (A03): the status bar's three fixed zones — the model on the left in both modes, an
/// activity in the centre only while it runs or where its wait can be answered. Presentation
/// only: the groups themselves are pinned by <see cref="StatusBarViewModelTests"/>.
/// </summary>
public sealed class StatusBarZonesTests
{
    private static ModelProfilesViewModel Profiles()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var llm = new LlmSectionViewModel(() => document, () => { });
        return new ModelProfilesViewModel(new InMemoryModelProfileStore(), llm, probe: new FakeLlmEndpointProbe());
    }

    private static ComposeProgress Composing(bool engineRunning, string? waitPhrase = null) =>
        new(Stage: "blueprint", EngineRunning: engineRunning, WaitingOnUser: false, Finished: false,
            Narration: null, FileCount: 0, ValidationOk: null, PromptTokens: 0, CompletionTokens: 0,
            Estimated: false, WaitPhrase: waitPhrase);

    [Fact]
    public void The_left_zone_says_no_model_then_names_the_default_profile_in_both_modes()
    {
        var profiles = Profiles();
        var mode = new UiModeViewModel(UiModeViewModel.Novice);
        var bar = new StatusBarViewModel(new StatusBarSources
        {
            Profiles = profiles, Mode = mode, Strings = new FakeResxStudioStrings("fr"),
        });

        Assert.Equal("Aucun modèle", bar.ModelText);
        Assert.False(bar.HasModel);

        profiles.CommitEdit(
            new ModelProfile { Name = "Cloud", Provider = "DeepSeek", Model = "deepseek-chat" },
            previousName: null);

        Assert.Equal("DeepSeek · deepseek-chat", bar.ModelText);
        Assert.True(bar.HasModel);

        mode.SetExpertCommand.Execute(null);
        Assert.Equal("DeepSeek · deepseek-chat", bar.ModelText);
    }

    [Fact]
    public void A_running_composition_is_in_the_centre_from_any_screen()
    {
        var card = new ComposeProgressViewModel();
        var bar = new StatusBarViewModel(new StatusBarSources { Atelier = card });

        card.Update(Composing(engineRunning: true));
        bar.ShownScreen = StatusBarScreen.Run;
        bar.AtelierWaitOnShownStep = false;

        Assert.True(bar.Atelier.IsRunning);
        Assert.True(bar.ShowsAtelier);
    }

    [Fact]
    public void A_wait_shows_only_on_the_create_screen_at_the_step_that_holds_it()
    {
        var card = new ComposeProgressViewModel();
        var bar = new StatusBarViewModel(new StatusBarSources { Atelier = card });
        var changes = new List<string?>();
        bar.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        card.Update(Composing(engineRunning: false, waitPhrase: "Waiting for you: choose a folder"));

        Assert.True(bar.Atelier.IsActive);
        Assert.False(bar.Atelier.IsRunning);
        Assert.True(bar.ShowsAtelier);

        // Step 3 (Essayer, 019): the folder to choose belongs to step 2.
        bar.AtelierWaitOnShownStep = StatusBarViewModel.WaitConcernsStep(WizardWaitPoint.ChooseFolder, 3);
        Assert.False(bar.ShowsAtelier);

        bar.AtelierWaitOnShownStep = StatusBarViewModel.WaitConcernsStep(WizardWaitPoint.ChooseFolder, 2);
        Assert.True(bar.ShowsAtelier);

        // Lancer (075): the wait means nothing there.
        bar.ShownScreen = StatusBarScreen.Run;
        Assert.False(bar.ShowsAtelier);
        Assert.Contains(nameof(StatusBarViewModel.ShowsAtelier), changes);

        // The group itself is untouched: still active, still waiting.
        Assert.True(bar.Atelier.IsActive);
        Assert.True(bar.Atelier.IsWaiting);
    }

    [Theory]
    [InlineData(WizardWaitPoint.ConfirmFolders, 1, true)]
    [InlineData(WizardWaitPoint.ConfirmFolders, 2, false)]
    [InlineData(WizardWaitPoint.FillFolders, 1, true)]
    [InlineData(WizardWaitPoint.ChooseModel, 3, false)]
    [InlineData(WizardWaitPoint.ChooseFolder, 2, true)]
    [InlineData(WizardWaitPoint.ChooseFolder, 3, false)]
    [InlineData(WizardWaitPoint.Adopt, 4, true)]
    [InlineData(WizardWaitPoint.Adopt, 3, false)]
    [InlineData(WizardWaitPoint.AnswerAssistant, 3, true)]
    [InlineData(WizardWaitPoint.Decide, 2, true)]
    [InlineData(WizardWaitPoint.None, 1, true)]
    public void A_wait_point_belongs_to_the_step_that_answers_it(WizardWaitPoint point, int step, bool expected) =>
        Assert.Equal(expected, StatusBarViewModel.WaitConcernsStep(point, step));
}
