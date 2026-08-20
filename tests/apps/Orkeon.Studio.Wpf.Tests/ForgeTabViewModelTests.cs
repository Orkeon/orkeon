using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Forge;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The "Résoudre" screen over a scripted engine child: the stream in, the cards out —
/// no real binary, no LLM, the inline dispatcher. The heavy protocol reading is pinned in
/// <c>Orkeon.Studio.Core.Tests</c>; these tests pin the screen's behaviour.
/// </summary>
public class ForgeTabViewModelTests
{
    private static ProcessOutputLine Out(string json) =>
        ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, json);

    private static (ForgeTabViewModel Vm, FakeProcessLauncher Processes, FakePathPicker Picker) Build(
        IReadOnlyList<ForgeSolutionSummary>? solutions = null)
    {
        var processes = new FakeProcessLauncher();
        var picker = new FakePathPicker();
        var client = new ForgeClient(
            processes,
            new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled()));
        var vm = new ForgeTabViewModel(
            client,
            picker,
            dispatcher: null,
            strings: null,
            workspaceDirectory: "/ws",
            catalog: _ => solutions ?? []);
        return (vm, processes, picker);
    }

    [Fact]
    public async Task Starting_a_session_launches_the_engine_and_projects_the_stream()
    {
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":1,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/ws/.orkeon/forge/veille","format":"yaml","resumed":false}"""),
            Out("""{"v":1,"seq":2,"ts":"t","kind":"stage.entered","stage":"brief","iteration":1}"""),
            Out("""{"v":1,"seq":3,"ts":"t","kind":"assistant.message","text":"Quel est l'objectif ?"}"""),
            Out("""{"v":1,"seq":4,"ts":"t","kind":"session.finished","status":"abandoned","exitCode":0}"""),
        ]);

        vm.NeedText = "je veux une veille fournisseur";
        await vm.StartCommand.ExecuteAsync();

        // The child got the CLI grammar, in the workspace.
        Assert.Equal(["forge", "je veux une veille fournisseur", "--events", "jsonl"],
            processes.LastRequest!.Arguments);
        Assert.Equal("/ws", processes.LastRequest.WorkingDirectory);

        // The screen switched to the session; the conversation carries both turns.
        Assert.True(vm.HasActiveSession);
        Assert.False(vm.IsStartPage);
        Assert.Equal(2, vm.Conversation.Messages.Count);
        Assert.True(vm.Conversation.Messages[0].IsUser);
        Assert.Equal("Quel est l'objectif ?", vm.Conversation.Messages[1].Text);
        Assert.Equal(ForgeMilestone.Describe, vm.Milestones.Current);
        Assert.Contains("Attempt 1", vm.Footer, StringComparison.Ordinal);
        Assert.Contains("pick it up again", vm.StatusMessage, StringComparison.Ordinal);
        Assert.False(vm.IsEngineRunning);
    }

    [Fact]
    public async Task A_reply_goes_down_stdin_as_a_protocol_line_and_echoes_in_the_thread()
    {
        var (vm, processes, _) = Build();
        processes.WhileRunning = () =>
        {
            Assert.True(vm.Conversation.CanSend);
            vm.Conversation.InputText = "exemple.fr, chaque matin";
            vm.Conversation.SendCommand.Execute(null);
        };

        vm.NeedText = "veille";
        await vm.StartCommand.ExecuteAsync();

        Assert.Contains("""{"kind":"user.message","text":"exemple.fr, chaque matin"}""", processes.InputLines);
        Assert.Contains(vm.Conversation.Messages, m => m.IsUser && m.Text == "exemple.fr, chaque matin");
        Assert.Equal("", vm.Conversation.InputText);
        Assert.False(vm.Conversation.CanSend);   // the child is gone; the box says so
    }

    [Fact]
    public async Task The_result_card_closes_the_loop_with_the_success_cards_words()
    {
        var (vm, processes, _) = Build();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":1,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
            Out("""{"v":1,"seq":2,"ts":"t","kind":"brief.ready","brief":{"goal":"Veille fournisseurs","acceptance":[{"id":"A1","statement":"Le résumé cite ses sources","kind":"must"}]}}"""),
            Out("""{"v":1,"seq":3,"ts":"t","kind":"stage.entered","stage":"verdict","iteration":1}"""),
            Out("""{"v":1,"seq":4,"ts":"t","kind":"verdict.ready","score":0.4,"passing":false,"findings":[{"id":"F1","severity":"major","acceptance":"A1","statement":"Les sources manquent"}],"suggestions":[],"judge":"llm"}"""),
            Out("""{"v":1,"seq":5,"ts":"t","kind":"decision.needed","options":["accept","refine","abort"]}"""),
        ]);

        vm.NeedText = "veille";
        await vm.StartCommand.ExecuteAsync();

        Assert.True(vm.Dossier.IsResultCard);
        var check = Assert.Single(vm.Dossier.Checklist);
        Assert.Equal("✘", check.Mark);
        Assert.Equal("Le résumé cite ses sources", check.Statement);
        Assert.Equal("Les sources manquent", check.Detail);
        Assert.Contains("(score 0.4)", vm.Dossier.ResultSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_ready_session_offers_adoption_and_store_promotes_into_a_picked_folder()
    {
        var (vm, processes, picker) = Build();
        picker.FolderToReturn = "/solutions";
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":1,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
            Out("""{"v":1,"seq":2,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
        ]);

        vm.NeedText = "veille";
        await vm.StartCommand.ExecuteAsync();

        Assert.Equal(ForgeMilestone.Adopt, vm.Milestones.Current);
        Assert.True(vm.Dossier.IsAdoptCard);
        Assert.True(vm.StoreCommand.CanExecute(null));

        // The adoption is a second child: forge promote, destination = picked folder + slug.
        processes.OutputToEmit.Clear();
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":1,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":true}"""),
            Out("""{"v":1,"seq":2,"ts":"t","kind":"promoted","path":"/solutions/veille","launcher":"run.sh","schedule":"schedule","install":"crontab hint"}"""),
            Out("""{"v":1,"seq":3,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
        ]);
        await vm.StoreCommand.ExecuteAsync();

        Assert.Equal(["forge", "promote", "veille", "--to", System.IO.Path.Combine("/solutions", "veille"), "--events", "jsonl"],
            processes.LastRequest!.Arguments);
        Assert.True(vm.Dossier.IsPromoted);
        Assert.Equal("/solutions/veille", vm.Dossier.PromotedPath);
        Assert.Equal("crontab hint", vm.Dossier.InstallCommand);
    }

    [Fact]
    public async Task Scheduling_passes_the_daily_spec_through_verbatim()
    {
        var (vm, processes, picker) = Build();
        picker.FolderToReturn = "/solutions";
        processes.OutputToEmit.AddRange(
        [
            Out("""{"v":1,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/d","format":"yaml","resumed":false}"""),
            Out("""{"v":1,"seq":2,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
        ]);
        vm.NeedText = "veille";
        await vm.StartCommand.ExecuteAsync();

        processes.OutputToEmit.Clear();
        vm.Dossier.ScheduleTime = "07:30";
        await vm.ScheduleCommand.ExecuteAsync();

        Assert.Contains("--schedule", processes.LastRequest!.Arguments);
        Assert.Contains("daily@07:30", processes.LastRequest.Arguments);
    }

    [Fact]
    public void The_solutions_list_reads_the_catalog_and_the_example_fills_the_need_box()
    {
        var (vm, _, _) = Build(
        [
            new ForgeSolutionSummary
            {
                Slug = "veille", Title = "Veille fournisseurs", State = "Promoted", Status = "Promoted",
                PromotedTo = "/solutions/veille", Directory = "/d/veille",
            },
            new ForgeSolutionSummary
            {
                Slug = "rapport", Title = null, State = "Test", Status = "Active", Directory = "/d/rapport",
            },
        ]);
        vm.Solutions.Refresh();

        Assert.False(vm.Solutions.IsEmpty);
        Assert.Equal(["Veille fournisseurs", "rapport"], vm.Solutions.Solutions.Select(s => s.Title));
        Assert.True(vm.Solutions.Solutions[0].CanRelaunch);
        Assert.True(vm.Solutions.Solutions[1].CanResume);

        vm.UseExampleCommand.Execute(vm.Examples[0]);
        Assert.Equal("Summarize a site's news every morning", vm.NeedText);
        Assert.True(vm.StartCommand.CanExecute(null));
    }

    [Fact]
    public void Relaunch_hands_the_adopted_folder_to_the_launcher()
    {
        var mainProcesses = new FakeProcessLauncher();
        var runner = new OrkeonProcessRunner(
            mainProcesses, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled()));
        var forgeClient = new ForgeClient(
            new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled()));
        var shell = new MainWindowViewModel(
            settingsStore: new FakeAppSettingsStore(),
            directories: new FakeDirectoryProbe(),
            targetProbe: new FakeTargetProbe(),
            picker: new FakePathPicker(),
            processRunner: runner,
            historyStore: new FakeLaunchHistoryStore(),
            forgeClient: forgeClient,
            forgeWorkspace: "/ws");

        var adopted = new ForgeSolutionViewModel(new ForgeSolutionSummary
        {
            Slug = "veille", Title = "Veille", State = "Promoted", Status = "Promoted",
            PromotedTo = "/solutions/veille", Directory = "/d/veille",
        });

        shell.Forge.RelaunchCommand.Execute(adopted);

        // The promoted folder is an ordinary target: the launcher receives it as-is.
        Assert.Equal("/solutions/veille", shell.Launch.Target.SelectedPath);
    }
}
