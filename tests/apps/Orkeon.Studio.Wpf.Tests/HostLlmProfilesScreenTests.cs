using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-48 on the model-settings screen: every setting is a host profile of the settings file —
/// written without its key, renamed and removed with it —, the editor and the cards show the name
/// a crew writes, an entry written by hand is shown read-only and never touched, the RAG's model is
/// chosen among the profiles, and every launch carries the profiles' keys. STUDIO-49: each entry,
/// and the elected default written whole, names the variable holding its key, which a run outside
/// Studio reads; a team on another setting never carries the default's key.
/// </summary>
public sealed class HostLlmProfilesScreenTests
{
    private const string HandWrittenFile = """
        {
          "Llm": {
            "BaseUrl": "http://localhost:11434", "Model": "qwen3",
            "Profiles": { "local-gpu": { "BaseUrl": "http://localhost:11500", "Model": "qwen3:32b", "MaxRetries": 2 } }
          }
        }
        """;

    private static (ModelProfilesViewModel Profiles, AppSettingsDocument Document, FakeApiKeyStore Keys, List<int> Edits) Build(
        string json = HandWrittenFile)
    {
        var document = AppSettingsDocument.Parse(json);
        var edits = new List<int>();
        var llm = new LlmSectionViewModel(() => document, () => edits.Add(edits.Count));
        var keys = new FakeApiKeyStore();
        var profiles = new ModelProfilesViewModel(
            new InMemoryModelProfileStore(), llm, probe: new FakeLlmEndpointProbe(), keyStore: keys);
        return (profiles, document, keys, edits);
    }

    private static void Create(ModelProfilesViewModel profiles, string name, string provider, string? key = null)
    {
        profiles.NewProfileCommand.Execute(null);
        var editor = profiles.Editor!;
        editor.SelectedProvider = editor.Providers.First(p => p.Name == provider);
        editor.Name = name;
        if (key is not null)
            editor.ApiKeyInput = key;
        Assert.True(editor.SaveCommand.CanExecute(null), editor.HostIdText);
        editor.SaveCommand.Execute(null);
    }

    [Fact]
    public void Two_settings_created_in_the_editor_are_two_host_profiles_of_the_file_without_their_keys()
    {
        var (profiles, document, keys, edits) = Build();

        Create(profiles, "DeepSeek", LlmPresets.DeepSeek, key: "sk-ds");
        Create(profiles, "Z.AI", LlmPresets.Zai, key: "sk-zai");

        Assert.Equal(["local-gpu", "deepseek", "z-ai"], document.Llm.Profiles.Ids);
        Assert.Equal("https://api.deepseek.com", document.Llm.Profiles.Get("deepseek")?.BaseUrl);
        Assert.Equal("sk-ds", keys.Saved["DEEPSEEK_API_KEY"]);
        // STUDIO-49: two references — the variables Studio remembers the keys in — and no key.
        Assert.Equal("DEEPSEEK_API_KEY", document.Llm.Profiles.Get("deepseek")?.ApiKeyEnvVar);
        Assert.Equal("ZAI_API_KEY", document.Llm.Profiles.Get("z-ai")?.ApiKeyEnvVar);
        Assert.DoesNotContain("sk-", document.ToJson(), StringComparison.Ordinal);
        KeyTripwire.AssertNamesNoKey(document.ToJson(), "DEEPSEEK_API_KEY", "ZAI_API_KEY");
        // The writes go through the screen's edit cycle: the novice auto-save hears them.
        Assert.NotEmpty(edits);
    }

