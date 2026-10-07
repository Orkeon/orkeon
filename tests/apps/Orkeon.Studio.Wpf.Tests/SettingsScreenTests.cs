using System.Globalization;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The unified settings screen (design v3): the model profiles, their two elections, the
/// mirror of the default into the Llm section, and the tab gating by mode.
/// </summary>
public sealed class SettingsScreenTests
{
    // ── STUDIO-61: the « Teams folder » card of Settings › Studio ──

    private static readonly string WorkshopTeams = Path.Combine(Path.GetTempPath(), "orkeon-ws", "teams");
    private static readonly string PickedTeams = Path.Combine(Path.GetTempPath(), "orkeon-picked", "teams");

    private static string English(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, EnglishStudioStrings.Instance[key], arguments);

    [Fact]
    public void The_teams_folder_card_says_the_path_and_its_source()
    {
        var variable = new StudioSettingsViewModel(
            teamsRoot: new TeamsRootResolution(WorkshopTeams, TeamsRootSource.Environment, null, null));
        Assert.Equal(WorkshopTeams, variable.TeamsRootPath);
        Assert.Equal(English(StudioStringKeys.SettingsTeamsRootSourceEnvironment, TeamsRootLocator.EnvironmentVariable), variable.TeamsRootSourceText);
        Assert.Contains("ORKEON_STUDIO_TEAMS_ROOT", variable.TeamsRootSourceText, StringComparison.Ordinal);
        Assert.False(variable.HasTeamsRootIgnored);
        Assert.Null(variable.TeamsRootIgnoredText);

        var option = new StudioSettingsViewModel(
            teamsRoot: new TeamsRootResolution(WorkshopTeams, TeamsRootSource.Argument, null, null));
        Assert.Contains("--teams-root", option.TeamsRootSourceText, StringComparison.Ordinal);

        var preference = new StudioSettingsViewModel(
            teamsRoot: new TeamsRootResolution(WorkshopTeams, TeamsRootSource.Preference, null, null));
        Assert.Equal(English(StudioStringKeys.SettingsTeamsRootSourcePreference), preference.TeamsRootSourceText);

        var nothing = new StudioSettingsViewModel();
        Assert.Equal(TeamCatalog.DefaultRoot(), nothing.TeamsRootPath);
        Assert.Equal(English(StudioStringKeys.SettingsTeamsRootSourceDefault), nothing.TeamsRootSourceText);
        Assert.True(nothing.ChangeTeamsRootCommand.CanExecute(null));
    }

    [Fact]
    public void Change_asks_the_folder_dialog_and_writes_the_preference_for_the_next_start()
    {
        var persisted = new List<StudioSettings>();
        var picker = new FakePathPicker { FolderToReturn = PickedTeams + Path.DirectorySeparatorChar };
        var studio = new StudioSettingsViewModel(
            new BalanceReadings(settings: StudioSettings.Default with { BalanceRefreshMinutes = 15 }),
            persisted.Add,
            teamsRoot: new TeamsRootResolution(WorkshopTeams, TeamsRootSource.Preference, null, null),
            picker: picker);

        Assert.True(studio.ChangeTeamsRootCommand.CanExecute(null));
        studio.ChangeTeamsRootCommand.Execute(null);

        Assert.Equal([English(StudioStringKeys.SettingsTeamsRootTitle)], picker.Prompts);
        var written = Assert.Single(persisted);
        Assert.Equal(PickedTeams, written.TeamsRoot);
        // Written by merge with the rest of Settings › Studio, never alone.
        Assert.Equal(15, written.BalanceRefreshMinutes);
        Assert.Same(written, studio.Current);
        // The card shows the choice; the root in force stays until the next start.
        Assert.Equal(PickedTeams, studio.TeamsRootPath);
        Assert.Equal(English(StudioStringKeys.SettingsTeamsRootSourcePreference), studio.TeamsRootSourceText);
        Assert.Equal(WorkshopTeams, studio.TeamsRoot.Path);
    }

    [Fact]
    public void A_cancelled_dialog_writes_nothing()
    {
        var persisted = new List<StudioSettings>();
        var studio = new StudioSettingsViewModel(persist: persisted.Add, picker: new FakePathPicker());

        studio.ChangeTeamsRootCommand.Execute(null);

        Assert.Empty(persisted);
        Assert.Equal(TeamCatalog.DefaultRoot(), studio.TeamsRootPath);
    }

    [Fact]
    public void When_the_variable_or_the_option_holds_the_root_the_command_is_off_and_the_card_says_which()
    {
        var persisted = new List<StudioSettings>();
        var picker = new FakePathPicker { FolderToReturn = PickedTeams };

        foreach (var source in new[] { TeamsRootSource.Environment, TeamsRootSource.Argument })
        {
            var studio = new StudioSettingsViewModel(
                persist: persisted.Add,
                teamsRoot: new TeamsRootResolution(WorkshopTeams, source, null, null),
                picker: picker);

            Assert.False(studio.CanChangeTeamsRoot);
            Assert.False(studio.ChangeTeamsRootCommand.CanExecute(null));
            studio.ChangeTeamsRootCommand.Execute(null);
            Assert.Contains(
                source == TeamsRootSource.Environment ? TeamsRootLocator.EnvironmentVariable : StartupArguments.TeamsRootSwitch,
                studio.TeamsRootSourceText,
                StringComparison.Ordinal);
        }

        Assert.Empty(persisted);
        Assert.Empty(picker.Prompts);
    }

    [Fact]
    public void A_value_ignored_at_startup_is_named_with_its_reason()
    {
        var studio = new StudioSettingsViewModel(
            teamsRoot: new TeamsRootResolution(
                WorkshopTeams, TeamsRootSource.Argument, "my-workshop/teams", "ORKEON_STUDIO_TEAMS_ROOT is not an absolute path."));

        Assert.True(studio.HasTeamsRootIgnored);
        Assert.Equal(
            English(StudioStringKeys.SettingsTeamsRootIgnored, "my-workshop/teams", "ORKEON_STUDIO_TEAMS_ROOT is not an absolute path."),
            studio.TeamsRootIgnoredText);
    }

    private static (ModelProfilesViewModel Profiles, LlmSectionViewModel Llm, InMemoryModelProfileStore Store, AppSettingsDocument Document) Build() =>
        Build(new FakeApiKeyStore(), new FakeLlmEndpointProbe());

    private static (ModelProfilesViewModel Profiles, LlmSectionViewModel Llm, InMemoryModelProfileStore Store, AppSettingsDocument Document) Build(
        IApiKeyStore keyStore, FakeLlmEndpointProbe probe)
    {
        var document = AppSettingsDocument.CreateEmpty();
        var llm = new LlmSectionViewModel(() => document, () => { });
        var store = new InMemoryModelProfileStore();
        var profiles = new ModelProfilesViewModel(store, llm, probe: probe, keyStore: keyStore);
        return (profiles, llm, store, document);
    }

    private static ModelProfile Ollama(string name) =>
        new() { Name = name, Provider = "Ollama", Model = "qwen2.5:14b", BaseUrl = "http://localhost:11434/v1" };

    // ── profiles ──

