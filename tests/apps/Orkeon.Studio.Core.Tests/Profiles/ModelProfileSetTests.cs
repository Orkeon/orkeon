using System.Text.Json;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Profiles;

namespace Orkeon.Studio.Core.Tests.Profiles;

/// <summary>
/// The model-profile set (the model settings of design v3): elections that never dangle,
/// renames the elections follow, and a file that always degrades to the empty set.
/// </summary>
public sealed class ModelProfileSetTests
{
    private static ModelProfile Profile(string name, string model = "qwen2.5:14b") =>
        new() { Name = name, Provider = "Ollama", Model = model, BaseUrl = "http://localhost:11434/v1" };

    [Fact]
    public void The_first_profile_added_becomes_the_default()
    {
        var set = ModelProfileSet.Empty.Upsert(Profile("Local rapide"));

        Assert.Equal("Local rapide", set.DefaultProfile);
        Assert.Equal("Local rapide", set.Default?.Name);
        Assert.Null(set.Studio);
    }

    [Fact]
    public void A_rename_carries_the_elections_with_it()
    {
        var set = ModelProfileSet.Empty
            .Upsert(Profile("Local"))
            .WithStudio("Local")
            .Upsert(Profile("Local rapide"), previousName: "Local");

        Assert.Single(set.Profiles);
        Assert.Equal("Local rapide", set.DefaultProfile);
        Assert.Equal("Local rapide", set.StudioProfile);
    }

    [Fact]
    public void A_rename_onto_an_existing_name_absorbs_that_profile_instead_of_duplicating_it()
    {
        var set = ModelProfileSet.Empty
            .Upsert(Profile("Local"))
            .Upsert(Profile("Cloud", model: "gpt-5"))
            .WithDefault("Cloud")
            .Upsert(Profile("Cloud", model: "qwen2.5:32b"), previousName: "Local");

        // The name is the identity teams reference: never two bearers at once.
        var survivor = Assert.Single(set.Profiles);
        Assert.Equal("Cloud", survivor.Name);
        Assert.Equal("qwen2.5:32b", survivor.Model);
        Assert.Equal("Cloud", set.DefaultProfile);
        Assert.Equal("qwen2.5:32b", set.Default?.Model);

        // And removing that name removes exactly one profile, not a homonym pile.
        Assert.Empty(set.Remove("Cloud").Profiles);
    }

    [Fact]
    public void Removing_the_default_falls_back_to_the_first_remaining_profile()
    {
        var set = ModelProfileSet.Empty
            .Upsert(Profile("A"))
            .Upsert(Profile("B"))
            .Remove("A");

        Assert.Equal("B", set.DefaultProfile);
    }

    [Fact]
    public void Removing_the_assistants_profile_clears_the_election_instead_of_dangling()
    {
        var set = ModelProfileSet.Empty
            .Upsert(Profile("A"))
            .Upsert(Profile("B"))
            .WithStudio("B")
            .Remove("B");

        Assert.Null(set.StudioProfile);
        Assert.Null(set.Studio);
    }

    [Fact]
    public void An_unknown_name_cannot_be_elected()
    {
        var set = ModelProfileSet.Empty.Upsert(Profile("A"));

        Assert.Equal(set, set.WithDefault("ghost"));
        Assert.Equal(set, set.WithStudio("ghost"));
    }

    [Fact]
    public void Copy_names_stay_unique_however_many_copies_exist()
    {
        var set = ModelProfileSet.Empty.Upsert(Profile("A"));
        set = set.Upsert(Profile(set.CopyNameFor("A", "copy")));

        Assert.Equal("A (copy)", set.Profiles[1].Name);
        Assert.Equal("A (copy 2)", set.CopyNameFor("A", "copy"));
    }