    [Fact]
    public void The_election_writes_the_default_whole_its_reference_and_its_600_seconds_included()
    {
        var (profiles, document, _, _) = Build();
        Create(profiles, "Local", LlmPresets.Ollama);
        Create(profiles, "DeepSeek", LlmPresets.DeepSeek, key: "sk-ds");
        Assert.Null(document.Llm.ApiKeyEnvVar);

        profiles.SetDefault("DeepSeek");

        Assert.Equal("https://api.deepseek.com", document.Llm.BaseUrl);
        Assert.Equal("DEEPSEEK_API_KEY", document.Llm.ApiKeyEnvVar);
        Assert.Equal(LlmPresets.ReasoningTimeoutSeconds, document.Llm.TimeoutSeconds);

        // Back to a keyless setting: its reference and its timeout go with the election.
        profiles.SetDefault("Local");
        Assert.Null(document.Llm.ApiKeyEnvVar);
        Assert.Null(document.Llm.TimeoutSeconds);
        Assert.Equal("http://localhost:11434", document.Llm.BaseUrl);
    }

    [Fact]
    public async Task A_default_elected_before_the_reference_is_healed_at_the_next_gesture_never_at_startup()
    {
        // A file and a store from before STUDIO-49: the election wrote the model and the endpoint alone.
        var (profiles, document, _, edits) = Build("""
            { "Llm": { "BaseUrl": "https://api.deepseek.com", "Model": "deepseek-v4-flash" } }
            """);
        var store = new InMemoryModelProfileStore();
        await store.SaveAsync(ModelProfileSet.Empty.Upsert(new ModelProfile
        {
            Name = "DeepSeek", Provider = "DeepSeek", BaseUrl = "https://api.deepseek.com", Model = "deepseek-v4-flash",
            KeyEnvName = "DEEPSEEK_API_KEY", TimeoutSeconds = 600,
        }), TestContext.Current.CancellationToken);
        var llm = new LlmSectionViewModel(() => document, () => edits.Add(edits.Count));
        profiles = new ModelProfilesViewModel(store, llm, probe: new FakeLlmEndpointProbe(), keyStore: new FakeApiKeyStore());

        await profiles.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Null(document.Llm.ApiKeyEnvVar);
        Assert.Empty(edits);

        profiles.StudioProfileName = "DeepSeek";

        Assert.Equal("DEEPSEEK_API_KEY", document.Llm.ApiKeyEnvVar);
        Assert.Equal(600, document.Llm.TimeoutSeconds);
        Assert.Equal("DEEPSEEK_API_KEY", document.Llm.Profiles.Get("deepseek")?.ApiKeyEnvVar);
        Assert.NotEmpty(edits);
    }

    [Fact]
    public void Renaming_then_deleting_a_setting_moves_then_removes_its_entry_and_the_hand_written_one_stays()
    {
        var (profiles, document, _, _) = Build();
        Create(profiles, "DeepSeek", LlmPresets.DeepSeek);
        Create(profiles, "Z.AI", LlmPresets.Zai);

        profiles.BeginEdit(profiles.Set.Find("DeepSeek")!);
        profiles.Editor!.Name = "DeepSeek raisonneur";
        profiles.Editor.SaveCommand.Execute(null);
        Assert.Equal(["local-gpu", "z-ai", "deepseek-raisonneur"], document.Llm.Profiles.Ids);

        profiles.Delete("DeepSeek raisonneur");
        Assert.Equal(["local-gpu", "z-ai"], document.Llm.Profiles.Ids);
        Assert.Equal(2, document.GetInt32("Llm:Profiles:local-gpu:MaxRetries"));
        Assert.Equal("qwen3:32b", document.Llm.Profiles.Get("local-gpu")?.Model);
    }

    [Fact]
    public void A_duplicate_is_a_host_profile_of_its_own()
    {
        var (profiles, document, _, _) = Build();
        Create(profiles, "DeepSeek", LlmPresets.DeepSeek);

        profiles.Duplicate(profiles.Set.Find("DeepSeek")!);

        Assert.Equal(["local-gpu", "deepseek", "deepseek-copy"], document.Llm.Profiles.Ids);
    }

