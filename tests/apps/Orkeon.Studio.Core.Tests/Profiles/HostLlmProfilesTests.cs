using Orkeon.Constants.Llm;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Profiles;

/// <summary>
/// STUDIO-48: Studio's model settings are the host's LLM profiles. Each one that names a provider
/// is mirrored into <c>Llm:Profiles:&lt;slug&gt;</c> without its key, follows its setting through a
/// rename and a removal, and reaches a launch with its key through the environment; an entry
/// written by hand is never touched, and <c>default</c> is refused as a name. STUDIO-49: each
/// entry names the variable holding its key (<c>ApiKeyEnvVar</c>), which a run outside Studio
/// reads, and the elected setting is written whole into the <c>Llm</c> section.
/// </summary>
public sealed class HostLlmProfilesTests
{
    private static ModelProfile DeepSeek(string name = "DeepSeek") => new()
    {
        Name = name,
        Provider = "DeepSeek",
        BaseUrl = "https://api.deepseek.com",
        Model = "deepseek-v4-flash",
        KeyEnvName = "DEEPSEEK_API_KEY",
        TimeoutSeconds = 600,
        StreamIdleSeconds = 45,
    };

    private static ModelProfile Zai(string name = "Z.AI") => new()
    {
        Name = name,
        Provider = "Z.AI",
        BaseUrl = "https://api.z.ai/api/paas/v4",
        Model = "glm-5.2",
        KeyEnvName = "ZAI_API_KEY",
        ThinkingEnabled = false,
    };

    private static ModelProfile Echo(string name) => new() { Name = name, Provider = "None" };

    private static ModelProfile Ollama(string name = "Local") => new()
    {
        Name = name,
        Provider = "Ollama",
        BaseUrl = "http://localhost:11434",
        Model = "qwen3",
    };

    private static ModelProfile Docker(string name = "Docker") => new()
    {
        Name = name,
        Provider = LlmPresets.DockerModelRunner,
        BaseUrl = LlmPresets.DockerModelRunnerBaseUrl,
        Model = LlmPresets.DockerModelRunnerDefaultModel,
    };

    /// <summary>What <c>orkeon init --preset docker-model-runner</c> writes.</summary>
    private const string InitDockerFile = """
        { "Llm": { "Model": "ai/granite-4.0-h-tiny", "BaseUrl": "http://localhost:12434/engines/llama.cpp/v1", "ApiKey": "not-needed" } }
        """;

    /// <summary>A settings file with a default, a profile written by hand and the RAG on none.</summary>
    private static AppSettingsDocument HandWrittenDocument() => AppSettingsDocument.Parse("""
        {
          "Llm": {
            "BaseUrl": "http://localhost:11434", "Model": "qwen3",
            "Profiles": { "local-gpu": { "BaseUrl": "http://localhost:11500", "Model": "qwen3:32b", "MaxRetries": 2, "ApiKeyEnvVar": "GPU_BOX_KEY" } }
          }
        }
        """);

    private static (AppSettingsDocument Document, ModelProfileSet Set) TwoSettings()
    {
        var document = HandWrittenDocument();
        var set = ModelProfileSet.Empty.Upsert(DeepSeek()).Upsert(Zai());
        HostLlmProfiles.Mirror(document, ModelProfileSet.Empty, set);
        return (document, set);
    }

    [Fact]
    public void The_id_is_the_name_through_the_folder_name_rule_of_the_teams()
    {
        Assert.Equal("claude", ModelProfile.HostProfileIdOf("Claude"));
        Assert.Equal("z-ai", ModelProfile.HostProfileIdOf("Z.AI"));
        Assert.Equal("deepseek-v4-raisonneur", ModelProfile.HostProfileIdOf("DeepSeek V4 (raisonneur)"));
        Assert.Equal("modele-local", ModelProfile.HostProfileIdOf("Modèle local"));
        Assert.Null(ModelProfile.HostProfileIdOf("深度求索"));
        Assert.Equal("z-ai", Zai().HostProfileId);
    }