    [Fact]
    public void The_environment_overrides_lay_every_default_field_blank_when_the_profile_leaves_it_unset()
    {
        var overrides = Profile("A").EnvironmentOverrides();

        Assert.Equal("qwen2.5:14b", overrides["ORKEON_Llm__Model"]);
        Assert.Equal("http://localhost:11434/v1", overrides["ORKEON_Llm__BaseUrl"]);
        // STUDIO-49 (decision 4): a profile laid in place of the default leaves nothing of the
        // default through — its key, the variable holding it, its timeout: blank reads as absent.
        Assert.Equal("", overrides["ORKEON_Llm__ApiKey"]);
        Assert.Equal("", overrides["ORKEON_Llm__ApiKeyEnvVar"]);
        Assert.Equal("", overrides["ORKEON_Llm__TimeoutSeconds"]);

        var empty = new ModelProfile { Name = "empty" }.EnvironmentOverrides();
        Assert.Equal(
            [
                "ORKEON_Llm__ApiKey", "ORKEON_Llm__ApiKeyEnvVar", "ORKEON_Llm__BaseUrl", "ORKEON_Llm__MaxTokens",
                "ORKEON_Llm__Model", "ORKEON_Llm__Temperature", "ORKEON_Llm__Thinking__Effort",
                "ORKEON_Llm__Thinking__Enabled", "ORKEON_Llm__TimeoutSeconds",
            ],
            empty.Keys.Order(StringComparer.Ordinal));
        Assert.All(empty.Values, value => Assert.Equal("", value));
    }