    [Fact]
    public void The_editor_shows_the_name_a_crew_writes_as_the_name_is_typed()
    {
        var (profiles, _, _, _) = Build();
        profiles.NewProfileCommand.Execute(null);
        var editor = profiles.Editor!;
        editor.SelectedProvider = editor.Providers.First(p => p.Name == LlmPresets.Anthropic);

        editor.Name = "Claude Opus";

        Assert.Equal("In a crew file: profile: claude-opus", editor.HostIdText);
        Assert.False(editor.IsHostIdIssue);
        // STUDIO-49: the variable a run outside Studio reads is the one Studio remembers the key in.
        Assert.Equal(
            "Outside Studio — a terminal, a scheduled team — every run reads this setting's key from ANTHROPIC_API_KEY: " +
            "the settings file names that variable (ApiKeyEnvVar), never the key.",
            editor.HostKeyHint);
    }

    [Fact]
    public void The_key_hint_names_the_variable_of_the_card_picked()
    {
        var (profiles, _, _, _) = Build();
        profiles.NewProfileCommand.Execute(null);
        var editor = profiles.Editor!;
        editor.Name = "GLM";

        editor.SelectedProvider = editor.Providers.First(p => p.Name == LlmPresets.Zai);

        Assert.Contains("ZAI_API_KEY", editor.HostKeyHint, StringComparison.Ordinal);
        editor.SelectedProvider = editor.Providers.First(p => p.Name == LlmPresets.Ollama);
        Assert.Null(editor.HostKeyHint);
    }

    [Fact]
    public async Task A_compatible_openai_setting_keeping_its_key_in_the_defaults_variable_says_so_unless_it_is_the_default()
    {
        // Decision 7: before STUDIO-49 the catch-all kept its key in ORKEON_Llm__ApiKey — the
        // runtime's key of the default. Such a setting keeps its variable; its card says what it means.
        var (_, document, keys, edits) = Build();
        var store = new InMemoryModelProfileStore();
        await store.SaveAsync(ModelProfileSet.Empty
            .Upsert(new ModelProfile { Name = "DeepSeek", Provider = "DeepSeek", BaseUrl = "https://api.deepseek.com", Model = "deepseek-chat", KeyEnvName = "DEEPSEEK_API_KEY" })
            .Upsert(new ModelProfile { Name = "Box", Provider = "Other OpenAI-compatible", BaseUrl = "https://llm.example.com/v1", Model = "m", KeyEnvName = LlmPresets.DefaultApiKeyEnv }),
            TestContext.Current.CancellationToken);
        var llm = new LlmSectionViewModel(() => document, () => edits.Add(edits.Count));
        var profiles = new ModelProfilesViewModel(store, llm, probe: new FakeLlmEndpointProbe(), keyStore: keys);
        await profiles.InitializeAsync(TestContext.Current.CancellationToken);

        var box = profiles.Profiles.Single(p => p.Name == "Box");
        Assert.True(box.HasDefaultKeyVariableWarning);
        Assert.Contains("ORKEON_Llm__ApiKey", box.DefaultKeyVariableWarning, StringComparison.Ordinal);
        Assert.Contains("ORKEON_CUSTOM_LLM_API_KEY", box.DefaultKeyVariableWarning, StringComparison.Ordinal);
        Assert.False(profiles.Profiles.Single(p => p.Name == "DeepSeek").HasDefaultKeyVariableWarning);

        profiles.SetDefault("Box");
        Assert.False(profiles.Profiles.Single(p => p.Name == "Box").HasDefaultKeyVariableWarning);

        // A setting created from the card today keeps its key in a variable of its own.
        profiles.NewProfileCommand.Execute(null);
        var editor = profiles.Editor!;
        editor.SelectedProvider = editor.Providers.First(p => p.Name == LlmPresets.Custom);
        Assert.Equal(LlmPresets.CustomApiKeyEnv, editor.ApiKeyEnvName);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("Default")]
    public void The_editor_refuses_the_name_default(string name)
    {
        var (profiles, _, _, _) = Build();
        profiles.NewProfileCommand.Execute(null);
        var editor = profiles.Editor!;

        editor.Name = name;

        Assert.False(editor.CanSave);
        Assert.True(editor.IsHostIdIssue);
        Assert.Equal(
            "A crew would write profile: default, the name reserved for the default setting — choose another name.",
            editor.HostIdText);
    }

