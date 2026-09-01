using System.Globalization;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The card that fills the wait on steps 2 and 3, and the token meter it carries.
/// <para>
/// The defect it answers is the owner's screenshot of step 2 mid-compose: «Ajouter un agent»,
/// an empty consigne field and the technical log, with nothing at all to say whether the
/// engine was thinking or had died. Every test here stands for one thing the card must say —
/// or, as often, must refuse to say.
/// </para>
/// </summary>
public sealed class ComposeProgressTests
{
    private static ProcessOutputLine Out(string json) =>
        ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, json);

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

    [Fact]
    public void Nothing_is_shown_before_a_session_exists()
    {
        var (vm, _) = Build();

        Assert.False(vm.Progress.IsVisible);
        Assert.False(vm.Progress.IsWorking);
        Assert.False(vm.Progress.HasTokens);
    }

    /// <summary>
    /// The gap the owner photographed: step 2 is reached when the engine ENTERS the blueprint
    /// stage, which is well before blueprint.ready. What fills it is this sentence.
    /// </summary>
    [Fact]
    public async Task The_stage_the_engine_entered_is_named_while_it_works()
    {
        var (vm, processes) = Build();
        string title = "", detail = "";
        var working = false;
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(
                """{"v":2,"seq":3,"ts":"t","kind":"stage.entered","stage":"blueprint","iteration":1}"""));
            (title, detail, working) = (vm.Progress.Title, vm.Progress.Detail, vm.Progress.IsWorking);
        };

        await vm.ComposeCommand.ExecuteAsync();

        Assert.True(working);
        Assert.Equal("I am composing the team…", title);
        // No news is not the same as going well — the doctrine RunProgressViewModel already holds.
        Assert.Equal("Nothing reported yet.", detail);
    }

    [Fact]
    public async Task The_models_own_words_are_what_the_card_relays_never_the_users()
    {
        var (vm, processes) = Build();
        var detail = "";
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(
                """{"v":2,"seq":4,"ts":"t","kind":"assistant.message","text":"Je cherche les fichiers du dossier."}"""));
            detail = vm.Progress.Detail;
        };

        await vm.ComposeCommand.ExecuteAsync();

        Assert.Equal("Je cherche les fichiers du dossier.", detail);
    }

    /// <summary>
    /// The same lie the stepper's spinner told, one card lower: the engine child is alive
    /// while blocked on stdin, so «running» must not read as «working».
    /// </summary>
    [Fact]
    public async Task It_does_not_spin_while_the_engine_waits_on_you()
    {
        var (vm, processes) = Build();
        string title = "";
        bool working = true, waiting = false;
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(
                """{"v":2,"seq":3,"ts":"t","kind":"stage.entered","stage":"verdict","iteration":1}"""));
            processes.Emit(Out(
                """{"v":2,"seq":8,"ts":"t","kind":"decision.needed","options":["accept","retry","refine","abort"]}"""));
            (title, working, waiting) = (vm.Progress.Title, vm.Progress.IsWorking, vm.Progress.IsWaiting);
        };

        await vm.ComposeCommand.ExecuteAsync();

        Assert.False(working);
        Assert.True(waiting);
        Assert.Equal("I am waiting for your answer.", title);
    }

    [Fact]
    public async Task A_finished_session_stops_working_whatever_stage_it_died_in()
    {
        var (vm, processes) = Build();
        processes.WhileRunning = () =>
        {
            processes.Emit(Out(
                """{"v":2,"seq":3,"ts":"t","kind":"stage.entered","stage":"render","iteration":1}"""));
            processes.Emit(Out(
                """{"v":2,"seq":9,"ts":"t","kind":"session.finished","status":"paused","exitCode":0}"""));
        };

        await vm.ComposeCommand.ExecuteAsync();

        Assert.False(vm.Progress.IsWorking);
        Assert.Equal("Stopped.", vm.Progress.Title);
    }

    /// <summary>
    /// The owner's report, in substance: the up-and-down token counter never shows
    /// up. It never appeared because the protocol carried one grand total and no XAML
    /// bound even that.
    /// </summary>
    [Fact]
    public async Task The_two_directions_are_shown_apart()
    {
        var (vm, processes) = Build();
        processes.WhileRunning = () => processes.Emit(Out(
            """{"v":2,"seq":5,"ts":"t","kind":"cost.updated","tokens":14044,"promptTokens":12840,"completionTokens":1204}"""));

        await vm.ComposeCommand.ExecuteAsync();

        Assert.True(vm.Progress.HasTokens);
        // Grouped in the reader's own culture — the figures are for a human, not a parser.
        Assert.Equal(12840L.ToString("N0", CultureInfo.CurrentCulture), vm.Progress.TokensUp);
        Assert.Equal(1204L.ToString("N0", CultureInfo.CurrentCulture), vm.Progress.TokensDown);
        // Reported by the provider, so not marked as an approximation.
        Assert.False(vm.Progress.TokensEstimated);
    }

    [Fact]
    public async Task An_estimated_meter_says_that_it_is_one()
    {
        var (vm, processes) = Build();
        processes.WhileRunning = () => processes.Emit(Out(
            """{"v":2,"seq":5,"ts":"t","kind":"cost.updated","tokens":900,"promptTokens":700,"completionTokens":200,"estimatedTokens":900}"""));

        await vm.ComposeCommand.ExecuteAsync();

        Assert.True(vm.Progress.TokensEstimated);
        Assert.NotEmpty(vm.Progress.TokensNote);
    }

    /// <summary>
    /// A meter that has not moved shows nothing. «0 jetons» is a measurement, and no
    /// measurement was taken — the same rule the run's usage chips already follow (W-08).
    /// </summary>
    [Fact]
    public async Task A_meter_that_never_moved_shows_no_figure()
    {
        var (vm, processes) = Build();
        processes.WhileRunning = () => processes.Emit(Out(
            """{"v":2,"seq":3,"ts":"t","kind":"stage.entered","stage":"blueprint","iteration":1}"""));

        await vm.ComposeCommand.ExecuteAsync();

        Assert.False(vm.Progress.HasTokens);
        Assert.Empty(vm.Progress.TokensUp);
        Assert.Empty(vm.Progress.TokensDown);
    }

    [Fact]
    public async Task What_is_already_acquired_is_listed_and_nothing_else()
    {
        var (vm, processes) = Build();
        processes.WhileRunning = () =>
        {
            processes.Emit(Out("""{"v":2,"seq":6,"ts":"t","kind":"file.written","path":"crew/crew.yaml"}"""));
            processes.Emit(Out("""{"v":2,"seq":7,"ts":"t","kind":"file.written","path":"crew/agents.yaml"}"""));
            processes.Emit(Out("""{"v":2,"seq":8,"ts":"t","kind":"validation.result","ok":true,"errors":[]}"""));
        };

        await vm.ComposeCommand.ExecuteAsync();

        Assert.True(vm.Progress.HasFacts);
        Assert.Equal(["2 file(s) written", "definition checked"], vm.Progress.Facts);
    }

    [Fact]
    public async Task A_refused_definition_is_not_reported_as_a_checked_one()
    {
        var (vm, processes) = Build();
        processes.WhileRunning = () => processes.Emit(Out(
            """{"v":2,"seq":8,"ts":"t","kind":"validation.result","ok":false,"errors":["agents[0].role is required"]}"""));

        await vm.ComposeCommand.ExecuteAsync();

        Assert.Equal(["definition refused"], vm.Progress.Facts);
    }

    /// <summary>
    /// Which engine build answered, on the line that already names the assistant. Studio
    /// does not embed the CLI — it launches whichever <c>orkeon</c> its locator finds first —
    /// so a session driven by a stale binary otherwise looks exactly like a working one that
    /// happens to report nothing. That ambiguity is what this line removes.
    /// </summary>
    [Fact]
    public async Task The_engine_build_that_answered_is_named()
    {
        var (vm, processes) = Build();
        processes.WhileRunning = () => processes.Emit(Out(
            """{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false,"engine":"1.0.0-rc.2"}"""));

        await vm.ComposeCommand.ExecuteAsync();

        Assert.True(vm.HasEngineVersion);
        Assert.Equal("1.0.0-rc.2", vm.EngineVersion);
        Assert.Contains("1.0.0-rc.2", vm.EngineLabel, StringComparison.Ordinal);
    }

    /// <summary>An engine too old to announce itself says nothing rather than «engine ?».</summary>
    [Fact]
    public async Task An_engine_that_does_not_announce_itself_shows_no_version()
    {
        var (vm, processes) = Build();
        processes.WhileRunning = () => processes.Emit(Out(
            """{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""));

        await vm.ComposeCommand.ExecuteAsync();

        Assert.False(vm.HasEngineVersion);
        Assert.Empty(vm.EngineLabel);
    }
}
