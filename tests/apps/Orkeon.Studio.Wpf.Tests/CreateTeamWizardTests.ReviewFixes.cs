using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Findings of the UX review of the creation wizard: a missing engine that can be retried, the
/// verdict's count, the trial's reason and the wait reminder beside another screen's thread.
/// </summary>
public partial class CreateTeamWizardTests
{
    [Fact]
    public async Task A_missing_engine_can_be_retried_without_losing_the_need()
    {
        var (vm, _, _) = Build(cliInstalled: false);
        FillStepOne(vm);

        await Compose(vm);

        Assert.True(vm.IsEngineMissing);
        Assert.False(vm.ComposeCommand.CanExecute(null));
        // « Try again » stays live: the user installs the engine, then retries — « Start over »
        // is not the only way out, and the need is still there.
        Assert.True(vm.FailureRetryCommand.CanExecute(null));
        Assert.Equal("une veille documentaire", vm.Need);

        await vm.FailureRetryCommand.ExecuteAsync();

        Assert.Equal(WizardFailureKind.EngineMissing, vm.Failure!.Kind);
        Assert.Equal("une veille documentaire", vm.Need);
    }

    [Fact]
    public async Task The_verdict_counts_the_unmet_criteria_not_the_findings_that_name_none()
    {
        var (vm, processes, _) = Build();
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(BriefTwoCriteria));
            processes.Emit(Out(
                """{"v":2,"seq":9,"ts":"t","kind":"verdict.ready","score":0.3,"passing":false,"judge":"llm","findings":[{"id":"F1","severity":"major","statement":"Un fichier en trop"},{"id":"F2","severity":"major","acceptance":"A1","statement":"Aucune source"}],"suggestions":[]}"""));
        };
        FillStepOne(vm);

        await Compose(vm);

        Assert.Equal(2, vm.Checklist.Count(line => line.IsFailure));
        Assert.Equal(EnglishStudioStrings.Instance.Format(StudioStringKeys.WizardVerdictToFix, 1), vm.VerdictHeadline);
    }

    [Fact]
    public async Task The_trial_reason_never_says_the_composition_is_missing_once_a_team_is_proposed()
    {
        var (vm, processes) = ComposedAndPaused();
        await Compose(vm);
        vm.CreateAllInsideTeamCommand.Execute(null);
        string? tipWhileRunning = null;
        processes.WhileRunning = () => tipWhileRunning = vm.TryTeamTip;

        await vm.TryTeamCommand.ExecuteAsync();

        Assert.True(vm.HasProposal);
        Assert.Equal(string.Empty, tipWhileRunning);
    }

    [Fact]
    public async Task The_wait_reminder_stays_while_the_panel_shows_another_screens_thread()
    {
        var (vm, _) = ComposedAndPaused();
        await Compose(vm);
        Assert.True(vm.Chat.IsOpen);
        Assert.Equal(string.Empty, vm.WaitPointLabel);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Chat.SetContext(AssistantContext.Run);

        Assert.True(vm.Chat.IsOpen);
        Assert.Equal("Waiting for you: choose a folder", vm.WaitPointLabel);
        Assert.Contains(nameof(CreateTeamViewModel.WaitPointLabel), raised);
    }
    [Fact]
    public void A_language_switch_reads_the_model_line_and_the_verdict_headline_again()
    {
        var strings = new SwitchableStrings();
        var document = Orkeon.Studio.Core.Configuration.AppSettingsDocument.CreateEmpty();
        var llm = new Orkeon.Studio.Wpf.ViewModels.Config.LlmSectionViewModel(() => document, () => { });
        var profiles = new Orkeon.Studio.Wpf.ViewModels.Config.ModelProfilesViewModel(
            new Orkeon.Studio.Core.Profiles.InMemoryModelProfileStore(), llm, probe: new Doubles.FakeLlmEndpointProbe());
        var vm = new CreateTeamViewModel(
            profiles,
            new CreateTeamDependencies
            {
                Client = new Orkeon.Studio.Core.Forge.ForgeClient(
                    new Doubles.FakeProcessLauncher(),
                    new Orkeon.Studio.Core.Process.OrkeonBinaryLocator(Doubles.FakeExecutableProbe.WithOrkeonInstalled())),
                WorkspaceDirectory = "/ws",
                TeamsRoot = "/teams",
                Strings = strings,
            });
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        strings.Switch();

        Assert.Contains(nameof(CreateTeamViewModel.AdoptModelLine), raised);
        Assert.Contains(nameof(CreateTeamViewModel.VerdictHeadline), raised);
    }

    private sealed class SwitchableStrings : IStudioStrings
    {
        public string this[string key] => key;

        public event EventHandler? CultureChanged;

        public void Switch() => CultureChanged?.Invoke(this, EventArgs.Empty);
    }
}