    [Fact]
    public void The_editor_refuses_a_name_that_would_take_over_an_entry_written_by_hand()
    {
        var (profiles, document, _, _) = Build();
        profiles.NewProfileCommand.Execute(null);
        var editor = profiles.Editor!;
        editor.SelectedProvider = editor.Providers.First(p => p.Name == LlmPresets.Ollama);

        editor.Name = "Local GPU";

        Assert.False(editor.CanSave);
        Assert.Contains("profile: local-gpu", editor.HostIdText, StringComparison.Ordinal);
        editor.SaveCommand.Execute(null);
        Assert.Equal("qwen3:32b", document.Llm.Profiles.Get("local-gpu")?.Model);
    }

    [Fact]
    public void The_editor_refuses_a_name_another_setting_already_answers_to()
    {
        var (profiles, _, _, _) = Build();
        Create(profiles, "DeepSeek", LlmPresets.DeepSeek);
        profiles.NewProfileCommand.Execute(null);
        var editor = profiles.Editor!;

        editor.Name = "deepseek";

        Assert.False(editor.CanSave);
        Assert.Equal("The setting “DeepSeek” already answers to profile: deepseek — choose another name.", editor.HostIdText);
    }

    [Fact]
    public void A_name_without_a_latin_letter_is_saved_and_says_no_crew_can_name_it()
    {
        var (profiles, document, _, _) = Build();
        profiles.NewProfileCommand.Execute(null);
        var editor = profiles.Editor!;
        editor.SelectedProvider = editor.Providers.First(p => p.Name == LlmPresets.DeepSeek);

        editor.Name = "深度求索";

        Assert.True(editor.CanSave);
        Assert.True(editor.IsHostIdIssue);
        Assert.Equal("No crew can name this setting: its name keeps no Latin letter or digit.", editor.HostIdText);
        editor.SaveCommand.Execute(null);
        Assert.Equal(["local-gpu"], document.Llm.Profiles.Ids);
        Assert.True(Assert.Single(profiles.Profiles).IsHostIdIssue);
    }

    [Fact]
    public void Renaming_an_offered_setting_warns_that_crews_writing_the_old_name_stop_loading()
    {
        var (profiles, _, _, _) = Build();
        Create(profiles, "DeepSeek", LlmPresets.DeepSeek);
        profiles.BeginEdit(profiles.Set.Find("DeepSeek")!);
        var editor = profiles.Editor!;
        Assert.Null(editor.HostIdRenamedText);

        editor.Name = "DS";

        Assert.Equal(
            "Renamed, the setting gets a new crew name: a crew that still writes profile: deepseek will no longer load.",
            editor.HostIdRenamedText);
        editor.Name = "Deepseek";
        Assert.Null(editor.HostIdRenamedText);
    }

    [Fact]
    public void Each_card_shows_the_name_a_crew_writes()
    {
        var (profiles, _, _, _) = Build();
        Create(profiles, "Z.AI", LlmPresets.Zai);

        var card = Assert.Single(profiles.Profiles);

        Assert.Equal("z-ai", card.HostProfileId);
        Assert.Equal("In a crew file: profile: z-ai", card.HostIdText);
        Assert.False(card.IsHostIdIssue);
    }

    [Fact]
    public void An_entry_written_by_hand_is_listed_read_only()
    {
        var (profiles, _, _, _) = Build();
        Create(profiles, "DeepSeek", LlmPresets.DeepSeek);

        var row = Assert.Single(profiles.HandWrittenProfiles);

        Assert.True(profiles.HasHandWrittenProfiles);
        Assert.Equal("local-gpu", row.Id);
        Assert.Equal("qwen3:32b · http://localhost:11500", row.Summary);
        Assert.Equal("In a crew file: profile: local-gpu", row.HostIdText);
        // Read-only: the row offers no gesture at all.
        Assert.DoesNotContain(row.GetType().GetProperties(), p => typeof(System.Windows.Input.ICommand).IsAssignableFrom(p.PropertyType));
    }

