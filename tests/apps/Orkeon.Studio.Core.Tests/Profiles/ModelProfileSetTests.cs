using System.Text.Json;
using Orkeon.Studio.Core.Profiles;

namespace Orkeon.Studio.Core.Tests.Profiles;

/// <summary>
/// The model-profile set (design v3 "réglages de modèle"): elections that never dangle,
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
    public void The_environment_overrides_carry_only_what_the_profile_sets()
    {
        var overrides = Profile("A").EnvironmentOverrides();

        Assert.Equal("qwen2.5:14b", overrides["ORKEON_Llm__Model"]);
        Assert.Equal("http://localhost:11434/v1", overrides["ORKEON_Llm__BaseUrl"]);
        Assert.Empty(new ModelProfile { Name = "empty" }.EnvironmentOverrides());
    }

    [Fact]
    public async Task The_file_store_round_trips_and_a_corrupt_file_reads_as_empty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"orkeon-profile-tests-{Guid.NewGuid():N}.json");
        try
        {
            var store = new ModelProfileFileStore(path);
            var set = ModelProfileSet.Empty.Upsert(Profile("Local rapide")).WithStudio("Local rapide");

            await store.SaveAsync(set, TestContext.Current.CancellationToken);
            var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal("Local rapide", loaded.DefaultProfile);
            Assert.Equal("Local rapide", loaded.StudioProfile);
            Assert.Equal("Ollama · qwen2.5:14b", loaded.Profiles[0].Summary);

            // The key never travels: the file names no secret, only the endpoint and the model.
            var raw = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.DoesNotContain("ApiKey", raw, StringComparison.OrdinalIgnoreCase);

            await File.WriteAllTextAsync(path, "{ not json", TestContext.Current.CancellationToken);
            var corrupt = await store.LoadAsync(TestContext.Current.CancellationToken);
            Assert.Empty(corrupt.Profiles);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_missing_file_reads_as_the_empty_set()
    {
        var store = new ModelProfileFileStore(
            Path.Combine(Path.GetTempPath(), $"orkeon-none-{Guid.NewGuid():N}.json"));

        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(loaded.Profiles);
        Assert.Null(loaded.DefaultProfile);
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
