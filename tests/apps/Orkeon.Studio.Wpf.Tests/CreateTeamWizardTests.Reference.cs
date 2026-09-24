using System.Text.Json.Nodes;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Core.UseCases;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-40: the use case a creation starts from reaches the engine as <c>forge --reference</c>,
/// and comes back from the session — kept in its <c>session.json</c> (D-01) — when the wizard
/// resumes it or reopens its team.
/// </summary>
public partial class CreateTeamWizardTests
{
    /// <summary>The <c>reference</c> a session composed from the e-mail case records.</summary>
    private const string EmailReference = """{"id":"03-email-pipeline","title":"Email triage and replies"}""";

    [Fact]
    public async Task Composing_from_a_chosen_case_hands_its_id_to_the_engine()
    {
        var (vm, processes) = WizardComposingWithGallery();
        await vm.Gallery.LoadAsync(TestContext.Current.CancellationToken);
        vm.BrowseUseCasesCommand.Execute(null);
        vm.Gallery.Cards.Single(card => card.Id == "03-email-pipeline").ChooseCommand.Execute(null);
        AnswerTheStepOneChoices(vm);

        await vm.ComposeCommand.ExecuteAsync();

        var argv = processes.Requests[0].Arguments.ToList();
        Assert.Equal("forge", argv[0]);
        Assert.Equal("03-email-pipeline", argv[argv.IndexOf("--reference") + 1]);
        Assert.Contains("--dry", argv);
    }

    [Fact]
    public async Task Composing_without_a_case_or_with_its_chip_removed_names_no_reference()
    {
        var (vm, processes) = WizardComposingWithGallery();
        await vm.Gallery.LoadAsync(TestContext.Current.CancellationToken);
        vm.BrowseUseCasesCommand.Execute(null);
        vm.Gallery.Cards.Single(card => card.Id == "03-email-pipeline").ChooseCommand.Execute(null);
        vm.RemoveReferenceUseCaseCommand.Execute(null);
        AnswerTheStepOneChoices(vm);

        await vm.ComposeCommand.ExecuteAsync();

        Assert.DoesNotContain("--reference", processes.Requests[0].Arguments);
    }

    /// <summary>
    /// A resume reads the reference back from the session instead of dropping it with the
    /// previous creation's: the chip names the case again. A session composed from nothing takes
    /// away the reference another creation had attached.
    /// </summary>
    [Fact]
    public async Task A_resume_reads_the_reference_back_from_the_session()
    {
        var root = Path.Combine(Path.GetTempPath(), "orkeon-wiz-reference-" + Guid.NewGuid().ToString("N"));
        var referenced = await WritePausedSession(root, "trier", EmailReference);
        var plain = await WritePausedSession(root, "veille", reference: null);
        try
        {
            var (vm, processes) = WizardComposingWithGallery();
            await vm.Gallery.LoadAsync(TestContext.Current.CancellationToken);

            await vm.ResumeAsync(new ForgeSolutionSummary { Slug = "trier", State = "Test", Status = "Active", Directory = referenced });

            Assert.Empty(processes.Requests);   // the dry pause reopens without an engine
            Assert.Equal("03-email-pipeline", vm.ReferenceUseCaseId);
            Assert.True(vm.HasReferenceUseCase);
            Assert.Equal("Inspired by: Tri et réponse aux e-mails", vm.ReferenceUseCaseLabel);

            await vm.ResumeAsync(new ForgeSolutionSummary { Slug = "veille", State = "Test", Status = "Active", Directory = plain });

            Assert.Null(vm.ReferenceUseCaseId);
            Assert.False(vm.HasReferenceUseCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>« Modify » on a team: the session the engine names for the folder brings its reference back too.</summary>
    [Fact]
    public async Task A_reopen_reads_the_reference_back_from_the_session()
    {
        var (root, teamDir, sessionDir) = await WriteReopenableTeam("[]");
        var sessionFile = Path.Combine(sessionDir, "session.json");
        var session = JsonNode.Parse(await File.ReadAllTextAsync(sessionFile, TestContext.Current.CancellationToken))!.AsObject();
        session["reference"] = JsonNode.Parse(EmailReference);
        await File.WriteAllTextAsync(sessionFile, session.ToJsonString(), TestContext.Current.CancellationToken);
        try
        {
            var (vm, processes, _) = Build(teamsRoot: Path.Combine(root, "teams"));
            processes.NextRuns.Enqueue(FoundStream(sessionDir, teamDir));
            processes.OutputToEmit.AddRange(
            [
                Out($$"""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":{{System.Text.Json.JsonSerializer.Serialize(sessionDir)}},"format":"yaml","resumed":true}"""),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);

            await vm.ReopenTeamAsync(TeamCatalog.Describe(teamDir));

            Assert.Equal(["forge", "reopen", teamDir, "--events", "jsonl"], processes.Requests[0].Arguments);
            Assert.Equal("03-email-pipeline", vm.ReferenceUseCaseId);
            // The session keeps its reference: the resume that follows never names one.
            Assert.DoesNotContain("--reference", processes.Requests[1].Arguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>The gallery wizard of STUDIO-39, with an assistant profile to compose and its engine child scripted.</summary>
    private static (CreateTeamViewModel Vm, FakeProcessLauncher Processes) WizardComposingWithGallery()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var llm = new LlmSectionViewModel(() => document, () => { }, new FakeLlmEndpointProbe());
        var profiles = new ModelProfilesViewModel(new InMemoryModelProfileStore(), llm, probe: new FakeLlmEndpointProbe());
        profiles.CommitEdit(
            new ModelProfile { Name = "Local", Provider = "Ollama", Model = "qwen2.5:14b", BaseUrl = "http://localhost:11434/v1" },
            previousName: null);
        profiles.StudioProfileName = "Local";

        var locator = new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled());
        var processes = new FakeProcessLauncher();
        var vm = new CreateTeamViewModel(
            profiles,
            new CreateTeamDependencies
            {
                Client = new ForgeClient(processes, locator),
                WorkspaceDirectory = "/ws",
                TeamsRoot = "/teams",
                UseCases = new UseCaseClient(UseCaseCli(), locator),
                UiLanguage = () => "fr",
            });
        return (vm, processes);
    }

    /// <summary>The three choices step 1 still asks for once a case wrote the need.</summary>
    private static void AnswerTheStepOneChoices(CreateTeamViewModel vm)
    {
        vm.FrequencyChoices[1].SelectCommand.Execute(null);
        vm.SourceChoices[0].SelectCommand.Execute(null);
        vm.OutputChoices[0].SelectCommand.Execute(null);
        Assert.True(vm.CanCompose);
    }

    /// <summary>A session at the dry pause under <paramref name="root"/>, its <c>reference</c> written as given.</summary>
    private static async Task<string> WritePausedSession(string root, string slug, string? reference)
    {
        var directory = Path.Combine(root, ".orkeon", "forge", slug);
        Directory.CreateDirectory(directory);
        var recorded = reference is null ? "" : $$""","reference":{{reference}}""";
        await File.WriteAllTextAsync(Path.Combine(directory, "session.json"),
            $$"""{"v":1,"slug":"{{slug}}","format":"yaml"{{recorded}},"state":"Test","status":"Active"}""",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "blueprint.json"),
            """{"crew":{"name":"trier"},"agents":[{"key":"a","role":"A","tools":[]}],"tasks":[{"key":"t","description":"d","agent":"a"}]}""",
            TestContext.Current.CancellationToken);
        return directory;
    }
}
