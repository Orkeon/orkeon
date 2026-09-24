using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Core.UseCases;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Wpf.ViewModels.Shell;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-41: « Import as is » on a gallery card, over a scripted <c>orkeon usecases</c> that writes
/// the team folder <c>export</c> writes, into a teams root on the real disk — the expert's action
/// only, never on a reference-only case; a name already taken answered by the adoption's own rule;
/// and the team that lands named, described, modifiable, and launched with its data mounted.
/// </summary>
public partial class CreateTeamWizardTests
{
    private const string EmailTriageTeam = "Tri et réponse aux e-mails";

    private static readonly IReadOnlyList<string> EmailTriageMounts = ["./data:/data:ro", "./output:/output:rw"];

    /// <summary>The scripted CLI, its first case — the e-mail triage — mounting its data and its output.</summary>
    private static FakeUseCaseCli ExportingCli()
    {
        var cli = UseCaseCli();
        cli.Catalog[0] = EmailTriage with { Mounts = EmailTriageMounts };
        return cli;
    }

    /// <summary>The wizard over <paramref name="cli"/>, its teams in <paramref name="teamsRoot"/>, the window's switch in <paramref name="mode"/>.</summary>
    private static CreateTeamViewModel WizardForImport(
        FakeUseCaseCli cli, string teamsRoot, UiModeViewModel mode, string language = "fr")
    {
        var document = AppSettingsDocument.CreateEmpty();
        var llm = new LlmSectionViewModel(() => document, () => { }, new FakeLlmEndpointProbe());
        var profiles = new ModelProfilesViewModel(new InMemoryModelProfileStore(), llm, probe: new FakeLlmEndpointProbe());
        var locator = new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled());

