using System.Text;
using Microsoft.Extensions.Configuration;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Validation;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// STUDIO-48: <c>Llm:Profiles</c> in the settings document — kept as written by every edit of the
/// rest of the file, written in place by Studio for the entries it owns, and never mistaken for
/// the default provider the <c>Llm</c> section describes.
/// </summary>
public sealed class LlmProfilesSectionTests
{
    private const string HandWritten = """
        {
          "Llm": {
            "BaseUrl": "https://api.deepseek.com", "Model": "deepseek-v4-flash",
            "Profiles": {
              "claude": { "BaseUrl": "https://api.anthropic.com/v1", "Model": "claude-sonnet-5", "MaxRetries": 2 }
            }
          },
          "Orkeon": { "FileSystem": { "Mounts": [ "C:/data:/workspace:rw" ] } }
        }
        """;

    [Fact]
    public void A_hand_written_profile_survives_a_load_edit_save_round_trip()
    {
        var document = AppSettingsDocument.Parse(HandWritten);

        document.Llm.Model = "deepseek-v4-pro";
        document.Llm.Temperature = 0.3;
        var reloaded = AppSettingsDocument.Parse(document.ToJson());

        var entry = reloaded.Llm.Profiles.Get("claude");
        Assert.NotNull(entry);
        Assert.Equal("https://api.anthropic.com/v1", entry.BaseUrl);
        Assert.Equal("claude-sonnet-5", entry.Model);
        Assert.Equal(2, reloaded.GetInt32("Llm:Profiles:claude:MaxRetries"));
        Assert.Equal("deepseek-v4-pro", reloaded.Llm.Model);
    }

    [Theory]
    [InlineData("""{ "Llm": { "Profiles": { "claude": { "BaseUrl": "https://api.anthropic.com/v1" } } } }""", false)]
    [InlineData("""{ "Llm": { "Model": "qwen3", "Profiles": { "claude": { "BaseUrl": "https://api.anthropic.com/v1" } } } }""", true)]
    [InlineData("""{ "Llm": { } }""", false)]
    [InlineData("""{ }""", false)]
    public void The_default_exists_as_the_runtime_reads_it_and_profiles_alone_do_not_make_one(string json, bool expected)
    {
        var document = AppSettingsDocument.Parse(json);
        var runtime = new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();

        Assert.Equal(expected, document.Llm.Exists);
        // The runtime's own reading: a section holding profiles alone leaves the default unset.
        Assert.Equal(LlmSettings.HasDefault(runtime), document.Llm.Exists);
    }

    [Fact]
    public void A_section_holding_profiles_alone_raises_the_win01_warning()
    {
        var document = AppSettingsDocument.Parse(
            """{ "Llm": { "Profiles": { "claude": { "BaseUrl": "https://api.anthropic.com/v1" } } } }""");

        var messages = new AppSettingsValidator(new Doubles.FakeDirectoryProbe()).Validate(document);

        Assert.Contains(messages, m => m.Code == ValidationCodes.LlmSectionMissing);
    }

    [Fact]
    public void Removing_the_default_keeps_the_named_profiles()
    {
        var document = AppSettingsDocument.Parse(HandWritten);

        document.Llm.Remove();

        Assert.False(document.Llm.Exists);
        Assert.Null(document.Llm.Model);
        Assert.Equal(["claude"], document.Llm.Profiles.Ids);
        Assert.Equal(2, document.GetInt32("Llm:Profiles:claude:MaxRetries"));
    }

    [Fact]
    public void The_no_model_preset_clears_the_default_and_keeps_the_named_profiles()
    {
        var document = AppSettingsDocument.Parse(HandWritten);
        Assert.True(LlmPresets.TryCreatePlan("none", null, out var plan, out _));

        LlmPresets.Apply(document, plan);

        Assert.False(document.Llm.Exists);
        Assert.Equal("claude-sonnet-5", document.Llm.Profiles.Get("claude")?.Model);
    }

    [Fact]
    public void Removing_the_default_of_a_section_without_profiles_removes_the_section()
    {
        var document = AppSettingsDocument.Parse("""{ "Llm": { "Model": "qwen3" }, "Other": 1 }""");

        document.Llm.Remove();

        Assert.False(document.ContainsPath("Llm"));
        Assert.Equal(1, document.GetInt32("Other"));
    }

