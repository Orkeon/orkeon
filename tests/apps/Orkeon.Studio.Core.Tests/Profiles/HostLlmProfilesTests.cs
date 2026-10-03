using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Profiles;

/// <summary>
/// STUDIO-48: Studio's model settings are the host's LLM profiles. Each one that names a provider
/// is mirrored into <c>Llm:Profiles:&lt;slug&gt;</c> without its key, follows its setting through a
/// rename and a removal, and reaches a launch with its key through the environment; an entry
/// written by hand is never touched, and <c>default</c> is refused as a name.
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

    /// <summary>A settings file with a default, a profile written by hand and the RAG on none.</summary>
    private static AppSettingsDocument HandWrittenDocument() => AppSettingsDocument.Parse("""
        {
          "Llm": {
            "BaseUrl": "http://localhost:11434", "Model": "qwen3",
            "Profiles": { "local-gpu": { "BaseUrl": "http://localhost:11500", "Model": "qwen3:32b", "MaxRetries": 2 } }
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
        Assert.False(document.Llm.Profiles.Get("z-ai")?.ThinkingEnabled);
        Assert.DoesNotContain("ApiKey", document.ToJson(), StringComparison.OrdinalIgnoreCase);
        // The default stays the Llm section's own: a profile is not an election.
        Assert.Equal("qwen3", document.Llm.Model);
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
        Assert.Equal("""{"BaseUrl":"http://localhost:11500","Model":"qwen3:32b","MaxRetries":2}""",
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
        Assert.Equal("false", environment["ORKEON_Llm__Profiles__z-ai__Thinking__Enabled"]);
        // A key that is not remembered is not invented, and a setting offered to no crew rides nowhere.
        Assert.False(environment.ContainsKey("ORKEON_Llm__Profiles__z-ai__ApiKey"));
        Assert.DoesNotContain(environment.Keys, key => key.Contains("echo", StringComparison.OrdinalIgnoreCase));
        // Nothing of the default section: that is the team's election, laid by the launcher.
        Assert.DoesNotContain(environment.Keys, key => !key.StartsWith("ORKEON_Llm__Profiles__", StringComparison.Ordinal));
    }

    [Fact]
    public void The_terminal_variable_of_a_setting_is_named_after_its_id()
    {
        Assert.Equal("ORKEON_Llm__Profiles__z-ai__ApiKey", ModelProfile.HostKeyVariable("z-ai"));
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

    [Fact]
    public void The_id_is_derived_and_never_stored()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Zai());

        Assert.DoesNotContain("HostProfileId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("DescribesProvider", json, StringComparison.Ordinal);
    }
}