    [Fact]
    public void Creating_two_settings_writes_their_two_ids_without_a_key()
    {
        var (document, _) = TwoSettings();

        Assert.Equal(["local-gpu", "deepseek", "z-ai"], document.Llm.Profiles.Ids);
        var deepseek = document.Llm.Profiles.Get("deepseek");
        Assert.Equal("https://api.deepseek.com", deepseek?.BaseUrl);
        Assert.Equal("deepseek-v4-flash", deepseek?.Model);
        Assert.Equal(600, deepseek?.TimeoutSeconds);
        Assert.Equal(45, deepseek?.StreamIdleSeconds);
        Assert.False(document.Llm.Profiles.Get("z-ai")?.ThinkingEnabled);
        // STUDIO-49: each entry names the variable holding its key — a run outside Studio reads
        // it — and no key is written anywhere.
        Assert.Equal("DEEPSEEK_API_KEY", deepseek?.ApiKeyEnvVar);
        Assert.Equal("ZAI_API_KEY", document.Llm.Profiles.Get("z-ai")?.ApiKeyEnvVar);
        KeyTripwire.AssertNamesNoKey(document.ToJson(), "DEEPSEEK_API_KEY", "ZAI_API_KEY", "GPU_BOX_KEY");
        // The default stays the Llm section's own: a profile is not an election.
        Assert.Equal("qwen3", document.Llm.Model);
    }

    [Fact]
    public void A_setting_that_needs_no_key_names_no_variable_and_loses_one_its_entry_had()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var set = ModelProfileSet.Empty.Upsert(Ollama());
        HostLlmProfiles.Mirror(document, ModelProfileSet.Empty, set);
        Assert.Null(document.Llm.Profiles.Get("local")?.ApiKeyEnvVar);

        // An entry carrying a reference its keyless setting does not: rewritten at the next gesture.
        document.SetString("Llm:Profiles:local:ApiKeyEnvVar", "STALE_KEY");
        Assert.True(HostLlmProfiles.Mirror(document, set, set.WithStudio("Local")));