    [Fact]
    public async Task Creating_the_first_profile_elects_it_and_mirrors_it_into_the_llm_section()
    {
        var (profiles, _, store, document) = Build();

        profiles.NewProfileCommand.Execute(null);
        profiles.Editor!.Name = "Local rapide";
        profiles.Editor.SaveCommand.Execute(null);

        Assert.Null(profiles.Editor);
        Assert.Equal("Local rapide", profiles.DefaultProfileName);
        // The first preset seeded the endpoint; electing the default writes it to the document.
        Assert.NotNull(document.Llm.Model);
        Assert.NotNull(document.Llm.BaseUrl);
        var persisted = (await store.LoadAsync(TestContext.Current.CancellationToken)).Set;
        Assert.Single(persisted.Profiles);
    }

    [Fact]
    public void Electing_a_default_rewrites_the_llm_section_to_that_profile()
    {
        var (profiles, _, _, document) = Build();
        profiles.CommitEdit(Ollama("Local"), previousName: null);
        profiles.CommitEdit(
            new ModelProfile { Name = "Cloud", Provider = "OpenAI", Model = "gpt-4.1-mini", BaseUrl = "https://api.openai.com/v1" },
            previousName: null);

        profiles.SetDefault("Cloud");

        Assert.Equal("Cloud", profiles.DefaultProfileName);
        Assert.Equal("gpt-4.1-mini", document.Llm.Model);
        Assert.Equal("https://api.openai.com/v1", document.Llm.BaseUrl);
    }

    [Fact]
    public void The_assistants_profile_is_a_separate_election_that_gates_nothing_else()
    {
        var (profiles, _, _, _) = Build();
        profiles.CommitEdit(Ollama("Local"), previousName: null);

        Assert.False(profiles.HasStudioProfile);

        profiles.StudioProfileName = "Local";

        Assert.True(profiles.HasStudioProfile);
        Assert.Equal("Local", profiles.Set.Studio?.Name);
    }

    [Fact]
    public void The_editor_refuses_a_name_another_profile_already_bears()
    {
        var (profiles, _, _, _) = Build();
        profiles.NewProfileCommand.Execute(null);
        profiles.Editor!.Name = "Local";
        profiles.Editor.SaveCommand.Execute(null);

        // A second profile cannot take the same identity…
        profiles.NewProfileCommand.Execute(null);
        profiles.Editor!.Name = "Local";
        Assert.True(profiles.Editor.NameCollision);
        Assert.False(profiles.Editor.CanSave);
        profiles.Editor.SaveCommand.Execute(null);
        Assert.NotNull(profiles.Editor);   // still open: nothing was overwritten

        // …but reopening a profile under its own name is not a collision.
        profiles.CancelEdit();
        profiles.Profiles[0].EditCommand.Execute(null);
        Assert.False(profiles.Editor!.NameCollision);
        Assert.True(profiles.Editor.CanSave);
    }

    [Fact]
    public void Deleting_is_refused_on_the_last_profile()
    {
        var (profiles, _, _, _) = Build();
        profiles.CommitEdit(Ollama("Only"), previousName: null);

        Assert.False(profiles.CanDelete);
        Assert.False(profiles.Profiles[0].DeleteCommand.CanExecute(null));
    }

    [Fact]
    public void Duplication_appends_a_uniquely_named_copy()
    {
        var (profiles, _, _, _) = Build();
        profiles.CommitEdit(Ollama("Local"), previousName: null);

        profiles.Profiles[0].DuplicateCommand.Execute(null);

        Assert.Equal(2, profiles.Profiles.Count);
        Assert.Equal("Local (copy)", profiles.Profiles[1].Name);
    }

    [Fact]
    public async Task The_editor_probes_the_endpoint_and_reports_the_answer()
    {
        var (profiles, _, _, _) = Build();
        profiles.BeginEdit(Ollama("Local"));

        await profiles.Editor!.TestConnectionAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(profiles.Editor.ConnectionTestResult);
    }

    // ── STUDIO-43: the connection test exercises the profile and the screen follows it ──

    private static ModelProfile Zai(bool? thinking) => new()
    {
        Name = "Z.AI",
        Provider = "Z.AI",
        Model = "glm-5.2",
        BaseUrl = "https://api.z.ai/api/paas/v4",
        TimeoutSeconds = 600,
        ThinkingEnabled = thinking,
        KeyEnvName = "ZAI_API_KEY",
    };

    [Fact]
    public async Task The_connection_test_carries_the_profiles_model_thinking_and_a_bounded_deadline()
    {
        var keyStore = new FakeApiKeyStore();
        keyStore.Stage("ZAI_API_KEY", "sk-zai");
        var probe = new FakeLlmEndpointProbe();
        var (profiles, _, _, _) = Build(keyStore, probe);
        profiles.BeginEdit(Zai(thinking: false));

        await profiles.Editor!.TestConnectionAsync(TestContext.Current.CancellationToken);

        var request = probe.LastRequest;
        Assert.Equal("glm-5.2", request.Model);
        Assert.False(request.ThinkingEnabled);
        Assert.True(request.CheckCompletion);
        Assert.Equal(TimeSpan.FromSeconds(30), request.Timeout);   // not the profile's 600 s
    }

    [Fact]
    public async Task Changing_the_thinking_switch_clears_the_previous_test_result()
    {
        var (profiles, _, _, _) = Build();
        profiles.BeginEdit(Ollama("Local") with { ThinkingEnabled = false });
        var editor = profiles.Editor!;
        await editor.TestConnectionAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(editor.ConnectionTestResult);

        editor.SelectedThinking = editor.ThinkingChoices.Single(c => c.Value is null);

        Assert.Null(editor.ConnectionTestResult);
    }

    [Fact]
    public async Task Changing_the_model_or_the_url_clears_the_previous_test_result()
    {
        var (profiles, _, _, _) = Build();
        profiles.BeginEdit(Ollama("Local"));
        var editor = profiles.Editor!;

        await editor.TestConnectionAsync(TestContext.Current.CancellationToken);
        editor.Model = "llama3.2";
        Assert.Null(editor.ConnectionTestResult);

        await editor.TestConnectionAsync(TestContext.Current.CancellationToken);
        editor.BaseUrl = "http://localhost:11435/v1";
        Assert.Null(editor.ConnectionTestResult);
    }

    [Fact]
    public async Task Going_back_from_disabled_to_the_providers_default_shows_without_reopening_the_editor()
    {
        var keyStore = new FakeApiKeyStore();
        keyStore.Stage("ZAI_API_KEY", "sk-zai");
        var (profiles, _, _, _) = Build(keyStore, new FakeLlmEndpointProbe());
        profiles.BeginEdit(Zai(thinking: false));
        var editor = profiles.Editor!;
        await editor.TestConnectionAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(editor.ConnectionTestResult);
        var changed = new List<string?>();
        editor.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        editor.SelectedThinking = editor.ThinkingChoices.Single(c => c.Value is null);

        Assert.Same(editor, profiles.Editor);
        Assert.Null(editor.SelectedThinking.Value);
        Assert.Contains(nameof(ModelProfileEditorViewModel.SelectedThinking), changed);
        Assert.Contains(nameof(ModelProfileEditorViewModel.ConnectionTestResult), changed);
        Assert.Null(editor.ConnectionTestResult);
    }