    [Fact]
    public async Task Loading_another_settings_file_lists_its_hand_written_entries()
    {
        var store = new FakeAppSettingsStore();
        store.Files["/cfg/appsettings.json"] = HandWrittenFile;
        var config = new ConfigTabViewModel(new StudioServices { SettingsStore = store, Directories = new FakeDirectoryProbe() });
        var profiles = new ModelProfilesViewModel(new InMemoryModelProfileStore(), config.Llm);
        Assert.Empty(profiles.HandWrittenProfiles);

        await config.LoadAsync("/cfg/appsettings.json", TestContext.Current.CancellationToken);

        Assert.Equal("local-gpu", Assert.Single(profiles.HandWrittenProfiles).Id);
        Assert.Contains(profiles.RagProfileChoices, choice => choice.Id == "local-gpu");
    }

    [Fact]
    public void The_rag_model_is_chosen_among_the_profiles_and_follows_its_setting()
    {
        var (profiles, document, _, _) = Build();
        Create(profiles, "DeepSeek", LlmPresets.DeepSeek);

        Assert.Equal([null, "deepseek", "local-gpu"], profiles.RagProfileChoices.Select(c => c.Id));
        Assert.Equal("default — the setting marked default", profiles.RagProfileChoices[0].Label);
        Assert.Null(profiles.SelectedRagProfile!.Id);

        profiles.SelectedRagProfile = profiles.RagProfileChoices.Single(c => c.Id == "deepseek");
        Assert.Equal("deepseek", document.Rag.LlmProfile);

        profiles.BeginEdit(profiles.Set.Find("DeepSeek")!);
        profiles.Editor!.Name = "DS";
        profiles.Editor.SaveCommand.Execute(null);
        Assert.Equal("ds", document.Rag.LlmProfile);
        Assert.Equal("ds", profiles.SelectedRagProfile!.Id);

        Create(profiles, "Z.AI", LlmPresets.Zai);   // the last setting cannot be deleted
        Assert.Equal([null, "ds", "z-ai", "local-gpu"], profiles.RagProfileChoices.Select(c => c.Id));
        profiles.Delete("DS");
        Assert.Null(document.Rag.LlmProfile);
        Assert.Null(profiles.SelectedRagProfile!.Id);
    }

    [Fact]
    public async Task Choosing_the_default_for_the_rag_removes_its_key()
    {
        var (profiles, document, _, _) = Build();
        await profiles.InitializeAsync(TestContext.Current.CancellationToken);
        profiles.SelectedRagProfile = profiles.RagProfileChoices.Single(c => c.Id == "local-gpu");
        Assert.Equal("local-gpu", document.Rag.LlmProfile);

        profiles.SelectedRagProfile = profiles.RagProfileChoices[0];

        Assert.False(document.ContainsPath("Orkeon:Rag:LlmProfile"));
    }

    [Fact]
    public void A_launch_carries_every_setting_and_its_remembered_key()
    {
        var (profiles, _, keys, _) = Build();
        Create(profiles, "DeepSeek", LlmPresets.DeepSeek, key: "sk-ds");
        Create(profiles, "Z.AI", LlmPresets.Zai);
        keys.Stage("ZAI_API_KEY", "sk-zai");

        var team = profiles.LaunchEnvironmentOf(profiles.Set.Find("Z.AI")!);
        var bare = profiles.LaunchEnvironment();

        Assert.Equal("sk-ds", team["ORKEON_Llm__Profiles__deepseek__ApiKey"]);
        Assert.Equal("sk-zai", team["ORKEON_Llm__Profiles__z-ai__ApiKey"]);
        Assert.Equal("glm-5.2", team["ORKEON_Llm__Model"]);   // the team's own election, as before
        Assert.Equal("sk-zai", team["ORKEON_Llm__ApiKey"]);
        Assert.Equal("ZAI_API_KEY", team["ORKEON_Llm__ApiKeyEnvVar"]);
        Assert.Equal("sk-ds", bare["ORKEON_Llm__Profiles__deepseek__ApiKey"]);
        Assert.False(bare.ContainsKey("ORKEON_Llm__Model"));
    }