    [Fact]
    public void A_write_lands_field_by_field_never_writes_a_key_and_keeps_what_it_does_not_model()
    {
        var document = AppSettingsDocument.Parse(HandWritten);

        var changed = document.Llm.Profiles.Set(new LlmProfileEntry
        {
            Id = "claude",
            BaseUrl = "https://api.anthropic.com/v1",
            Model = "claude-opus-5",
            Temperature = 0.2,
            TimeoutSeconds = 120,
            ThinkingEnabled = false,
        });

        Assert.True(changed);
        Assert.Equal("claude-opus-5", document.GetString("Llm:Profiles:claude:Model"));
        Assert.Equal(0.2, document.GetDouble("Llm:Profiles:claude:Temperature"));
        Assert.Equal(120, document.GetInt32("Llm:Profiles:claude:TimeoutSeconds"));
        Assert.False(document.GetBoolean("Llm:Profiles:claude:Thinking:Enabled"));
        Assert.Equal(2, document.GetInt32("Llm:Profiles:claude:MaxRetries"));
        Assert.DoesNotContain("ApiKey", document.ToJson(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_write_of_the_same_values_changes_nothing_and_a_cleared_field_loses_its_key()
    {
        var document = AppSettingsDocument.CreateEmpty();
        var entry = new LlmProfileEntry { Id = "local", BaseUrl = "http://localhost:11434", Model = "qwen3", ThinkingEffort = "high" };
        Assert.True(document.Llm.Profiles.Set(entry));

        Assert.False(document.Llm.Profiles.Set(entry));

        Assert.True(document.Llm.Profiles.Set(entry with { ThinkingEffort = null }));
        Assert.False(document.ContainsPath("Llm:Profiles:local:Thinking"));
        Assert.Equal("""{"BaseUrl":"http://localhost:11434","Model":"qwen3"}""", document.GetNode("Llm:Profiles:local")!.ToJsonString());
    }

    [Fact]
    public void A_write_replaces_a_differently_cased_twin_the_binder_would_merge()
    {
        var document = AppSettingsDocument.Parse(
            """{ "Llm": { "Profiles": { "Claude": { "Model": "old", "MaxRetries": 1 } } } }""");

        document.Llm.Profiles.Set(new LlmProfileEntry { Id = "claude", Model = "claude-sonnet-5" });

        Assert.Equal(["claude"], document.Llm.Profiles.Ids);
        Assert.Equal("claude-sonnet-5", document.Llm.Profiles.Get("CLAUDE")?.Model);
        Assert.Equal(1, document.GetInt32("Llm:Profiles:claude:MaxRetries"));
    }

    [Fact]
    public void The_reserved_name_is_never_written()
    {
        var document = AppSettingsDocument.CreateEmpty();

        Assert.Throws<ArgumentException>(() =>
            document.Llm.Profiles.Set(new LlmProfileEntry { Id = "Default", Model = "m" }));
        Assert.True(LlmProfilesSection.IsDefault(" default "));
        Assert.True(LlmProfilesSection.IsDefault(null));
        Assert.False(LlmProfilesSection.IsDefault("claude"));
    }

    [Fact]
    public void A_rename_moves_the_entry_with_every_key_it_carries()
    {
        var document = AppSettingsDocument.Parse(HandWritten);

        Assert.True(document.Llm.Profiles.Rename("claude", "claude-opus"));

        Assert.Equal(["claude-opus"], document.Llm.Profiles.Ids);
        Assert.Equal(2, document.GetInt32("Llm:Profiles:claude-opus:MaxRetries"));
        Assert.False(document.Llm.Profiles.Rename("ghost", "other"));
    }

    [Fact]
    public void A_rename_onto_an_id_another_entry_holds_does_nothing()
    {
        var document = AppSettingsDocument.Parse(
            """{ "Llm": { "Profiles": { "a": { "Model": "1" }, "b": { "Model": "2" } } } }""");

        Assert.False(document.Llm.Profiles.Rename("a", "B"));

        Assert.Equal(["a", "b"], document.Llm.Profiles.Ids);
    }

    [Fact]
    public void Removing_the_last_profile_removes_the_section_and_an_llm_section_left_empty()
    {
        var document = AppSettingsDocument.Parse(
            """{ "Llm": { "Profiles": { "claude": { "Model": "m" } } }, "Other": 1 }""");

        Assert.True(document.Llm.Profiles.Remove("CLAUDE"));

        Assert.False(document.ContainsPath("Llm"));
        Assert.Equal(1, document.GetInt32("Other"));
        Assert.False(document.Llm.Profiles.Remove("claude"));
    }

    [Fact]
    public void Removing_a_profile_keeps_the_default_beside_it()
    {
        var document = AppSettingsDocument.Parse(HandWritten);

        document.Llm.Profiles.Remove("claude");

        Assert.False(document.ContainsPath("Llm:Profiles"));
        Assert.Equal("deepseek-v4-flash", document.Llm.Model);
    }

    [Fact]
    public void The_rag_llm_profile_reads_and_writes_its_key()
    {
        var document = AppSettingsDocument.CreateEmpty();

        document.Rag.LlmProfile = "claude";

        Assert.Equal("claude", document.GetString("Orkeon:Rag:LlmProfile"));
        Assert.Equal("Orkeon:Rag:LlmProfile", RagSection.LlmProfilePath);
        document.Rag.LlmProfile = null;
        Assert.False(document.ContainsPath("Orkeon:Rag:LlmProfile"));
    }
}