    [Fact]
    public async Task The_file_store_round_trips_and_a_corrupt_file_reads_as_empty_with_its_reason()
    {
        var path = Path.Combine(Path.GetTempPath(), $"orkeon-profile-tests-{Guid.NewGuid():N}.json");
        try
        {
            var store = new ModelProfileFileStore(path);
            var set = ModelProfileSet.Empty.Upsert(Profile("Local rapide")).WithStudio("Local rapide");

            await store.SaveAsync(set, TestContext.Current.CancellationToken);
            var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

            Assert.False(loaded.Failed);
            Assert.Equal("Local rapide", loaded.Set.DefaultProfile);
            Assert.Equal("Local rapide", loaded.Set.StudioProfile);
            // STUDIO-54: the card is read by its name, and titled in the language of the moment.
            Assert.Equal("ollama", loaded.Set.Profiles[0].Provider);
            Assert.Equal("Ollama · qwen2.5:14b", loaded.Set.Profiles[0].Summary(EnglishStudioStrings.Instance));

            // The key never travels: the file names no secret, only the endpoint and the model.
            var raw = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.DoesNotContain("ApiKey", raw, StringComparison.OrdinalIgnoreCase);

            // STUDIO-12 C6: a file that fails to parse is still the empty set — but it says
            // so, instead of passing for a first run.
            await File.WriteAllTextAsync(path, "{ not json", TestContext.Current.CancellationToken);
            var corrupt = await store.LoadAsync(TestContext.Current.CancellationToken);
            Assert.Empty(corrupt.Set.Profiles);
            Assert.True(corrupt.Failed);
            Assert.Contains(path, corrupt.Error!, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_missing_file_reads_as_the_empty_set_without_an_error()
    {
        var store = new ModelProfileFileStore(
            Path.Combine(Path.GetTempPath(), $"orkeon-none-{Guid.NewGuid():N}.json"));

        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(loaded.Failed);
        Assert.Empty(loaded.Set.Profiles);
        Assert.Null(loaded.Set.DefaultProfile);
    }

    /// <summary>
    /// STUDIO-12 C6: the file is documented as hand-editable and "profiles" is what people
    /// type; a camelCase file used to load zero profiles, silently. Writes stay PascalCase.
    /// </summary>
    [Fact]
    public async Task A_hand_written_camel_case_file_loads_its_profiles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"orkeon-camel-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """
                {
                  "profiles": [
                    { "name": "kimi", "provider": "Kimi", "model": "kimi-k3", "baseUrl": "https://api.moonshot.ai/v1", "temperature": 1, "maxTokens": 32768 }
                  ],
                  "defaultProfile": "kimi",
                  "studioProfile": "kimi"
                }
                """, TestContext.Current.CancellationToken);
            var store = new ModelProfileFileStore(path);

            var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

            Assert.False(loaded.Failed);
            var profile = Assert.Single(loaded.Set.Profiles);
            Assert.Equal("kimi", profile.Name);
            Assert.Equal("kimi-k3", profile.Model);
            Assert.Equal(1, profile.Temperature);
            Assert.Equal(32768, profile.MaxTokens);
            Assert.Equal("kimi", loaded.Set.DefaultProfile);
            Assert.Same(profile, loaded.Set.Studio);

            // The next write is PascalCase, as before.
            await store.SaveAsync(loaded.Set, TestContext.Current.CancellationToken);
            var raw = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.Contains("\"Profiles\"", raw, StringComparison.Ordinal);
            Assert.Contains("\"MaxTokens\"", raw, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// STUDIO-54, decision 1: a setting is its card's name, never a title of the interface. A file
    /// written by a Studio in French holds the card's French title, which an English Studio used to
    /// read as no card at all; the store recognises it — and a provider the default blanked — as it
    /// reads, in memory: the next gesture on the settings writes the name, never the startup.
    /// </summary>
    [Fact]
    public async Task A_file_written_in_another_language_is_read_as_its_card_names_without_being_written()
    {
        var path = Path.Combine(Path.GetTempPath(), $"orkeon-titles-{Guid.NewGuid():N}.json");
        const string written = """
            {
              "Profiles": [
                { "Name": "Boîte", "Provider": "Autre compatible OpenAI", "Model": "qwen3", "BaseUrl": "https://llm.example.com/v1", "KeyEnvName": "ORKEON_CUSTOM_LLM_API_KEY" },
                { "Name": "Écho", "Provider": "Aucun / hors ligne" },
                { "Name": "GLM", "Provider": "Z.AI (GLM)", "Model": "glm-5.2", "BaseUrl": "https://api.z.ai/api/paas/v4" },
                { "Name": "Perdu", "Model": "deepseek-chat", "BaseUrl": "https://api.deepseek.com" }
              ],
              "DefaultProfile": "Boîte"
            }
            """;
        try
        {
            await File.WriteAllTextAsync(path, written, TestContext.Current.CancellationToken);
            var before = File.GetLastWriteTimeUtc(path);

            var loaded = await new ModelProfileFileStore(path).LoadAsync(TestContext.Current.CancellationToken);

            Assert.False(loaded.Failed);
            Assert.Equal(["custom", "none", "zai", "deepseek"], loaded.Set.Profiles.Select(profile => profile.Provider));
            Assert.Equal("ORKEON_CUSTOM_LLM_API_KEY", loaded.Set.Default?.KeyEnvName);
            Assert.Equal(written, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Equal(before, File.GetLastWriteTimeUtc(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_json_shape_is_the_three_plain_fields()
    {
        var set = ModelProfileSet.Empty.Upsert(Profile("A"));

        var json = JsonSerializer.Serialize(set);

        Assert.Contains("\"Profiles\"", json, StringComparison.Ordinal);
        Assert.Contains("\"DefaultProfile\"", json, StringComparison.Ordinal);
        // Computed views stay out of the file: the resolved Default would duplicate a profile.
        Assert.DoesNotContain("\"Summary\"", json, StringComparison.Ordinal);
    }
}

/// <summary>
/// A profile-pinned temperature (some vendors mandate one per model — Kimi K3 accepts
/// only 1) rides the launch as an ORKEON_Llm__Temperature override, written invariant
/// so a comma-decimal locale cannot corrupt it, and round-trips through the store.
/// </summary>
public sealed class ProfileTemperatureTests
{
    [Fact]
    public void The_pinned_temperature_overrides_the_environment_invariantly()
    {
        var profile = new ModelProfile
        {
            Name = "Kimi K3",
            Provider = "Kimi",
            Model = "kimi-k3",
            BaseUrl = "https://api.moonshot.ai/v1",
            Temperature = 1,
        };

        var overrides = profile.EnvironmentOverrides();

        Assert.Equal("1", overrides["ORKEON_Llm__Temperature"]);

        // And a fractional pin never picks up a comma from the locale.
        var fractional = profile with { Temperature = 0.7 };
        Assert.Equal("0.7", fractional.EnvironmentOverrides()["ORKEON_Llm__Temperature"]);

        // No pin: blank, so the engine keeps its own default — not the default setting's pin.
        var unpinned = profile with { Temperature = null };
        Assert.Equal("", unpinned.EnvironmentOverrides()["ORKEON_Llm__Temperature"]);
    }

    [Fact]
    public void The_thinking_switch_and_effort_ride_the_launch_blank_when_unpinned()
    {
        // LLM-11: the knob the run of 2026-09-20 could not reach from Studio.
        var profile = new ModelProfile { Name = "Kimi", Model = "kimi-k2.6", ThinkingEnabled = false, ThinkingEffort = " high " };

        var overrides = profile.EnvironmentOverrides();

        Assert.Equal("false", overrides["ORKEON_Llm__Thinking__Enabled"]);
        Assert.Equal("high", overrides["ORKEON_Llm__Thinking__Effort"]);
        Assert.Equal("true", (profile with { ThinkingEnabled = true }).EnvironmentOverrides()["ORKEON_Llm__Thinking__Enabled"]);

        var unpinned = (profile with { ThinkingEnabled = null, ThinkingEffort = "  " }).EnvironmentOverrides();
        Assert.Equal("", unpinned["ORKEON_Llm__Thinking__Enabled"]);
        Assert.Equal("", unpinned["ORKEON_Llm__Thinking__Effort"]);
    }

    [Fact]
    public void The_pinned_timeout_rides_the_launch_and_an_unpinned_one_is_blank()
    {
        var profile = new ModelProfile { Name = "Kimi K3", Model = "kimi-k3", TimeoutSeconds = 180 };

        Assert.Equal("180", profile.EnvironmentOverrides()["ORKEON_Llm__TimeoutSeconds"]);
        Assert.Equal("", (profile with { TimeoutSeconds = null }).EnvironmentOverrides()["ORKEON_Llm__TimeoutSeconds"]);
        Assert.Equal("", (profile with { TimeoutSeconds = 0 }).EnvironmentOverrides()["ORKEON_Llm__TimeoutSeconds"]);
    }

    /// <summary>
    /// STUDIO-12 C5b: the response budget rides the launch too — a pin only; an empty field
    /// leaves the engine to send the model's documented maximum (LLM-10).
    /// </summary>
    [Fact]
    public void The_pinned_max_tokens_ride_the_launch_and_an_unpinned_cap_is_blank()
    {
        var profile = new ModelProfile { Name = "Kimi K3", Model = "kimi-k3", MaxTokens = 32768 };

        Assert.Equal("32768", profile.EnvironmentOverrides()["ORKEON_Llm__MaxTokens"]);
        // Unpinned: blank (STUDIO-49), so the engine sends the model's maximum — not the
        // default setting's cap.
        Assert.Equal("", (profile with { MaxTokens = null }).EnvironmentOverrides()["ORKEON_Llm__MaxTokens"]);
        Assert.Equal("", (profile with { MaxTokens = 0 }).EnvironmentOverrides()["ORKEON_Llm__MaxTokens"]);
    }

    [Fact]
    public async Task The_temperature_round_trips_through_the_file_store()
    {
        var path = Path.Combine(Path.GetTempPath(), $"orkeon-profiles-{Guid.NewGuid():N}.json");
        try
        {
            var store = new ModelProfileFileStore(path);
            await store.SaveAsync(new ModelProfileSet
            {
                Profiles = [new ModelProfile { Name = "Kimi K3", Model = "kimi-k3", Temperature = 1, TimeoutSeconds = 180, MaxTokens = 32768 }],
            }, TestContext.Current.CancellationToken);

            var loaded = (await store.LoadAsync(TestContext.Current.CancellationToken)).Set;

            Assert.Equal(1, loaded.Profiles[0].Temperature);
            Assert.Equal(180, loaded.Profiles[0].TimeoutSeconds);
            Assert.Equal(32768, loaded.Profiles[0].MaxTokens);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