    [Fact]
    public void A_team_on_another_setting_whose_key_is_not_remembered_carries_no_key_of_the_default()
    {
        // Decision 4: blank, not absent — the DeepSeek key of the default must never reach Z.AI.
        var (profiles, _, _, _) = Build();
        Create(profiles, "DeepSeek", LlmPresets.DeepSeek, key: "sk-ds");
        Create(profiles, "Z.AI", LlmPresets.Zai);

        var team = profiles.LaunchEnvironmentOf(profiles.Set.Find("Z.AI")!);

        Assert.Equal("", team["ORKEON_Llm__ApiKey"]);
        Assert.Equal("ZAI_API_KEY", team["ORKEON_Llm__ApiKeyEnvVar"]);
        Assert.DoesNotContain(team, entry => entry.Key.StartsWith("ORKEON_Llm__", StringComparison.Ordinal)
            && !entry.Key.StartsWith("ORKEON_Llm__Profiles__", StringComparison.Ordinal)
            && entry.Value == "sk-ds");

        // The default's own launch lays what it sets, and nothing blank over the section it already is.
        var onDefault = profiles.LaunchEnvironmentOf(profiles.Set.Find("DeepSeek")!);
        Assert.Equal("sk-ds", onDefault["ORKEON_Llm__ApiKey"]);
        Assert.DoesNotContain(onDefault.Values, value => value.Length == 0);
    }

    [Fact]
    public async Task A_run_of_a_crew_that_is_no_team_carries_the_profiles_too()
    {
        var launcher = new FakeProcessLauncher();
        var keys = new FakeApiKeyStore();
        keys.Stage("DEEPSEEK_API_KEY", "sk-ds");
        var root = Path.Combine(Path.GetTempPath(), "orkeon-studio48-" + Guid.NewGuid().ToString("N"));
        try
        {
            var window = new MainWindowViewModel(
                new StudioServices
                {
                    SettingsStore = new FakeAppSettingsStore(),
                    Directories = new FakeDirectoryProbe(),
                    TargetProbe = new FakeTargetProbe().WithFile("/crews/team.yaml"),
                    Picker = new FakePathPicker(),
                    ProcessRunner = new OrkeonProcessRunner(
                        launcher, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                    HistoryStore = new FakeLaunchHistoryStore(),
                    KeyStore = keys,
                },
                globalPathOverride: Path.Combine(root, "appsettings.json"),
                forgeWorkspace: Path.Combine(root, "forge"),
                teamsRoot: Path.Combine(root, "teams"));
            window.Settings.Profiles.CommitEdit(
                new ModelProfile
                {
                    Name = "DeepSeek", Provider = "DeepSeek", BaseUrl = "https://api.deepseek.com",
                    Model = "deepseek-v4-flash", KeyEnvName = "DEEPSEEK_API_KEY",
                },
                previousName: null);
            window.Launch.Target.Select("/crews/team.yaml");

            await window.Launch.RunAsync(TestContext.Current.CancellationToken);

            Assert.Equal("sk-ds", launcher.LastRequest!.Environment["ORKEON_Llm__Profiles__deepseek__ApiKey"]);
            Assert.Equal("deepseek-v4-flash", launcher.LastRequest.Environment["ORKEON_Llm__Profiles__deepseek__Model"]);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_validation_overlay_names_the_rag_profile_problem_in_plain_words()
    {
        Assert.Equal(
            "The document search names a model profile this file does not define.",
            EnglishStudioStrings.Instance["Studio.Diagnostics.Code." + Orkeon.Studio.Core.Validation.ValidationCodes.UnknownRagLlmProfile]);
    }
}