        Assert.False(document.ContainsPath("Llm:Profiles:local:ApiKeyEnvVar"));
    }

    [Fact]
    public void An_owned_entry_written_before_the_reference_is_healed_at_the_next_gesture()
    {
        // A file mirrored by STUDIO-48: the entries name no variable.
        var (document, set) = TwoSettings();
        document.Remove("Llm:Profiles:deepseek:ApiKeyEnvVar");
        document.Remove("Llm:Profiles:z-ai:ApiKeyEnvVar");

        Assert.True(HostLlmProfiles.Mirror(document, set, set.WithStudio("DeepSeek")));

        Assert.Equal("DEEPSEEK_API_KEY", document.Llm.Profiles.Get("deepseek")?.ApiKeyEnvVar);
        Assert.Equal("ZAI_API_KEY", document.Llm.Profiles.Get("z-ai")?.ApiKeyEnvVar);
        // Healed once: the next gesture finds nothing to write.
        Assert.False(HostLlmProfiles.Mirror(document, set, set.WithStudio("Z.AI")));
    }

    [Fact]
    public void Renaming_a_setting_moves_its_entry_and_what_studio_does_not_model_travels_with_it()
    {
        var (document, set) = TwoSettings();
        document.SetInt32("Llm:Profiles:deepseek:MaxRetries", 3);
        var renamed = set.Upsert(DeepSeek("DeepSeek raisonneur"), previousName: "DeepSeek");

        Assert.True(HostLlmProfiles.Mirror(document, set, renamed, "DeepSeek", "DeepSeek raisonneur"));

        Assert.Equal(["local-gpu", "z-ai", "deepseek-raisonneur"], document.Llm.Profiles.Ids);
        Assert.Equal("deepseek-v4-flash", document.Llm.Profiles.Get("deepseek-raisonneur")?.Model);
        Assert.Equal(3, document.GetInt32("Llm:Profiles:deepseek-raisonneur:MaxRetries"));
        // The reference to the key's variable travels with the entry.
        Assert.Equal("DEEPSEEK_API_KEY", document.Llm.Profiles.Get("deepseek-raisonneur")?.ApiKeyEnvVar);
    }

    [Fact]
    public void Editing_a_setting_rewrites_its_entry_in_place()
    {
        var (document, set) = TwoSettings();
        var edited = set.Upsert(Zai() with { Model = "glm-5.3", ThinkingEnabled = null }, previousName: "Z.AI");

        HostLlmProfiles.Mirror(document, set, edited, "Z.AI", "Z.AI");

        var entry = document.Llm.Profiles.Get("z-ai");
        Assert.Equal("glm-5.3", entry?.Model);
        Assert.Null(entry?.ThinkingEnabled);
    }

    [Fact]
    public void Removing_a_setting_removes_its_entry()
    {
        var (document, set) = TwoSettings();

        HostLlmProfiles.Mirror(document, set, set.Remove("Z.AI"));

        Assert.Equal(["local-gpu", "deepseek"], document.Llm.Profiles.Ids);
        // Its reference went with it; the one written by hand stays.
        KeyTripwire.AssertNamesNoKey(document.ToJson(), "DEEPSEEK_API_KEY", "GPU_BOX_KEY");
    }

    [Fact]
    public void An_entry_written_by_hand_survives_every_change_and_is_listed_as_such()
    {
        var (document, set) = TwoSettings();
        var renamed = set.Upsert(DeepSeek("DS"), previousName: "DeepSeek");
        HostLlmProfiles.Mirror(document, set, renamed, "DeepSeek", "DS");
        var removed = renamed.Remove("DS").Remove("Z.AI");
        HostLlmProfiles.Mirror(document, renamed, removed);

        Assert.Equal(["local-gpu"], document.Llm.Profiles.Ids);
        // Untouched, the reference written by hand included.
        Assert.Equal("""{"BaseUrl":"http://localhost:11500","Model":"qwen3:32b","MaxRetries":2,"ApiKeyEnvVar":"GPU_BOX_KEY"}""",
            document.GetNode("Llm:Profiles:local-gpu")!.ToJsonString());
        var handWritten = Assert.Single(HostLlmProfiles.HandWritten(document, removed));
        Assert.Equal("local-gpu", handWritten.Id);
        Assert.Equal("qwen3:32b · http://localhost:11500", handWritten.Summary);
        Assert.Equal("qwen3", document.Llm.Model);
    }

    [Fact]
    public void An_entry_a_setting_offers_is_not_listed_as_hand_written()
    {
        var (document, set) = TwoSettings();

        Assert.Equal(["local-gpu"], HostLlmProfiles.HandWritten(document, set).Select(e => e.Id));
    }

    [Fact]
    public void A_change_that_touches_no_setting_writes_nothing()
    {
        var (document, set) = TwoSettings();
        var before = document.ToJson();

        Assert.False(HostLlmProfiles.Mirror(document, set, set.WithDefault("Z.AI").WithStudio("DeepSeek")));

        Assert.Equal(before, document.ToJson());
    }

    [Fact]
    public void A_setting_whose_entry_the_file_lacks_is_written_by_the_next_change()
    {
        // A file that never received a setting — another file loaded, an entry deleted by hand.
        var document = HandWrittenDocument();
        var set = ModelProfileSet.Empty.Upsert(DeepSeek()).Upsert(Zai());

        Assert.True(HostLlmProfiles.Mirror(document, set, set.WithDefault("Z.AI")));

        Assert.Equal(["local-gpu", "deepseek", "z-ai"], document.Llm.Profiles.Ids);
    }

    [Fact]
    public void A_setting_without_a_model_is_offered_to_no_crew()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var set = ModelProfileSet.Empty.Upsert(Echo("Echo"));

        Assert.False(HostLlmProfiles.Mirror(document, ModelProfileSet.Empty, set));

        Assert.Empty(document.Llm.Profiles.Ids);
        Assert.Equal(HostProfileStatus.NoProvider, HostLlmProfiles.Classify(set)["Echo"].Status);
    }

    [Fact]
    public void A_setting_switched_to_no_model_loses_its_entry()
    {
        var (document, set) = TwoSettings();
        var echoed = set.Upsert(Echo("Z.AI"), previousName: "Z.AI");

        HostLlmProfiles.Mirror(document, set, echoed, "Z.AI", "Z.AI");

        Assert.Equal(["local-gpu", "deepseek"], document.Llm.Profiles.Ids);
    }

    [Fact]
    public void The_rag_profile_follows_a_rename_and_falls_back_to_the_default_when_its_setting_goes()
    {
        var (document, set) = TwoSettings();
        document.Rag.LlmProfile = "deepseek";

        var renamed = set.Upsert(DeepSeek("DS"), previousName: "DeepSeek");
        HostLlmProfiles.Mirror(document, set, renamed, "DeepSeek", "DS");
        Assert.Equal("ds", document.Rag.LlmProfile);

        HostLlmProfiles.Mirror(document, renamed, renamed.Remove("DS"));
        Assert.Null(document.Rag.LlmProfile);
        Assert.False(document.ContainsPath("Orkeon:Rag:LlmProfile"));
    }

    [Fact]
    public void A_rag_profile_written_by_hand_stays_where_it_is()
    {
        var (document, set) = TwoSettings();
        document.Rag.LlmProfile = "local-gpu";

        HostLlmProfiles.Mirror(document, set, set.Remove("DeepSeek"));

        Assert.Equal("local-gpu", document.Rag.LlmProfile);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("Default")]
    [InlineData(" DEFAULT ")]
    public void The_name_default_is_refused(string name)
    {
        var check = HostLlmProfiles.Check(name, null, describesProvider: true, ModelProfileSet.Empty, AppSettingsDocument.CreateEmpty());

        Assert.Equal(HostProfileStatus.DefaultName, check.Status);
        Assert.True(check.BlocksSave);
    }

    [Fact]
    public void A_name_whose_id_another_setting_answers_to_is_refused_and_names_that_setting()
    {
        var (document, set) = TwoSettings();

        var check = HostLlmProfiles.Check("DEEPSEEK", null, describesProvider: true, set, document);

        Assert.Equal(HostProfileStatus.TakenBySetting, check.Status);
        Assert.Equal("deepseek", check.Id);
        Assert.Equal("DeepSeek", check.TakenBy);
        Assert.True(check.BlocksSave);
        // The setting being edited keeps its own id.
        Assert.True(HostLlmProfiles.Check("deepseek", "DeepSeek", describesProvider: true, set, document).IsOffered);
    }

    [Fact]
    public void A_name_whose_id_an_entry_written_by_hand_holds_is_refused()
    {
        var (document, set) = TwoSettings();

        var check = HostLlmProfiles.Check("Local GPU", null, describesProvider: true, set, document);

        Assert.Equal(HostProfileStatus.TakenByFile, check.Status);
        Assert.Equal("local-gpu", check.Id);
        Assert.True(check.BlocksSave);
        // Without a provider the setting takes nothing over: it is simply offered to no crew.
        Assert.False(HostLlmProfiles.Check("Local GPU", null, describesProvider: false, set, document).BlocksSave);
    }

    [Fact]
    public void A_name_without_a_latin_letter_or_digit_is_allowed_and_offered_to_no_crew()
    {
        var check = HostLlmProfiles.Check("深度求索", null, describesProvider: true, ModelProfileSet.Empty, AppSettingsDocument.CreateEmpty());

        Assert.Equal(HostProfileStatus.NoId, check.Status);
        Assert.Null(check.Id);
        Assert.False(check.BlocksSave);
    }

    [Fact]
    public void A_free_name_is_offered_under_its_id()
    {
        var (document, set) = TwoSettings();

        var check = HostLlmProfiles.Check("Claude", null, describesProvider: true, set, document);

        Assert.Equal(new HostProfileCheck(HostProfileStatus.Offered, "claude"), check);
    }

    [Fact]
    public void In_a_hand_edited_store_the_first_setting_to_answer_to_an_id_owns_it()
    {
        var set = new ModelProfileSet { Profiles = [DeepSeek(), DeepSeek("deepseek"), DeepSeek("Default")] };

        var standing = HostLlmProfiles.Classify(set);

        Assert.Equal(HostProfileStatus.Offered, standing["DeepSeek"].Status);
        Assert.Equal(HostProfileStatus.TakenBySetting, standing["deepseek"].Status);
        Assert.Equal("DeepSeek", standing["deepseek"].TakenBy);
        Assert.Equal(HostProfileStatus.DefaultName, standing["Default"].Status);
        var offered = Assert.Single(HostLlmProfiles.Offered(set));
        Assert.Equal("DeepSeek", offered.Key);
        Assert.Equal("deepseek", offered.Value);
    }

    [Fact]
    public void The_launch_carries_each_setting_whose_key_is_remembered_with_its_key()
    {
        var keys = new FakeApiKeyStore();
        keys.Stage("DEEPSEEK_API_KEY", "sk-ds");
        var set = ModelProfileSet.Empty.Upsert(DeepSeek()).Upsert(Zai()).Upsert(Echo("Echo"));

        var environment = HostLlmProfiles.LaunchEnvironment(set, keys.Peek);

        Assert.Equal("sk-ds", environment["ORKEON_Llm__Profiles__deepseek__ApiKey"]);
        Assert.Equal("https://api.deepseek.com", environment["ORKEON_Llm__Profiles__deepseek__BaseUrl"]);
        Assert.Equal("deepseek-v4-flash", environment["ORKEON_Llm__Profiles__deepseek__Model"]);
        Assert.Equal("600", environment["ORKEON_Llm__Profiles__deepseek__TimeoutSeconds"]);
        Assert.Equal("45", environment["ORKEON_Llm__Profiles__deepseek__StreamIdleSeconds"]);
        Assert.Equal("false", environment["ORKEON_Llm__Profiles__z-ai__Thinking__Enabled"]);
        // A key that is not remembered is not invented, and a setting offered to no crew rides nowhere.
        Assert.False(environment.ContainsKey("ORKEON_Llm__Profiles__z-ai__ApiKey"));
        Assert.DoesNotContain(environment.Keys, key => key.Contains("echo", StringComparison.OrdinalIgnoreCase));
        // Nothing of the default section: that is the team's election, laid by the launcher.
        Assert.DoesNotContain(environment.Keys, key => !key.StartsWith("ORKEON_Llm__Profiles__", StringComparison.Ordinal));
    }

    [Fact]
    public void A_terminal_reads_a_settings_key_from_the_variable_studio_remembers_it_in()
    {
        // STUDIO-49: the variable a run outside Studio reads is the setting's own — the one
        // Studio remembers the key in —, named by its entry; no second copy under a name of its id.
        Assert.Equal("ZAI_API_KEY", Zai().ToHostEntry("z-ai").ApiKeyEnvVar);
        Assert.Null(Ollama().ToHostEntry("local").ApiKeyEnvVar);
    }

    // ── the election (STUDIO-49, decision 5) ────────────────────────────────

    [Fact]
    public void The_election_writes_the_setting_whole_and_keeps_what_studio_does_not_model()
    {
        var document = AppSettingsDocument.Parse("""
            {
              "Llm": {
                "BaseUrl": "http://localhost:11434", "Model": "qwen3", "Temperature": 0.4,
                "Thinking": { "Effort": "high" },
                "ApiKey": "sk-written-by-hand", "MaxRetries": 5, "Grammar": true, "AvailableModels": [ "a", "b" ],
                "Profiles": { "local-gpu": { "Model": "qwen3:32b" } }
              }
            }
            """);

        Assert.True(HostLlmProfiles.ElectDefault(document, DeepSeek()));

        Assert.Equal("https://api.deepseek.com", document.Llm.BaseUrl);
        Assert.Equal("deepseek-v4-flash", document.Llm.Model);
        Assert.Equal("DEEPSEEK_API_KEY", document.Llm.ApiKeyEnvVar);
        Assert.Equal(600, document.Llm.TimeoutSeconds);
        // What the setting leaves unset loses its key: the run must not keep the previous election's.
        Assert.Null(document.Llm.Temperature);
        Assert.False(document.ContainsPath("Llm:Thinking"));
        // What Studio does not model stays — the clear-text key is not Studio's to remove.
        Assert.Equal("sk-written-by-hand", document.Llm.ApiKey);
        Assert.Equal(5, document.GetInt32("Llm:MaxRetries"));
        Assert.True(document.GetBoolean("Llm:Grammar"));
        Assert.Equal(["a", "b"], document.GetStringArray("Llm:AvailableModels"));
        Assert.Equal(["local-gpu"], document.Llm.Profiles.Ids);
        Assert.False(HostLlmProfiles.ElectDefault(document, DeepSeek()));
    }

    [Fact]
    public void Electing_a_setting_that_needs_no_key_removes_the_previous_reference()
    {
        var document = AppSettingsDocument.CreateEmpty();
        HostLlmProfiles.ElectDefault(document, Zai());

        HostLlmProfiles.ElectDefault(document, Ollama());

        Assert.Null(document.Llm.ApiKeyEnvVar);
        Assert.Equal("http://localhost:11434", document.Llm.BaseUrl);
        Assert.Null(document.Llm.ThinkingEnabled);
    }

    [Fact]
    public void A_default_elected_before_the_reference_is_healed_whole_at_the_next_gesture_and_only_then()
    {
        // STUDIO-48 wrote the election's model and endpoint alone.
        var set = ModelProfileSet.Empty.Upsert(DeepSeek()).Upsert(Zai());
        var document = AppSettingsDocument.Parse("""{ "Llm": { "BaseUrl": "https://api.deepseek.com", "Model": "deepseek-v4-flash" } }""");

        Assert.True(HostLlmProfiles.HealDefault(document, set));

        Assert.Equal("DEEPSEEK_API_KEY", document.Llm.ApiKeyEnvVar);
        Assert.Equal(600, document.Llm.TimeoutSeconds);
        // Healed: a field then edited by hand stays — only the election and an edit of the
        // elected setting write the section.
        document.Llm.Temperature = 0.2;
        Assert.False(HostLlmProfiles.HealDefault(document, set));
        Assert.Equal(0.2, document.Llm.Temperature);
    }

    [Fact]
    public void A_set_without_a_default_heals_nothing()
    {
        var document = AppSettingsDocument.Parse("""{ "Llm": { "Model": "qwen3" } }""");

        Assert.False(HostLlmProfiles.HealDefault(document, ModelProfileSet.Empty));
        Assert.Equal("""{"Model":"qwen3"}""", document.GetNode("Llm")!.ToJsonString());
    }

    [Fact]
    public void A_copy_never_takes_an_id_another_setting_answers_to()
    {
        // « Claude (副本) » keeps the slug « claude »: the copy must move on to « (副本 2) ».
        var (document, set) = TwoSettings();
        var withClaude = set.Upsert(DeepSeek("Claude"));

        var name = withClaude.CopyNameFor("Claude", "副本",
            candidate => HostLlmProfiles.Check(candidate, null, describesProvider: true, withClaude, document).BlocksSave);

        Assert.Equal("Claude (副本 2)", name);
        Assert.Equal("claude-2", ModelProfile.HostProfileIdOf(name));
    }

    // ── Docker Model Runner (STUDIO-54, decision 4) ─────────────────────────

    /// <summary>
    /// The run reads Docker Model Runner as OpenAI, whose dialect refuses to call without a key: a
    /// setting of that card carries the placeholder <c>orkeon init</c> and the TUI write, in its entry
    /// and in <c>Llm</c> when it is elected — the one <c>ApiKey</c> Studio writes, and no reference.
    /// </summary>
    [Fact]
    public void A_docker_model_runner_setting_writes_the_placeholder_key_in_its_entry_and_in_llm_when_elected()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var set = ModelProfileSet.Empty.Upsert(Docker()).Upsert(DeepSeek());

        HostLlmProfiles.Mirror(document, ModelProfileSet.Empty, set);
        HostLlmProfiles.ElectDefault(document, set.Default!);

        Assert.Equal("not-needed", document.GetString("Llm:Profiles:docker:ApiKey"));
        Assert.Null(document.Llm.Profiles.Get("docker")?.ApiKeyEnvVar);
        Assert.Equal("not-needed", document.Llm.ApiKey);
        Assert.Null(document.Llm.ApiKeyEnvVar);
        // The DeepSeek entry names its variable, and holds no key.
        Assert.False(document.ContainsPath("Llm:Profiles:deepseek:ApiKey"));
        KeyTripwire.AssertNamesNoKey(document.ToJson(), "DEEPSEEK_API_KEY");
        Assert.True(document.Llm.Profiles.KeyAgrees(Docker().ToHostEntry("docker")));
        Assert.True(document.Llm.KeyAgrees(Docker().ToHostEntry(LlmProfileNames.Default)));
    }

    [Fact]
    public void Electing_another_card_takes_the_placeholder_out_of_llm_and_it_alone()
    {
        // The file `orkeon init --preset docker-model-runner` wrote, then DeepSeek elected in Studio:
        // left, the placeholder would pass before the variable the elected setting names.
        var document = AppSettingsDocument.Parse(InitDockerFile);

        Assert.True(HostLlmProfiles.ElectDefault(document, DeepSeek()));

        Assert.Null(document.Llm.ApiKey);
        Assert.Equal("DEEPSEEK_API_KEY", document.Llm.ApiKeyEnvVar);
        Assert.True(document.Llm.KeyAgrees(DeepSeek().ToHostEntry(LlmProfileNames.Default)));

        // Any other key stays: it is not Studio's (the election test above keeps sk-written-by-hand).
        document.Llm.ApiKey = "Not-Needed";
        Assert.False(HostLlmProfiles.ElectDefault(document, DeepSeek()));
        Assert.Equal("Not-Needed", document.Llm.ApiKey);
    }

    [Fact]
    public void An_entry_of_another_card_carrying_the_placeholder_loses_it_at_the_next_gesture()
    {
        var (document, set) = TwoSettings();
        document.SetString("Llm:Profiles:z-ai:ApiKey", "not-needed");
        Assert.False(document.Llm.Profiles.KeyAgrees(Zai().ToHostEntry("z-ai")));

        Assert.True(HostLlmProfiles.Mirror(document, set, set.WithStudio("DeepSeek")));

        Assert.False(document.ContainsPath("Llm:Profiles:z-ai:ApiKey"));
        Assert.Equal("ZAI_API_KEY", document.Llm.Profiles.Get("z-ai")?.ApiKeyEnvVar);
    }

    /// <summary>
    /// A file Studio wrote before this lot names a Docker Model Runner setting without the
    /// placeholder: the entry and the elected <c>Llm</c> receive it at the next gesture on the
    /// settings, once — reading the file writes nothing, and a second gesture finds nothing to write.
    /// </summary>
    [Fact]
    public void A_docker_model_runner_setting_written_before_receives_the_placeholder_at_the_next_gesture_once()
    {
        var set = ModelProfileSet.Empty.Upsert(Docker()).Upsert(DeepSeek());
        var document = AppSettingsDocument.Parse("""
            {
              "Llm": {
                "BaseUrl": "http://localhost:12434/engines/llama.cpp/v1", "Model": "ai/granite-4.0-h-tiny",
                "Profiles": {
                  "docker": { "BaseUrl": "http://localhost:12434/engines/llama.cpp/v1", "Model": "ai/granite-4.0-h-tiny" },
                  "deepseek": { "BaseUrl": "https://api.deepseek.com", "Model": "deepseek-v4-flash", "ApiKeyEnvVar": "DEEPSEEK_API_KEY", "TimeoutSeconds": 600 }
                }
              }
            }
            """);
        var before = document.ToJson();

        // What reading does: the standing of each setting, the entries written by hand.
        _ = HostLlmProfiles.Classify(set);
        _ = HostLlmProfiles.HandWritten(document, set);
        Assert.Equal(before, document.ToJson());
        Assert.False(document.Llm.Profiles.KeyAgrees(Docker().ToHostEntry("docker")));
        Assert.False(document.Llm.KeyAgrees(Docker().ToHostEntry(LlmProfileNames.Default)));

        // A gesture on the settings (the assistant's election): the entry, and the elected default.
        Assert.True(HostLlmProfiles.Mirror(document, set, set.WithStudio("DeepSeek")));
        Assert.True(HostLlmProfiles.HealDefault(document, set));

        Assert.Equal("not-needed", document.GetString("Llm:Profiles:docker:ApiKey"));
        Assert.Equal("not-needed", document.Llm.ApiKey);
        // Once: written, the rule holds, and the next gesture finds nothing to write.
        Assert.False(HostLlmProfiles.Mirror(document, set, set.WithStudio("Docker")));
        Assert.False(HostLlmProfiles.HealDefault(document, set));
    }

    [Fact]
    public void A_key_the_user_wrote_on_a_docker_model_runner_setting_stays_without_repair()
    {
        // The server checks no key: whatever the user wrote serves as well as the placeholder.
        var set = ModelProfileSet.Empty.Upsert(Docker());
        var document = AppSettingsDocument.CreateEmpty();
        HostLlmProfiles.Mirror(document, ModelProfileSet.Empty, set);
        HostLlmProfiles.ElectDefault(document, set.Default!);
        document.SetString("Llm:Profiles:docker:ApiKey", "my-own");
        document.Llm.ApiKey = "my-own";

        Assert.True(document.Llm.Profiles.KeyAgrees(Docker().ToHostEntry("docker")));
        Assert.False(HostLlmProfiles.Mirror(document, set, set.WithStudio("Docker")));
        Assert.False(HostLlmProfiles.HealDefault(document, set));
        Assert.False(HostLlmProfiles.ElectDefault(document, set.Default!));

        Assert.Equal("my-own", document.GetString("Llm:Profiles:docker:ApiKey"));
        Assert.Equal("my-own", document.Llm.ApiKey);
    }

    [Fact]
    public void A_launch_lays_the_placeholder_for_a_docker_model_runner_setting_and_nothing_for_ollama()
    {
        var set = ModelProfileSet.Empty.Upsert(Docker()).Upsert(Ollama());

        var environment = HostLlmProfiles.LaunchEnvironment(set, new FakeApiKeyStore().Peek);

        Assert.Equal("not-needed", environment["ORKEON_Llm__Profiles__docker__ApiKey"]);
        Assert.False(environment.ContainsKey("ORKEON_Llm__Profiles__local__ApiKey"));
        // In place of the default: the placeholder rather than the blank.
        Assert.Equal("not-needed", Docker().EnvironmentOverrides()["ORKEON_Llm__ApiKey"]);
        Assert.Equal("", Ollama().EnvironmentOverrides()["ORKEON_Llm__ApiKey"]);
        Assert.Equal("", Docker().EnvironmentOverrides()["ORKEON_Llm__ApiKeyEnvVar"]);
    }

    [Fact]
    public void A_docker_model_runner_setting_is_recognised_by_its_title_or_by_its_address()
    {
        // A setting written before STUDIO-54 holds its card's title — or nothing the default left.
        Assert.Equal("not-needed", (Docker() with { Provider = "Docker Model Runner" }).ToHostEntry("docker").ApiKeyPlaceholder);
        Assert.Equal("not-needed", (Docker() with { Provider = null }).ToHostEntry("docker").ApiKeyPlaceholder);
        Assert.Null(Ollama().ToHostEntry("local").ApiKeyPlaceholder);
        Assert.Null(DeepSeek().ToHostEntry("deepseek").ApiKeyPlaceholder);
    }

    /// <summary>
    /// STUDIO-50: the names a team's companion file may hold whose host profile moved — created,
    /// removed, renamed, switched to « no model » — and only those: an edit that keeps the id moves
    /// nothing, since a launcher names the id alone.
    /// </summary>
    [Fact]
    public void The_moved_names_are_those_whose_host_profile_appeared_went_or_changed_id()
    {
        var set = ModelProfileSet.Empty.Upsert(DeepSeek()).Upsert(Zai());

        Assert.Empty(HostLlmProfiles.MovedNames(set, set.Upsert(Zai() with { Model = "glm-6" })));
        Assert.Equal(["Local"], HostLlmProfiles.MovedNames(set, set.Upsert(Ollama())));
        Assert.Equal(["Z.AI"], HostLlmProfiles.MovedNames(set, set.Remove("Z.AI")));
        Assert.Equal(["GLM", "Z.AI"], HostLlmProfiles.MovedNames(set, set.Upsert(Zai("GLM"), previousName: "Z.AI")).Order(StringComparer.Ordinal));
        Assert.Equal(["Z.AI"], HostLlmProfiles.MovedNames(set, set.Upsert(Echo("Z.AI"), previousName: "Z.AI")));
    }

    [Fact]
    public void The_id_is_derived_and_never_stored()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Zai());

        Assert.DoesNotContain("HostProfileId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("DescribesProvider", json, StringComparison.Ordinal);
    }
}
