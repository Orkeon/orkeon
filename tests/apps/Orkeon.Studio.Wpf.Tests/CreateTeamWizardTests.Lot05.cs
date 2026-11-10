using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// UX lot 05: an interrupted brief offers « Resume » in the chat, the step-4 model line follows
/// the picker (default first, then a pinned profile), and a missing engine greys « Compose the
/// team » with its reason instead of letting it run into the same wall again.
/// </summary>
public partial class CreateTeamWizardTests
{
    [Fact]
    public async Task An_interrupted_brief_offers_resume_in_the_chat_and_flags_the_need_tab()
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

        Assert.True(vm.Chat.WasInterrupted);
        var pending = Assert.IsType<ChatPendingAction>(vm.Chat.PendingAction);
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.ChatRestart], pending.ActionLabel);
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.ChatSessionEnded], pending.Text);
        Assert.True(pending.Action.CanExecute(null));
        Assert.Equal("Warning", vm.NeedTabState);
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.WizardTabNeedInterrupted], vm.NeedTabSubtitle);
    }

    [Fact]
    public async Task A_new_run_takes_back_the_resume_offer()
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
        var resume = Assert.IsType<ChatPendingAction>(vm.Chat.PendingAction);

        processes.WhileRunning = () => Assert.NotSame(resume, vm.Chat.PendingAction);
        await Compose(vm);

        Assert.NotSame(resume, vm.Chat.PendingAction);
    }

    [Fact]
    public void The_model_line_reads_the_default_first_then_the_pinned_profile()
    {
        var (vm, _, profiles) = Build();
        profiles.CommitEdit(
            new ModelProfile { Name = "Cloud", Provider = "Kimi", Model = "kimi-k2", BaseUrl = "https://api.moonshot.ai/v1" },
            previousName: null);

        Assert.True(vm.AdoptUsesDefaultLlm);
        Assert.Equal(
            EnglishStudioStrings.Instance.Format(StudioStringKeys.WizardAdoptModelDefault, profiles.DefaultProfileName ?? ""),
            vm.AdoptModelLine);

        vm.ToggleAdoptProfilePickerCommand.Execute(null);
        vm.PickAdoptProfileCommand.Execute("Cloud");

        Assert.False(vm.AdoptUsesDefaultLlm);
        Assert.Equal("Cloud", vm.AdoptModelLine);

        vm.ToggleAdoptProfilePickerCommand.Execute(null);
        vm.UseDefaultAdoptLlmCommand.Execute(null);

        Assert.True(vm.AdoptUsesDefaultLlm);
        Assert.False(vm.IsAdoptProfilePickerOpen);
        Assert.StartsWith(profiles.DefaultProfileName ?? "", vm.AdoptModelLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_engine_greys_compose_with_its_reason()
    {
        var (vm, _, _) = Build(cliInstalled: false);
        FillStepOne(vm);
        Assert.True(vm.CanCompose);
        Assert.False(vm.HasComposeTip);

        await Compose(vm);

        Assert.True(vm.IsEngineMissing);
        Assert.False(vm.CanCompose);
        Assert.False(vm.ComposeCommand.CanExecute(null));
        Assert.True(vm.HasComposeTip);
        Assert.Equal(EnglishStudioStrings.Instance[StudioStringKeys.WizardFailureEngineMissing], vm.ComposeTip);
        // One cause on screen: the diagnostic is the alert's primary, never a second button beside it.
        Assert.False(vm.FailureOffersDiagnosticSecondary);
    }
}