        return new CreateTeamViewModel(
            profiles,
            new CreateTeamDependencies
            {
                Client = new Orkeon.Studio.Core.Forge.ForgeClient(new FakeProcessLauncher(), locator),
                WorkspaceDirectory = "/ws",
                TeamsRoot = teamsRoot,
                UseCases = new UseCaseClient(cli, locator),
                UiLanguage = () => language,
                Mode = mode,
            });
    }

    private static string TeamsRootForTest() =>
        Path.Combine(Path.GetTempPath(), $"orkeon-import-as-is-{Guid.NewGuid():N}", "teams");

    private static void DeleteTestRoot(string teamsRoot)
    {
        var root = Path.GetDirectoryName(teamsRoot)!;
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private static async Task<CreateTeamViewModel> OpenGallery(FakeUseCaseCli cli, string teamsRoot, UiModeViewModel mode)
    {
        var vm = WizardForImport(cli, teamsRoot, mode);
        await vm.Gallery.LoadAsync(TestContext.Current.CancellationToken);
        vm.BrowseUseCasesCommand.Execute(null);
        return vm;
    }

    private static UseCaseCardViewModel Card(CreateTeamViewModel vm, string id) =>
        vm.Gallery.Cards.Single(card => card.Id == id);

    [Fact]
    public async Task Import_as_is_is_offered_in_expert_mode_only_and_never_on_a_reference_only_case()
    {
        var cli = ExportingCli();
        var mode = new UiModeViewModel(UiModeViewModel.Novice);
        var vm = await OpenGallery(cli, "/teams", mode);

        Assert.All(vm.Gallery.Cards, card => Assert.False(card.CanImportAsIs));
        Assert.False(Card(vm, "03-email-pipeline").ImportAsIsCommand.CanExecute(null));

        mode.Mode = UiModeViewModel.Expert;

        Assert.True(Card(vm, "03-email-pipeline").CanImportAsIs);
        Assert.True(Card(vm, "03-email-pipeline").ImportAsIsCommand.CanExecute(null));
        // A reference-only case (D-03) stays browsable and usable as a reference, never importable.
        Assert.False(Card(vm, "31-algo-trading").CanImportAsIs);
        Assert.False(Card(vm, "31-algo-trading").ImportAsIsCommand.CanExecute(null));

        mode.Mode = UiModeViewModel.Novice;

        Assert.All(vm.Gallery.Cards, card => Assert.False(card.CanImportAsIs));
        Assert.Empty(cli.Exports);
    }

    [Fact]
    public async Task Importing_a_case_as_is_makes_a_team_with_its_name_its_description_and_modify_active()
    {
        var teamsRoot = TeamsRootForTest();
        try
        {
            var cli = ExportingCli();
            var vm = await OpenGallery(cli, teamsRoot, new UiModeViewModel(UiModeViewModel.Expert));
            var imported = new List<string>();
            vm.Gallery.Import.TeamImported += (_, e) => imported.Add(e.Path);

            await Card(vm, "03-email-pipeline").ImportAsIsCommand.ExecuteAsync();

            // Exported in the window's language, staged under the very name the team's folder takes.
            var staged = Assert.Single(cli.Exports);
            Assert.Equal("tri-et-reponse-aux-e-mails", Path.GetFileName(staged));
            Assert.Contains(cli.Requests, request => request.Arguments is ["usecases", "export", "03-email-pipeline", "--to", _, "--lang", "fr", "--events", "jsonl"]);
            Assert.False(Directory.Exists(Path.GetDirectoryName(staged)));

            var team = Path.Combine(teamsRoot, "tri-et-reponse-aux-e-mails");
            Assert.Equal([team], imported);

            // The adoption's own line, then the Import screen's report on the folders.
            var import = vm.Gallery.Import;
            Assert.True(import.HasImported);
            Assert.False(import.IsImporting);
            Assert.Equal($"Team “{EmailTriageTeam}” is saved in My teams.", import.ImportedLine);
            Assert.Equal("Declared folders", import.FoldersReport!.Title);
            Assert.Equal("2 folder(s): /data (read), /output (read, write)", import.FoldersReport.Detail);

            // My teams: a card with a name, a description and « Modify » active (D-04).
            var teams = new TeamsViewModel(new TeamsDependencies { TeamsRoot = teamsRoot, LoadSessions = () => [] });
            var card = Assert.Single(teams.Teams);
            Assert.Equal(EmailTriageTeam, card.Name);
            Assert.Equal("Trier mes e-mails et préparer les réponses", card.Description);
            Assert.True(card.CanModify);
            Assert.True(card.ModifyCommand.CanExecute(null));
            Assert.Equal(["/data (read)", "/output (read, write)"], card.MountChips.Select(chip => chip.Label));
        }
        finally
        {
            DeleteTestRoot(teamsRoot);
        }
    }

    [Fact]
    public async Task The_imported_team_launches_with_its_data_mounted_from_its_own_folder()
    {
        var teamsRoot = TeamsRootForTest();
        try
        {
            var vm = await OpenGallery(ExportingCli(), teamsRoot, new UiModeViewModel(UiModeViewModel.Expert));
            await Card(vm, "03-email-pipeline").ImportAsIsCommand.ExecuteAsync();
            var team = Path.Combine(teamsRoot, "tri-et-reponse-aux-e-mails");

            var tab = new LaunchTabViewModel(new LaunchTabDependencies
            {
                ProcessRunner = new OrkeonProcessRunner(
                    new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                TargetProbe = PhysicalTargetProbe.Instance,
                Directories = PhysicalDirectoryProbe.Instance,
                HistoryStore = new FakeLaunchHistoryStore(),
                SettingsStore = new FakeAppSettingsStore(),
                DeclaredMounts = () => [],
            });
            tab.Target.Select(team);

            var arguments = tab.BuildArguments().ToList();
            Assert.Contains(Path.Combine(team, "crew", "config.yaml"), arguments);
            var mountIndex = arguments.IndexOf("--mount");
            Assert.True(mountIndex >= 0);
            var mounts = arguments.Skip(mountIndex + 1).TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
            // ./data resolved under the team, never spelled relative: the runtime reads it against its own cwd.
            Assert.Equal([$"{Path.Combine(team, "data")}:/data:ro", $"{Path.Combine(team, "output")}:/output:rw"], mounts);
            Assert.True(File.Exists(Path.Combine(team, "data", "sample.csv")));
            // The team's own folders are vouched for by living inside it: nothing blocks the run.
            Assert.False(tab.IsBlockedByUndeclaredFolders);
        }
        finally
        {
            DeleteTestRoot(teamsRoot);
        }
    }

    [Fact]
    public async Task A_taken_name_gets_the_free_name_proposal_and_nothing_is_exported_until_it_is_taken()
    {
        var teamsRoot = TeamsRootForTest();
        try
        {
            var taken = Path.Combine(teamsRoot, "tri-et-reponse-aux-e-mails");
            TeamCatalog.SaveMetadata(taken, new StudioTeamMetadata { Name = EmailTriageTeam });
            var cli = ExportingCli();
            var vm = await OpenGallery(cli, teamsRoot, new UiModeViewModel(UiModeViewModel.Expert));

            await Card(vm, "03-email-pipeline").ImportAsIsCommand.ExecuteAsync();

            var import = vm.Gallery.Import;
            Assert.Empty(cli.Exports);
            Assert.True(import.HasConflict);
            Assert.Equal(
                $"This name is taken: the folder tri-et-reponse-aux-e-mails in My teams already holds the team “{EmailTriageTeam}”.",
                import.Conflict);
            Assert.Equal($"{EmailTriageTeam} (2)", import.FreeName);
            Assert.Equal($"Import as “{EmailTriageTeam} (2)”", import.UseFreeNameLabel);
            Assert.True(import.CanOpenConflictingTeam);

            await import.UseFreeNameCommand.ExecuteAsync();

            var team = Path.Combine(teamsRoot, "tri-et-reponse-aux-e-mails-2");
            Assert.Equal("tri-et-reponse-aux-e-mails-2", Path.GetFileName(Assert.Single(cli.Exports)));
            Assert.Equal($"{EmailTriageTeam} (2)", TeamCatalog.Describe(team).Name);
            Assert.False(import.HasConflict);
            Assert.Equal($"Team “{EmailTriageTeam} (2)” is saved in My teams.", import.ImportedLine);
            // The team that held the name is untouched.
            Assert.Equal(EmailTriageTeam, TeamCatalog.Describe(taken).Name);
        }
        finally
        {
            DeleteTestRoot(teamsRoot);
        }
    }

    [Fact]
    public async Task The_way_to_the_team_holding_the_name_and_to_the_imported_one_is_my_teams()
    {
        var teamsRoot = TeamsRootForTest();
        try
        {
            var taken = Path.Combine(teamsRoot, "tri-et-reponse-aux-e-mails");
            TeamCatalog.SaveMetadata(taken, new StudioTeamMetadata { Name = EmailTriageTeam });
            var vm = await OpenGallery(ExportingCli(), teamsRoot, new UiModeViewModel(UiModeViewModel.Expert));
            var opened = new List<string>();
            vm.OpenTeamRequested += (_, e) => opened.Add(e.Path);

            await Card(vm, "03-email-pipeline").ImportAsIsCommand.ExecuteAsync();
            vm.Gallery.Import.OpenConflictingTeamCommand.Execute(null);

            Assert.Equal([taken], opened);
            Assert.False(vm.Gallery.IsOpen);

            vm.BrowseUseCasesCommand.Execute(null);
            // A new visit of the gallery starts without the last one's banner.
            Assert.False(vm.Gallery.Import.HasBanner);
            await Card(vm, "56-adaptive-tutor").ImportAsIsCommand.ExecuteAsync();
            vm.Gallery.Import.OpenTeamsCommand.Execute(null);

            Assert.Equal([taken, Path.Combine(teamsRoot, "tutorat-personnalise")], opened);
            Assert.False(vm.Gallery.IsOpen);
        }
        finally
        {
            DeleteTestRoot(teamsRoot);
        }
    }

    [Fact]
    public async Task An_export_the_cli_refuses_is_said_in_its_words_and_leaves_no_team()
    {
        var teamsRoot = TeamsRootForTest();
        try
        {
            var cli = ExportingCli();
            cli.ExportRefusal = ("USECASES-DESTINATION-NOT-EMPTY", "the folder is not empty");
            var vm = await OpenGallery(cli, teamsRoot, new UiModeViewModel(UiModeViewModel.Expert));

            await Card(vm, "03-email-pipeline").ImportAsIsCommand.ExecuteAsync();

            var import = vm.Gallery.Import;
            Assert.True(import.HasFailure);
            Assert.Equal("Not imported — USECASES-DESTINATION-NOT-EMPTY: the folder is not empty", import.Failure);
            Assert.False(import.HasImported);
            Assert.Empty(TeamCatalog.List(teamsRoot));
            Assert.False(Directory.Exists(Path.GetDirectoryName(Assert.Single(cli.Exports))));

            import.DismissCommand.Execute(null);

            Assert.False(import.HasBanner);
        }
        finally
        {
            DeleteTestRoot(teamsRoot);
        }
    }

    /// <summary>
    /// Through the window: the gallery reads the window's own expert switch, and a case imported
    /// from it lands in My teams at once — the list refreshes on the import, as on an adoption.
    /// </summary>
    [Fact]
    public async Task Through_the_window_an_imported_case_is_in_my_teams_at_once()
    {
        var teamsRoot = TeamsRootForTest();
        try
        {
            var cli = ExportingCli();
            var shell = new MainWindowViewModel(
                new Orkeon.Studio.Wpf.ViewModels.Services.StudioServices
                {
                    SettingsStore = new FakeAppSettingsStore(),
                    Directories = new FakeDirectoryProbe(),
                    TargetProbe = new FakeTargetProbe(),
                    Picker = new FakePathPicker(),
                    ProcessRunner = new OrkeonProcessRunner(cli, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                    HistoryStore = new FakeLaunchHistoryStore(),
                },
                new Orkeon.Studio.Wpf.ViewModels.Services.StudioUiPreferences { InitialMode = UiModeViewModel.Expert, InitialLanguage = "fr" },
                globalPathOverride: "/home/user/.config/Orkeon/appsettings.json",
                forgeWorkspace: Path.Combine(Path.GetDirectoryName(teamsRoot)!, "forge"),
                teamsRoot: teamsRoot);
            await shell.InitializeAsync(TestContext.Current.CancellationToken);
            Assert.Empty(shell.Teams.Teams);

            shell.CreateTeam.BrowseUseCasesCommand.Execute(null);
            await shell.CreateTeam.Gallery.Cards.Single(card => card.Id == "03-email-pipeline").ImportAsIsCommand.ExecuteAsync();

            var card = Assert.Single(shell.Teams.Teams);
            Assert.Equal(EmailTriageTeam, card.Name);
            Assert.True(card.CanModify);
        }
        finally
        {
            DeleteTestRoot(teamsRoot);
        }
    }
}