    [Fact]
    public async Task The_testing_state_shows_while_the_probe_runs()
    {
        var probe = new FakeLlmEndpointProbe { Gate = new TaskCompletionSource() };
        var (profiles, _, _, _) = Build(new FakeApiKeyStore(), probe);
        profiles.BeginEdit(Ollama("Local"));
        var editor = profiles.Editor!;

        var running = editor.TestConnectionAsync(TestContext.Current.CancellationToken);

        Assert.True(editor.IsTestingConnection);
        Assert.False(editor.TestConnectionCommand.CanExecute(null));
        Assert.Equal("Testing the connection…", editor.ConnectionTestResult);

        probe.Gate.SetResult();
        await running;

        Assert.False(editor.IsTestingConnection);
        Assert.True(editor.TestConnectionCommand.CanExecute(null));
        Assert.Equal("Endpoint reachable — 2 model(s).", editor.ConnectionTestResult);
    }

    [Fact]
    public async Task A_failed_test_is_worded_with_its_step_url_and_cause()
    {
        var probe = new FakeLlmEndpointProbe
        {
            Result = new LlmProbeResult
            {
                Succeeded = false,
                Stage = LlmProbeStage.Completion,
                Failure = LlmProbeFailure.HttpStatus,
                Url = "http://localhost:11434/api/chat",
                Elapsed = TimeSpan.FromSeconds(1.5),
                StatusCode = 404,
                ReasonPhrase = "Not Found",
                Detail = "model 'qwen2.5:14b' not found",
            },
        };
        var (profiles, _, _, _) = Build(new FakeApiKeyStore(), probe);
        profiles.BeginEdit(Ollama("Local"));

        await profiles.Editor!.TestConnectionAsync(TestContext.Current.CancellationToken);

        var line = profiles.Editor.ConnectionTestResult!;
        Assert.Contains("test request", line, StringComparison.Ordinal);
        Assert.Contains("http://localhost:11434/api/chat", line, StringComparison.Ordinal);
        Assert.Contains("404", line, StringComparison.Ordinal);
        Assert.Contains("not found", line, StringComparison.Ordinal);
    }

    // ── the tab gating ──

    private static SettingsScreenViewModel Screen(UiModeViewModel mode)
    {
        var (profiles, _, _, _) = Build();
        var config = new ConfigTabViewModel(new StudioServices
        {
            SettingsStore = new FakeAppSettingsStore(),
            Directories = new FakeDirectoryProbe(),
        });
        return new SettingsScreenViewModel(config, profiles, mode);
    }

    [Fact]
    public void An_expert_tab_requested_in_novice_mode_falls_back_to_the_model_tab()
    {
        var screen = Screen(new UiModeViewModel());

        screen.ShowJsonCommand.Execute(null);

        Assert.True(screen.IsModelTab);
    }

    [Fact]
    public void Switching_back_to_novice_leaves_no_blank_screen_behind()
    {
        var mode = new UiModeViewModel("expert");
        var screen = Screen(mode);
        screen.ShowLimitsCommand.Execute(null);
        Assert.True(screen.IsLimitsTab);

        mode.SetNoviceCommand.Execute(null);

        Assert.True(screen.IsModelTab);
    }

    [Fact]
    public void The_folders_tab_is_open_to_both_modes()
    {
        var screen = Screen(new UiModeViewModel());

        screen.ShowFoldersCommand.Execute(null);

        Assert.True(screen.IsFoldersTab);
    }

    /// <summary>STUDIO-21: the tool keys and the catalogue are for everyone; the MCP servers are the expert's.</summary>
    [Fact]
    public void The_tools_tab_is_open_to_both_modes_and_the_mcp_tab_is_the_experts()
    {
        var novice = Screen(new UiModeViewModel());
        novice.ShowToolsCommand.Execute(null);
        Assert.True(novice.IsToolsTab);
        novice.ShowMcpCommand.Execute(null);
        Assert.True(novice.IsModelTab);

        var mode = new UiModeViewModel("expert");
        var expert = Screen(mode);
        expert.ShowMcpCommand.Execute(null);
        Assert.True(expert.IsMcpTab);

        mode.SetNoviceCommand.Execute(null);

        Assert.True(expert.IsModelTab);
    }

    /// <summary>STUDIO-67: the e-mail accounts are everyone's, so a switch back to novice stays on them.</summary>
    [Fact]
    public void The_mails_tab_is_open_to_both_modes_and_a_switch_back_to_novice_stays_on_it()
    {
        var novice = Screen(new UiModeViewModel());
        var raised = new List<string>();
        novice.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        novice.ShowMailsCommand.Execute(null);

        Assert.True(novice.IsMailsTab);
        Assert.Equal(SettingsScreenViewModel.MailsTab, novice.ActiveTab);
        Assert.Contains(nameof(SettingsScreenViewModel.IsMailsTab), raised);
        Assert.False(novice.IsModelTab);

        var mode = new UiModeViewModel("expert");
        var expert = Screen(mode);
        expert.ShowMailsCommand.Execute(null);
        Assert.True(expert.IsMailsTab);

        mode.SetNoviceCommand.Execute(null);

        Assert.True(expert.IsMailsTab);
    }

    /// <summary>STUDIO-67: a tab name the screen does not know never leaves a blank screen.</summary>
    [Fact]
    public void A_tab_name_the_screen_does_not_know_still_falls_back_to_the_model_tab()
    {
        var screen = Screen(new UiModeViewModel("expert"));
        screen.ShowMailsCommand.Execute(null);

        screen.ActiveTab = "mail";

        Assert.True(screen.IsModelTab);
        Assert.False(screen.IsMailsTab);
    }

    /// <summary>STUDIO-67: the e-mail form shows the expert's fields to the expert alone, and follows the switch.</summary>
    [Fact]
    public void The_email_form_follows_the_mode_switch()
    {
        var mode = new UiModeViewModel();
        var screen = Screen(mode);
        Assert.False(screen.Config.Email.IsExpert);

        mode.SetExpertCommand.Execute(null);
        Assert.True(screen.Config.Email.IsExpert);

        mode.SetNoviceCommand.Execute(null);
        Assert.False(screen.Config.Email.IsExpert);
    }

    // ── the read-only « Team folders » section (STUDIO-14, D-13 / P-1) ──

    private static SettingsScreenViewModel Screen(UiModeViewModel mode, TeamFoldersViewModel teamFolders)
    {
        var (profiles, _, _, _) = Build();
        var config = new ConfigTabViewModel(new StudioServices
        {
            SettingsStore = new FakeAppSettingsStore(),
            Directories = new FakeDirectoryProbe(),
        });
        return new SettingsScreenViewModel(config, profiles, mode, teamFolders);
    }

    private static string SeedTeam(string root, string slug, string name, params string[] mounts)
    {
        var directory = Path.Combine(root, slug);
        Directory.CreateDirectory(directory);
        TeamCatalog.SaveMetadata(directory, new StudioTeamMetadata
        {
            Name = name,
            Mounts = mounts.Length > 0 ? mounts : null,
        });
        return directory;
    }

