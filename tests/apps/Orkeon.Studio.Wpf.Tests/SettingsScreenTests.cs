using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The unified "Réglages" screen (design v3): the model profiles, their two elections, the
/// mirror of the default into the Llm section, and the tab gating by mode.
/// </summary>
public sealed class SettingsScreenTests
{
    private static (ModelProfilesViewModel Profiles, LlmSectionViewModel Llm, InMemoryModelProfileStore Store, AppSettingsDocument Document) Build() =>
        Build(new FakeApiKeyStore(), new FakeLlmEndpointProbe());

    private static (ModelProfilesViewModel Profiles, LlmSectionViewModel Llm, InMemoryModelProfileStore Store, AppSettingsDocument Document) Build(
        FakeApiKeyStore keyStore, FakeLlmEndpointProbe probe)
    {
        var document = AppSettingsDocument.CreateEmpty();
        var llm = new LlmSectionViewModel(() => document, () => { }, new FakeLlmEndpointProbe());
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
        var (profiles, llm, store, _) = Build();

        profiles.NewProfileCommand.Execute(null);
        profiles.Editor!.Name = "Local rapide";
        profiles.Editor.SaveCommand.Execute(null);

        Assert.Null(profiles.Editor);
        Assert.Equal("Local rapide", profiles.DefaultProfileName);
        // The first preset seeded the endpoint; electing the default writes it to the document.
        Assert.NotNull(llm.Model);
        Assert.NotNull(llm.BaseUrl);
        var persisted = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Single(persisted.Profiles);
    }

    [Fact]
    public void Electing_a_default_rewrites_the_llm_section_to_that_profile()
    {
        var (profiles, llm, _, _) = Build();
        profiles.CommitEdit(Ollama("Local"), previousName: null);
        profiles.CommitEdit(
            new ModelProfile { Name = "Cloud", Provider = "OpenAI", Model = "gpt-4.1-mini", BaseUrl = "https://api.openai.com/v1" },
            previousName: null);

        profiles.SetDefault("Cloud");

        Assert.Equal("Cloud", profiles.DefaultProfileName);
        Assert.Equal("gpt-4.1-mini", llm.Model);
        Assert.Equal("https://api.openai.com/v1", llm.BaseUrl);
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

    // ── the tab gating ──

    private static SettingsScreenViewModel Screen(UiModeViewModel mode)
    {
        var (profiles, _, _, _) = Build();
        var config = new ConfigTabViewModel(new FakeAppSettingsStore(), new FakeDirectoryProbe());
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
        Assert.Equal("deepseek-v4-flash", editor.Model);
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
        editor.StoreKeyCommand.Execute(null);
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

}

/// <summary>Novice auto-save and the profile usage chips (audit 07/16).</summary>
public sealed class SettingsRemediationTests
{
    [Fact]
    public void A_dirty_document_saves_itself_in_novice_mode_and_not_in_expert()
    {
        var store = new FakeAppSettingsStore();
        var novice = new ConfigTabViewModel(store, new FakeDirectoryProbe("/data"));
        _ = new SettingsScreenViewModel(
            novice,
            new ModelProfilesViewModel(new InMemoryModelProfileStore(), novice.Llm),
            new UiModeViewModel("novice"));

        novice.Mounts.AddMount().PhysicalPath = "/data";
        novice.Llm.Model = "phi3";

        // The edit marked the document dirty; the novice screen saved it by itself.
        Assert.NotEmpty(store.SavedPaths);

        var expertStore = new FakeAppSettingsStore();
        var expert = new ConfigTabViewModel(expertStore, new FakeDirectoryProbe("/data"));
        _ = new SettingsScreenViewModel(
            expert,
            new ModelProfilesViewModel(new InMemoryModelProfileStore(), expert.Llm),
            new UiModeViewModel("expert"));

        expert.Mounts.AddMount().PhysicalPath = "/data";
        expert.Llm.Model = "phi3";

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

        var config = new ConfigTabViewModel(new FakeAppSettingsStore(), new FakeDirectoryProbe());
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

/// <summary>The Réglages "Clés API" card: env-var rows, remember flow, no file ever.</summary>
public sealed class SecretsCardTests
{
    private sealed class RecordingKeyStore : IApiKeyStore
    {
        public Dictionary<string, string> Saved { get; } = new(StringComparer.Ordinal);
        public string? Peek(string envName) => Saved.TryGetValue(envName, out var v) ? v : null;
        public void Save(string envName, string value) => Saved[envName] = value;
    }

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

        var keys = new RecordingKeyStore();
        var config = new ConfigTabViewModel(new FakeAppSettingsStore(), new FakeDirectoryProbe());
        var profiles = new ModelProfilesViewModel(store, config.Llm, keyStore: keys);
        await profiles.InitializeAsync(TestContext.Current.CancellationToken);

        // The keyless local profile contributes no row; the two DeepSeek profiles share one.
        var row = Assert.Single(profiles.Secrets);
        Assert.Equal("DEEPSEEK_API_KEY", row.EnvName);
        Assert.Contains("DeepSeek rapide", row.UsedBy, StringComparison.Ordinal);
        Assert.False(row.HasKey);

        row.KeyInput = "  sk-test-123  ";
        Assert.True(row.StoreCommand.CanExecute(null));
        row.StoreCommand.Execute(null);

        Assert.Equal("sk-test-123", keys.Saved["DEEPSEEK_API_KEY"]);
        Assert.Equal("", row.KeyInput);   // the pasted key does not linger on screen
        Assert.True(row.HasKey);
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

        var config = new ConfigTabViewModel(new FakeAppSettingsStore(), new FakeDirectoryProbe());
        var profiles = new ModelProfilesViewModel(store, config.Llm);
        await profiles.InitializeAsync(TestContext.Current.CancellationToken);

        var resets = 0;
        profiles.ProfileNames.CollectionChanged += (_, _) => resets++;

        profiles.StudioProfileName = "DeepSeek";

        Assert.Equal(0, resets); // the ComboBox's ItemsSource was never disturbed
        Assert.Equal("DeepSeek", profiles.StudioProfileName);
        Assert.True(profiles.HasStudioProfile);
        Assert.Equal("DeepSeek", (await store.LoadAsync(TestContext.Current.CancellationToken)).StudioProfile);
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

        var config = new ConfigTabViewModel(new FakeAppSettingsStore(), new FakeDirectoryProbe());
        var profiles = new ModelProfilesViewModel(store, config.Llm);
        await profiles.InitializeAsync(TestContext.Current.CancellationToken);

        profiles.BeginEdit(profiles.Set.Profiles[0]);
        Assert.Equal("1", profiles.Editor!.TemperatureText);

        profiles.Editor.TemperatureText = "0,7"; // a French keyboard types the comma
        Assert.Equal(0.7, profiles.Editor.ParsedTemperature);

        profiles.Editor.TimeoutText = "180";
        Assert.Equal(180, profiles.Editor.ParsedTimeoutSeconds);

        profiles.Editor.SaveCommand.Execute(null);
        var saved = (await store.LoadAsync(TestContext.Current.CancellationToken)).Profiles[0];
        Assert.Equal(0.7, saved.Temperature);
        Assert.Equal(180, saved.TimeoutSeconds);
    }
}