    /// <summary>
    /// A team's own folders are vouched for by living inside the team and are never written to
    /// the settings file — so the settings screen shows them from the sidecars, for information:
    /// the teams' in-team entries make rows, a folder outside the team is the settings' own
    /// business, and nothing on the section can write anywhere.
    /// </summary>
    [Fact]
    public void The_folders_tab_lists_the_in_team_folders_of_adopted_teams_read_only()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-settings-{Guid.NewGuid():N}");
        try
        {
            var veille = SeedTeam(root, "veille", "Veille concurrentielle",
                "./input:/workspace:ro", "./output:/output:rw", Path.Combine("/data", "docs") + ":/docs:ro");
            SeedTeam(root, "rapport", "Rapport hebdo", "./rapports:/rapports:rw");
            var sidecar = File.ReadAllBytes(Path.Combine(veille, StudioTeamMetadata.FileName));

            var screen = Screen(new UiModeViewModel(), new TeamFoldersViewModel(() => TeamCatalog.List(root)));
            screen.ShowFoldersCommand.Execute(null);

            Assert.True(screen.IsFoldersTab);
            Assert.True(screen.TeamFolders.HasRows);
            Assert.Equal(
                ["Rapport hebdo · /rapports → rapports", "Veille concurrentielle · /workspace → input", "Veille concurrentielle · /output → output"],
                screen.TeamFolders.Rows.Select(r => r.Label));
            var output = screen.TeamFolders.Rows.Single(r => r.VirtualPath == "/output");
            Assert.Equal("Veille concurrentielle", output.TeamName);
            Assert.Equal("output", output.Folder);
            Assert.True(output.IsReadWrite);
            Assert.Equal("write", output.RightsBadge);
            Assert.Equal("Read / write (create and delete allowed)", output.RightsLabel);
            Assert.False(screen.TeamFolders.Rows.Single(r => r.VirtualPath == "/workspace").IsReadWrite);
            // The folder outside the team is not the section's to list — nor does any row
            // carry a disk path (ADR-008).
            Assert.DoesNotContain(screen.TeamFolders.Rows, r => r.VirtualPath == "/docs");
            Assert.All(screen.TeamFolders.Rows, r => Assert.DoesNotContain(root, r.Label, StringComparison.Ordinal));

            // Read-only: no command on the section, no entry in the settings document, the
            // sidecar byte for byte what it was.
            Assert.DoesNotContain(
                typeof(TeamFoldersViewModel).GetProperties(),
                p => typeof(System.Windows.Input.ICommand).IsAssignableFrom(p.PropertyType));
            Assert.Empty(screen.Config.Mounts.Mounts);
            Assert.Equal(sidecar, File.ReadAllBytes(Path.Combine(veille, StudioTeamMetadata.FileName)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_team_without_in_team_folders_adds_no_row()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-settings-{Guid.NewGuid():N}");
        try
        {
            SeedTeam(root, "veille", "Veille", Path.Combine("/data", "docs") + ":/docs:ro", Path.Combine("/data", "sortie") + ":/output:rw");
            SeedTeam(root, "contrats", "Contrats");
            Directory.CreateDirectory(Path.Combine(root, "sans-sidecar"));

            var screen = Screen(new UiModeViewModel(), new TeamFoldersViewModel(() => TeamCatalog.List(root)));
            screen.ShowFoldersCommand.Execute(null);

            Assert.Empty(screen.TeamFolders.Rows);
            Assert.True(screen.TeamFolders.IsEmpty);
            Assert.False(screen.TeamFolders.HasRows);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Through the shell: an adoption writes <c>./output:/output:rw</c> into the sidecar and
    /// the section shows the row without anyone visiting the folders tab first — a team
    /// adopted a minute ago must not be missing from the one screen that lists the folders.
    /// </summary>
    [Fact]
    public async Task An_adoption_refreshes_the_team_folders_section()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-settings-{Guid.NewGuid():N}");
        var promoted = Path.Combine(root, "ma-veille");
        try
        {
            var processes = new FakeProcessLauncher();
            var shell = Shell(processes, root);
            Assert.Empty(shell.Settings.TeamFolders.Rows);

            processes.OutputToEmit.AddRange(
            [
                Out("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"veille","dir":"/ws/.orkeon/forge/veille","format":"yaml","resumed":false}"""),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"blueprint.ready","blueprint":{"crew":{"name":"veille"},"agents":[{"key":"a","role":"A","tools":["file_write"]}],"tasks":[{"key":"t","description":"d","agent":"a","deliverable":"/output/rapport.md"}],"rationale":"r"},"iteration":1}"""),
                Out("""{"v":2,"seq":3,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);
            var wizard = shell.CreateTeam;
            wizard.Need = "une veille documentaire";
            await wizard.ComposeCommand.ExecuteAsync();
            wizard.TeamName = "Ma veille";
            Assert.True(wizard.CanSaveTeam);

            processes.OutputToEmit.Clear();
            processes.OutputToEmit.AddRange(
            [
                Out($$"""{"v":2,"seq":1,"ts":"t","kind":"promoted","path":{{System.Text.Json.JsonSerializer.Serialize(promoted)}},"launcher":"run.sh","install":""}"""),
                Out("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"ready","exitCode":0}"""),
            ]);
            await wizard.SaveTeamCommand.ExecuteAsync();

            Assert.Equal(["./output:/output:rw"], TeamCatalog.Describe(promoted).Metadata!.Mounts);
            var row = Assert.Single(shell.Settings.TeamFolders.Rows);
            Assert.Equal("Ma veille · /output → output", row.Label);
            Assert.Equal("write", row.RightsBadge);
            // And nothing reached the settings document (P-1).
            Assert.Empty(shell.Config.Mounts.Mounts);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The two card gestures that raise no event of their own still reach the section: the
    /// duplicate's own copy of the folder appears, and a deleted team's row goes.
    /// </summary>
    [Fact]
    public void Duplicating_and_deleting_a_team_from_its_card_refresh_the_team_folders_section()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orkeon-settings-{Guid.NewGuid():N}");
        try
        {
            SeedTeam(root, "veille", "Veille", "./output:/output:rw");
            var shell = Shell(new FakeProcessLauncher(), root);
            Assert.Equal(["Veille · /output → output"], shell.Settings.TeamFolders.Rows.Select(r => r.Label));

            shell.Teams.Teams.Single().DuplicateCommand.Execute(null);

            Assert.Equal(
                ["Veille · /output → output", "Veille (copy) · /output → output"],
                shell.Settings.TeamFolders.Rows.Select(r => r.Label));

            var original = shell.Teams.Teams.Single(card => card.Summary.Slug == "veille");
            original.AskDeleteCommand.Execute(null);
            original.ConfirmDeleteCommand.Execute(null);

            Assert.Equal(["Veille (copy) · /output → output"], shell.Settings.TeamFolders.Rows.Select(r => r.Label));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static ProcessOutputLine Out(string json) =>
        ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, json);

    /// <summary>A shell over a real teams root, the engine scripted, an assistant profile elected so the wizard is open.</summary>
    private static MainWindowViewModel Shell(FakeProcessLauncher processes, string teamsRoot)
    {
        var shell = new MainWindowViewModel(
            new StudioServices
            {
                SettingsStore = new FakeAppSettingsStore(),
                Directories = new FakeDirectoryProbe(),
                TargetProbe = new FakeTargetProbe(),
                Picker = new FakePathPicker(),
                ProcessRunner = new OrkeonProcessRunner(
                    new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                HistoryStore = new FakeLaunchHistoryStore(),
                ForgeClient = new ForgeClient(processes, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                ProfileStore = new InMemoryModelProfileStore(),
                LlmProbe = new FakeLlmEndpointProbe(),
                KeyStore = new FakeApiKeyStore(),
            },
            forgeWorkspace: "/ws",
            teamsRoot: teamsRoot);
        shell.Settings.Profiles.CommitEdit(Ollama("Local"), previousName: null);
        shell.Settings.Profiles.StudioProfileName = "Local";
        return shell;
    }

    [Fact]
    public void A_novice_creates_a_deepseek_setting_in_two_gestures_card_then_key()
    {
        var keyStore = new FakeApiKeyStore();
        var (profiles, _, _, _) = Build(keyStore, new FakeLlmEndpointProbe());

        profiles.NewProfileCommand.Execute(null);
        var editor = profiles.Editor!;

        // One click on the DeepSeek card: endpoint and model are filled, the key block opens.
        editor.SelectedProvider = editor.Providers.Single(p => p.Name == LlmPresets.DeepSeek);
        Assert.Equal("https://api.deepseek.com", editor.BaseUrl);
        Assert.Equal("deepseek-flash", editor.Model);
        Assert.True(editor.RequiresApiKey);
        Assert.Equal("DEEPSEEK_API_KEY", editor.ApiKeyEnvName);
        Assert.False(editor.HasStoredKey);

        // Paste the key, remember it: it lands in the environment store, never in the profile.
        editor.ApiKeyInput = " sk-novice ";
        editor.StoreKeyCommand.Execute(null);
        Assert.Equal("sk-novice", keyStore.Saved["DEEPSEEK_API_KEY"]);
        Assert.True(editor.HasStoredKey);
        Assert.Equal("", editor.ApiKeyInput);

        editor.Name = "Mon DeepSeek";
        Assert.True(editor.CanSave);
        editor.SaveCommand.Execute(null);

        var saved = profiles.Set.Profiles.Single(p => p.Name == "Mon DeepSeek");
        Assert.Equal("DEEPSEEK_API_KEY", saved.KeyEnvName);
        Assert.DoesNotContain("sk-novice", System.Text.Json.JsonSerializer.Serialize(saved), StringComparison.Ordinal);
    }

    [Fact]
    public void Picking_a_reasoning_provider_prefills_the_timeout_and_a_typed_value_survives()
    {
        // LLM-11: the run of 2026-09-20 ran Kimi K2.6 on a 180 s timeout; the card now brings
        // the 600 s the settings templates carry, says why, and never overwrites a typed value.
        var (profiles, _, _, _) = Build();
        profiles.NewProfileCommand.Execute(null);
        var editor = profiles.Editor!;
        Assert.Equal("", editor.TimeoutText);

        editor.SelectedProvider = editor.Providers.Single(p => p.Name == LlmPresets.Kimi);
        Assert.Equal("600", editor.TimeoutText);
        Assert.Contains("600", editor.TimeoutHint, StringComparison.Ordinal);
        Assert.Contains("Kimi", editor.TimeoutHint, StringComparison.Ordinal);

        // The seed leaves with the card that brought it.
        editor.SelectedProvider = editor.Providers.Single(p => p.Name == LlmPresets.Ollama);
        Assert.Equal("", editor.TimeoutText);
        Assert.DoesNotContain("Kimi", editor.TimeoutHint, StringComparison.Ordinal);
        Assert.DoesNotContain("pre-filled", editor.TimeoutHint, StringComparison.Ordinal);
        Assert.Contains("30 s", editor.TimeoutHint, StringComparison.Ordinal);

        // A typed value is the user's, whatever card comes next.
        editor.TimeoutText = "240";
        editor.SelectedProvider = editor.Providers.Single(p => p.Name == LlmPresets.DeepSeek);
        Assert.Equal("240", editor.TimeoutText);

        editor.Name = "Mon DeepSeek";
        editor.SaveCommand.Execute(null);
        Assert.Equal(240, profiles.Set.Profiles.Single(p => p.Name == "Mon DeepSeek").TimeoutSeconds);
    }

    [Fact]
    public void The_thinking_switch_rides_the_profile_and_its_launch_overrides()
    {
        // LLM-11: the knob existed in the crew YAML and the settings file, but the profile —
        // the thing Studio pins per model — had no place for it.
        var (profiles, _, _, _) = Build();
        profiles.NewProfileCommand.Execute(null);
        var editor = profiles.Editor!;
        editor.SelectedProvider = editor.Providers.Single(p => p.Name == LlmPresets.Kimi);
        Assert.Null(editor.SelectedThinking.Value);   // provider default
        Assert.Equal(3, editor.ThinkingChoices.Count);

        editor.SelectedThinking = editor.ThinkingChoices.Single(c => c.Value == false);
        editor.ThinkingEffortText = " high ";
        editor.Name = "Kimi sans réflexion";
        editor.SaveCommand.Execute(null);

        var saved = profiles.Set.Profiles.Single(p => p.Name == "Kimi sans réflexion");
        Assert.False(saved.ThinkingEnabled);
        Assert.Equal("high", saved.ThinkingEffort);
        Assert.Equal("false", saved.EnvironmentOverrides()["ORKEON_Llm__Thinking__Enabled"]);
        Assert.Equal("high", saved.EnvironmentOverrides()["ORKEON_Llm__Thinking__Effort"]);

        profiles.BeginEdit(saved);
        Assert.False(profiles.Editor!.SelectedThinking.Value);
        Assert.Equal("high", profiles.Editor.ThinkingEffortText);
    }

    [Fact]
    public async Task The_connection_test_refuses_to_probe_without_a_key_and_uses_the_stored_one_after()
    {
        var keyStore = new FakeApiKeyStore();
        var probe = new FakeLlmEndpointProbe();
        var (profiles, _, _, _) = Build(keyStore, probe);

        profiles.NewProfileCommand.Execute(null);
        var editor = profiles.Editor!;
        editor.SelectedProvider = editor.Providers.Single(p => p.Name == LlmPresets.DeepSeek);

        await editor.TestConnectionAsync(CancellationToken.None);
        Assert.Empty(probe.Requests); // no key → no doomed 401 probe
        Assert.False(string.IsNullOrEmpty(editor.ConnectionTestResult));

        editor.ApiKeyInput = "sk-now";
        await editor.StoreKeyCommand.ExecuteAsync();
        await editor.TestConnectionAsync(CancellationToken.None);
        Assert.Equal("sk-now", probe.LastRequest.ApiKey);
    }

    [Fact]
    public void The_catchall_card_demands_url_and_model_and_the_echo_card_needs_nothing()
    {
        var (profiles, _, _, _) = Build();

        profiles.NewProfileCommand.Execute(null);
        var editor = profiles.Editor!;
        editor.Name = "Maison";

        editor.SelectedProvider = editor.Providers.Single(p => p.Name == LlmPresets.Custom);
        Assert.True(editor.UrlAlwaysVisible);
        Assert.False(editor.CanSave); // URL + model still blank

        editor.BaseUrl = "http://localhost:8080/v1";
        editor.Model = "local-model";
        Assert.True(editor.CanSave);

        editor.SelectedProvider = editor.Providers.Single(p => p.Name == LlmPresets.None);
        Assert.True(editor.IsNone);
        Assert.False(editor.ShowFields);
        Assert.False(editor.RequiresApiKey);
        Assert.False(editor.ShowTestRow);
        Assert.True(editor.CanSave);
    }

    // ── STUDIO-44: a remembered key is recognised, and a failed write shows ──

    /// <summary>The Z.AI profile as its card saves it: the provider named by the card's title.</summary>
    private static ModelProfile ZaiCard() => Zai(thinking: null) with { Provider = "Z.AI (GLM)" };

    [Fact]
    public async Task A_key_remembered_once_is_recognised_after_saving_and_reopening_even_from_an_older_parent()
    {
        var environment = new FakeEnvironmentVariables();
        var (profiles, _, _, _) = Build(new EnvironmentApiKeyStore(environment), new FakeLlmEndpointProbe());
        profiles.BeginEdit(ZaiCard());
        var editor = profiles.Editor!;
        Assert.False(editor.HasStoredKey);

        // Remember the key, save the setting, close the editor.
        editor.ApiKeyInput = "sk-zai";
        await editor.StoreKeyAsync();
        Assert.True(editor.HasStoredKey);
        editor.SaveCommand.Execute(null);
        Assert.Null(profiles.Editor);

        // Studio relaunched from a terminal opened before the key: only the user scope holds it.
        environment.Process.Clear();
        profiles.BeginEdit(profiles.Set.Profiles.Single(p => p.Name == "Z.AI"));

        Assert.True(profiles.Editor!.HasStoredKey);
        Assert.Equal("key remembered", profiles.Editor.KeyStatusText);
        Assert.Equal("sk-zai", environment.Process["ZAI_API_KEY"]);
    }

    [Fact]
    public async Task A_failed_persistent_write_keeps_the_key_for_the_session_and_says_so()
    {
        var keyStore = new FakeApiKeyStore { PersistFailure = new UnauthorizedAccessException("HKCU is read-only") };
        var (profiles, _, _, _) = Build(keyStore, new FakeLlmEndpointProbe());
        profiles.BeginEdit(ZaiCard());
        var editor = profiles.Editor!;
        var changed = new List<string?>();
        editor.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        editor.ApiKeyInput = "sk-zai";
        await editor.StoreKeyAsync();

        Assert.True(editor.HasStoredKey);
        Assert.Equal("key remembered", editor.KeyStatusText);
        Assert.Equal("", editor.ApiKeyInput);
        Assert.NotNull(editor.KeyStoreError);
        Assert.Contains("this session", editor.KeyStoreError, StringComparison.Ordinal);
        Assert.Contains("HKCU is read-only", editor.KeyStoreError, StringComparison.Ordinal);
        Assert.Contains(nameof(ModelProfileEditorViewModel.HasStoredKey), changed);
        Assert.Contains(nameof(ModelProfileEditorViewModel.KeyStatusText), changed);
        Assert.Contains(nameof(ModelProfileEditorViewModel.KeyStoreError), changed);
    }

    [Fact]
    public async Task A_successful_remember_clears_the_previous_failure()
    {
        var keyStore = new FakeApiKeyStore { PersistFailure = new InvalidOperationException("broadcast failed") };
        var (profiles, _, _, _) = Build(keyStore, new FakeLlmEndpointProbe());
        profiles.BeginEdit(ZaiCard());
        var editor = profiles.Editor!;
        editor.ApiKeyInput = "sk-1";
        await editor.StoreKeyAsync();
        Assert.NotNull(editor.KeyStoreError);

        keyStore.PersistFailure = null;
        editor.ApiKeyInput = "sk-2";
        await editor.StoreKeyAsync();

        Assert.Null(editor.KeyStoreError);
        Assert.Equal("sk-2", keyStore.Saved["ZAI_API_KEY"]);
    }

    [Fact]
    public void A_key_pasted_and_saved_with_the_profile_that_is_not_kept_is_said_on_the_list()
    {
        var keyStore = new FakeApiKeyStore { PersistFailure = new UnauthorizedAccessException("HKCU is read-only") };
        var (profiles, _, _, _) = Build(keyStore, new FakeLlmEndpointProbe());
        profiles.BeginEdit(ZaiCard());
        profiles.Editor!.ApiKeyInput = "sk-zai";

        profiles.Editor.SaveCommand.Execute(null);

        Assert.Null(profiles.Editor);
        Assert.Equal("sk-zai", keyStore.Saved["ZAI_API_KEY"]);
        Assert.True(profiles.HasKeyStoreError);
        Assert.Contains("HKCU is read-only", profiles.KeyStoreError, StringComparison.Ordinal);

        // Opening an editor again starts from a clean slate.
        profiles.BeginEdit(profiles.Set.Profiles.Single(p => p.Name == "Z.AI"));
        Assert.False(profiles.HasKeyStoreError);
    }

    [Fact]
    public void A_launch_under_a_profile_carries_the_key_held_only_in_the_user_scope()
    {
        // The run launcher and the assistant lay the profile over the child as ORKEON_Llm__*:
        // the key is resolved through the store, so a key only HKCU holds still reaches the child.
        var environment = new FakeEnvironmentVariables();
        environment.User["ZAI_API_KEY"] = "sk-zai";
        var (profiles, _, _, _) = Build(new EnvironmentApiKeyStore(environment), new FakeLlmEndpointProbe());

        var overrides = profiles.LaunchEnvironmentOf(Zai(thinking: null));

        Assert.Equal("sk-zai", overrides["ORKEON_Llm__ApiKey"]);
        Assert.Equal("glm-5.2", overrides["ORKEON_Llm__Model"]);
        Assert.Equal("sk-zai", environment.Process["ZAI_API_KEY"]);
    }

}

/// <summary>Novice auto-save and the profile usage chips (audit 07/16).</summary>
public sealed class SettingsRemediationTests
{
    [Fact]
    public void A_dirty_document_saves_itself_in_novice_mode_and_not_in_expert()
    {
        var store = new FakeAppSettingsStore();
        var novice = new ConfigTabViewModel(new StudioServices
        {
            SettingsStore = store,
            Directories = new FakeDirectoryProbe("/data"),
        });
        _ = new SettingsScreenViewModel(
            novice,
            new ModelProfilesViewModel(new InMemoryModelProfileStore(), novice.Llm),
            new UiModeViewModel("novice"));

        novice.Mounts.AddMount().PhysicalPath = "/data";
        novice.Rag.Profile = "quality";

        // The edit marked the document dirty; the novice screen saved it by itself.
        Assert.NotEmpty(store.SavedPaths);

        var expertStore = new FakeAppSettingsStore();
        var expert = new ConfigTabViewModel(new StudioServices
        {
            SettingsStore = expertStore,
            Directories = new FakeDirectoryProbe("/data"),
        });
        _ = new SettingsScreenViewModel(
            expert,
            new ModelProfilesViewModel(new InMemoryModelProfileStore(), expert.Llm),
            new UiModeViewModel("expert"));

        expert.Mounts.AddMount().PhysicalPath = "/data";
        expert.Rag.Profile = "quality";

        Assert.True(expert.IsDirty);
        Assert.Empty(expertStore.SavedPaths);
    }

    [Fact]
    public async Task The_profile_cards_carry_the_teams_that_name_them()
    {
        var store = new InMemoryModelProfileStore();
        await store.SaveAsync(new ModelProfileSet
        {
            Profiles = [new ModelProfile { Name = "Local", Provider = "ollama", BaseUrl = "http://localhost:11434", Model = "phi3" }],
            DefaultProfile = "Local",
        }, TestContext.Current.CancellationToken);

        var config = new ConfigTabViewModel(new StudioServices
        {
            SettingsStore = new FakeAppSettingsStore(),
            Directories = new FakeDirectoryProbe(),
        });
        var profiles = new ModelProfilesViewModel(store, config.Llm, loadTeams: () =>
        [
            new TeamSummary { Name = "Veille", Slug = "veille", Path = "/teams/veille",
                Metadata = new StudioTeamMetadata { Profile = "Local" } },
            new TeamSummary { Name = "Contrats", Slug = "contrats", Path = "/teams/contrats" },
        ]);
        await profiles.InitializeAsync(TestContext.Current.CancellationToken);

        var card = Assert.Single(profiles.Profiles);
        Assert.True(card.IsUsedByTeams);
        Assert.Equal(["Veille"], card.UsedByTeams);
    }
}

/// <summary>The settings screen's API-keys card: env-var rows, remember flow, no file ever.</summary>
public sealed class SecretsCardTests
{
    [Fact]
    public async Task One_row_per_distinct_key_variable_and_storing_wipes_the_field()
    {
        var store = new InMemoryModelProfileStore();
        await store.SaveAsync(new ModelProfileSet
        {
            Profiles =
            [
                new ModelProfile { Name = "DeepSeek rapide", Provider = "deepseek", BaseUrl = "https://api.deepseek.com", Model = "m", KeyEnvName = "DEEPSEEK_API_KEY" },
                new ModelProfile { Name = "DeepSeek raisonneur", Provider = "deepseek", BaseUrl = "https://api.deepseek.com", Model = "r", KeyEnvName = "DEEPSEEK_API_KEY" },
                new ModelProfile { Name = "Local", Provider = "ollama", BaseUrl = "http://localhost:11434", Model = "phi3" },
            ],
            DefaultProfile = "Local",
        }, TestContext.Current.CancellationToken);

        var keys = new FakeApiKeyStore();
        var config = new ConfigTabViewModel(new StudioServices
        {
            SettingsStore = new FakeAppSettingsStore(),
            Directories = new FakeDirectoryProbe(),
        });
        var profiles = new ModelProfilesViewModel(store, config.Llm, keyStore: keys);
        await profiles.InitializeAsync(TestContext.Current.CancellationToken);

        // The keyless local profile contributes no row; the two DeepSeek profiles share one.
        var row = Assert.Single(profiles.Secrets);
        Assert.Equal("DEEPSEEK_API_KEY", row.EnvName);
        Assert.Contains("DeepSeek rapide", row.UsedBy, StringComparison.Ordinal);
        Assert.False(row.HasKey);

        row.KeyInput = "  sk-test-123  ";
        Assert.True(row.StoreCommand.CanExecute(null));
        await row.StoreCommand.ExecuteAsync();

        Assert.Equal("sk-test-123", keys.Saved["DEEPSEEK_API_KEY"]);
        Assert.Equal("", row.KeyInput);   // the pasted key does not linger on screen
        Assert.True(row.HasKey);
    }

    [Fact]
    public async Task A_row_whose_persistent_write_fails_keeps_the_key_and_says_so()
    {
        var keys = new FakeApiKeyStore { PersistFailure = new UnauthorizedAccessException("HKCU is read-only") };
        var row = new SecretRowViewModel("DEEPSEEK_API_KEY", "DeepSeek", keys, Orkeon.Studio.Core.Localization.EnglishStudioStrings.Instance);
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.KeyInput = "sk-test";
        await row.StoreAsync();

        Assert.True(row.HasKey);
        Assert.Equal("", row.KeyInput);
        Assert.NotNull(row.StoreError);
        Assert.Contains("HKCU is read-only", row.StoreError, StringComparison.Ordinal);
        Assert.Contains(nameof(SecretRowViewModel.StoreError), changed);
        Assert.Contains(nameof(SecretRowViewModel.HasKey), changed);
    }
}

/// <summary>
/// Electing the assistant's profile must not rebuild the name list feeding the ComboBox:
/// WPF nulls a TwoWay selection whose ItemsSource is cleared mid-write and swallows the
/// correcting notification, so the election LOOKS unsaved (it was stored all along).
/// </summary>
public sealed class AssistantElectionTests
{
    [Fact]
    public async Task Electing_the_assistant_stores_it_and_leaves_the_name_list_untouched()
    {
        var store = new InMemoryModelProfileStore();
        await store.SaveAsync(new ModelProfileSet
        {
            Profiles =
            [
                new ModelProfile { Name = "Local", Provider = "ollama", BaseUrl = "http://localhost:11434", Model = "phi3" },
                new ModelProfile { Name = "DeepSeek", Provider = "deepseek", BaseUrl = "https://api.deepseek.com", Model = "m" },
            ],
            DefaultProfile = "Local",
        }, TestContext.Current.CancellationToken);

        var config = new ConfigTabViewModel(new StudioServices
        {
            SettingsStore = new FakeAppSettingsStore(),
            Directories = new FakeDirectoryProbe(),
        });
        var profiles = new ModelProfilesViewModel(store, config.Llm);
        await profiles.InitializeAsync(TestContext.Current.CancellationToken);

        var resets = 0;
        profiles.ProfileNames.CollectionChanged += (_, _) => resets++;

        profiles.StudioProfileName = "DeepSeek";

        Assert.Equal(0, resets); // the ComboBox's ItemsSource was never disturbed
        Assert.Equal("DeepSeek", profiles.StudioProfileName);
        Assert.True(profiles.HasStudioProfile);
        Assert.Equal("DeepSeek", (await store.LoadAsync(TestContext.Current.CancellationToken)).Set.StudioProfile);
    }
}

/// <summary>
/// LLM-10: the hint under the response-budget field says what an empty field means for THIS
/// model — the documented maximum, no cap at all, a local runtime, or the 4096 fallback with
/// the invitation to pin — and follows the model and the provider as they are edited.
/// </summary>
public sealed class EditorMaxTokensHintTests
{
    private static async Task<ModelProfilesViewModel> OpenAsync(ModelProfile profile)
    {
        var store = new InMemoryModelProfileStore();
        await store.SaveAsync(new ModelProfileSet { Profiles = [profile], DefaultProfile = profile.Name }, TestContext.Current.CancellationToken);

        var config = new ConfigTabViewModel(new StudioServices
        {
            SettingsStore = new FakeAppSettingsStore(),
            Directories = new FakeDirectoryProbe(),
        });
        var profiles = new ModelProfilesViewModel(store, config.Llm);
        await profiles.InitializeAsync(TestContext.Current.CancellationToken);
        profiles.BeginEdit(profiles.Set.Profiles[0]);
        return profiles;
    }

    private static string Formatted(int tokens) => tokens.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

    [Fact]
    public async Task A_documented_model_names_its_maximum_and_an_unknown_one_says_4096()
    {
        var profiles = await OpenAsync(new ModelProfile { Name = "Kimi K3", Provider = "Kimi", BaseUrl = "https://api.moonshot.ai/v1", Model = "kimi-k3" });
        var editor = profiles.Editor!;

        Assert.Contains(Formatted(131_072), editor.MaxTokensHint, StringComparison.Ordinal);

        var raised = new List<string>();
        editor.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
        editor.Model = "kimi-k9-nobody-documented";

        Assert.Contains("4096", editor.MaxTokensHint, StringComparison.Ordinal);
        Assert.Contains(nameof(ModelProfileEditorViewModel.MaxTokensHint), raised);
    }

    [Fact]
    public async Task A_vendor_without_a_documented_cap_and_a_local_runtime_send_none()
    {
        var profiles = await OpenAsync(new ModelProfile { Name = "Any", Provider = "Kimi", Model = "kimi-k3" });
        var editor = profiles.Editor!;

        editor.SelectedProvider = editor.Providers.First(p => p.Name == Orkeon.Constants.Llm.LlmProviderKeys.Mistral);
        Assert.DoesNotContain("4096", editor.MaxTokensHint, StringComparison.Ordinal);
        Assert.Contains("window", editor.MaxTokensHint, StringComparison.Ordinal);

        editor.SelectedProvider = editor.Providers.First(p => p.Name == Orkeon.Constants.Llm.LlmProviderKeys.Ollama);
        editor.Model = "some-local-model:7b";
        Assert.Contains("local runtime", editor.MaxTokensHint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_entry_bound_to_one_provider_does_not_leak_to_another()
    {
        var profiles = await OpenAsync(new ModelProfile { Name = "Any", Provider = "Kimi", Model = "kimi-k3" });
        var editor = profiles.Editor!;

        // Mammouth's own figure for qwen3.7-plus (65 500) holds on the proxy only; the direct
        // endpoint shows the vendor's 131 072.
        editor.SelectedProvider = editor.Providers.First(p => p.Name == Orkeon.Constants.Llm.LlmProviderKeys.Mammouth);
        editor.Model = "qwen3.7-plus";
        Assert.Contains(Formatted(65_500), editor.MaxTokensHint, StringComparison.Ordinal);

        editor.SelectedProvider = editor.Providers.First(p => p.Name == Orkeon.Constants.Llm.LlmProviderKeys.Qwen);
        Assert.Contains(Formatted(131_072), editor.MaxTokensHint, StringComparison.Ordinal);

        // Together's window entries were withdrawn on 2026-09-21 (LLM-10 D-05): its engines
        // refuse them, so an id it serves keeps the fallback like everywhere else.
        editor.SelectedProvider = editor.Providers.First(p => p.Name == Orkeon.Constants.Llm.LlmProviderKeys.Together);
        editor.Model = "Qwen/Qwen3.5-9B";
        Assert.Contains("4096", editor.MaxTokensHint, StringComparison.Ordinal);
    }
}

/// <summary>The editor's pinned-temperature field: tolerant parse, saved on the profile.</summary>
public sealed class EditorTemperatureTests
{
    [Fact]
    public async Task The_field_round_trips_and_tolerates_the_french_comma()
    {
        var store = new InMemoryModelProfileStore();
        await store.SaveAsync(new ModelProfileSet
        {
            Profiles = [new ModelProfile { Name = "Kimi K3", Provider = "Kimi", BaseUrl = "https://api.moonshot.ai/v1", Model = "kimi-k3", Temperature = 1 }],
            DefaultProfile = "Kimi K3",
        }, TestContext.Current.CancellationToken);

        var config = new ConfigTabViewModel(new StudioServices
        {
            SettingsStore = new FakeAppSettingsStore(),
            Directories = new FakeDirectoryProbe(),
        });
        var profiles = new ModelProfilesViewModel(store, config.Llm);
        await profiles.InitializeAsync(TestContext.Current.CancellationToken);

        profiles.BeginEdit(profiles.Set.Profiles[0]);
        Assert.Equal("1", profiles.Editor!.TemperatureText);

        profiles.Editor.TemperatureText = "0,7"; // a French keyboard types the comma
        Assert.Equal(0.7, profiles.Editor.ParsedTemperature);

        profiles.Editor.TimeoutText = "180";
        Assert.Equal(180, profiles.Editor.ParsedTimeoutSeconds);

        profiles.Editor.SaveCommand.Execute(null);
        var saved = (await store.LoadAsync(TestContext.Current.CancellationToken)).Set.Profiles[0];
        Assert.Equal(0.7, saved.Temperature);
        Assert.Equal(180, saved.TimeoutSeconds);
    }

    /// <summary>
    /// STUDIO-12 C5b: the response budget is editable from the profile — the novice path —
    /// not only from the expert Settings tab. Empty leaves the engine default; a typed value
    /// that does not parse blocks the save instead of vanishing.
    /// </summary>
    [Fact]
    public async Task The_max_tokens_field_round_trips_and_an_unparseable_value_blocks_the_save()
    {
        var store = new InMemoryModelProfileStore();
        await store.SaveAsync(new ModelProfileSet
        {
            Profiles = [new ModelProfile { Name = "Kimi K3", Provider = "Kimi", BaseUrl = "https://api.moonshot.ai/v1", Model = "kimi-k3" }],
            DefaultProfile = "Kimi K3",
        }, TestContext.Current.CancellationToken);

        var config = new ConfigTabViewModel(new StudioServices
        {
            SettingsStore = new FakeAppSettingsStore(),
            Directories = new FakeDirectoryProbe(),
        });
        var profiles = new ModelProfilesViewModel(store, config.Llm);
        await profiles.InitializeAsync(TestContext.Current.CancellationToken);

        profiles.BeginEdit(profiles.Set.Profiles[0]);
        Assert.Equal("", profiles.Editor!.MaxTokensText); // null = the engine default

        profiles.Editor.MaxTokensText = "lots";
        Assert.Null(profiles.Editor.ParsedMaxTokens);
        Assert.False(profiles.Editor.CanSave);

        profiles.Editor.MaxTokensText = "32768";
        Assert.Equal(32768, profiles.Editor.ParsedMaxTokens);
        Assert.True(profiles.Editor.CanSave);

        profiles.Editor.SaveCommand.Execute(null);
        var saved = (await store.LoadAsync(TestContext.Current.CancellationToken)).Set.Profiles[0];
        Assert.Equal(32768, saved.MaxTokens);
        Assert.Equal("32768", saved.EnvironmentOverrides()["ORKEON_Llm__MaxTokens"]);

        profiles.BeginEdit(profiles.Set.Profiles[0]);
        Assert.Equal("32768", profiles.Editor!.MaxTokensText);
    }
}

/// <summary>
/// STUDIO-12 C6: a profile file that exists but cannot be read is said on the screen. It
/// used to load as the empty set — a first-run screen over a hand-written file, and the
/// next change overwrote that file.
/// </summary>
public sealed class ProfileLoadErrorTests
{
    private sealed class UnreadableProfileStore : IModelProfileStore
    {
        public Task<ModelProfileLoadResult> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelProfileLoadResult(ModelProfileSet.Empty, "studio-model-profiles.json: '{' is invalid after a value."));

        public Task SaveAsync(ModelProfileSet profiles, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task An_unreadable_file_is_named_on_the_screen_instead_of_passing_for_a_first_run()
    {
        var config = new ConfigTabViewModel(new StudioServices
        {
            SettingsStore = new FakeAppSettingsStore(),
            Directories = new FakeDirectoryProbe(),
        });
        var profiles = new ModelProfilesViewModel(new UnreadableProfileStore(), config.Llm);

        await profiles.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(profiles.IsEmpty);
        Assert.True(profiles.HasLoadError);
        Assert.Contains("studio-model-profiles.json", profiles.LoadError!, StringComparison.Ordinal);
        Assert.Contains("could not be read", profiles.LoadError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_clean_load_shows_no_error_line()
    {
        var config = new ConfigTabViewModel(new StudioServices
        {
            SettingsStore = new FakeAppSettingsStore(),
            Directories = new FakeDirectoryProbe(),
        });
        var profiles = new ModelProfilesViewModel(new InMemoryModelProfileStore(), config.Llm);

        await profiles.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.False(profiles.HasLoadError);
        Assert.Null(profiles.LoadError);
    }
}
