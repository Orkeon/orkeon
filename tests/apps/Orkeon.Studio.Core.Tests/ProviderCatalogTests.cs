using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Profiles;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// The model-profile editor's provider catalogue: every cloud the runtime ships a provider
/// for gets a ready-to-click card, and the key policy stays "an environment variable name,
/// never a value in a file".
/// </summary>
public sealed class ProviderCatalogTests
{
    private static IReadOnlyList<LlmPresetInfo> Catalogue() =>
        LlmPresets.ProviderCatalogFor(EnglishStudioStrings.Instance);

    [Fact]
    public void Every_runtime_cloud_provider_has_a_card()
    {
        var ids = Catalogue().Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

        // The framework's cloud providers, through their factory keys. Azure OpenAI has no
        // card by design (per-resource endpoint — the "Compatible OpenAI" card covers it),
        // and the mock keeps the echo fallback as a card of its own.
        string[] expected =
        [
            LlmPresets.Ollama, LlmPresets.OpenAI, LlmPresets.Anthropic,
            LlmPresets.Groq, LlmPresets.Mistral, LlmPresets.DeepSeek, LlmPresets.Kimi,
            LlmPresets.Qwen, LlmPresets.Together, LlmPresets.HuggingFace, LlmPresets.Zai,
            LlmPresets.Gemini, LlmPresets.Grok,
        ];

        Assert.All(expected, id => Assert.Contains(id, ids));
        Assert.Contains(LlmPresets.DockerModelRunner, ids);
        Assert.Contains(LlmPresets.Custom, ids);
        Assert.Contains(LlmPresets.None, ids);
        Assert.DoesNotContain(LlmPresets.AzureOpenAI, ids);
    }

    [Fact]
    public void Every_cloud_card_is_complete_enough_for_a_novice()
    {
        foreach (var card in Catalogue())
        {
            switch (card.Kind)
            {
                case LlmPresetKind.Local:
                case LlmPresetKind.None:
                    Assert.False(card.RequiresApiKey, $"{card.Name} should be keyless");
                    break;

                case LlmPresetKind.Other:
                    // The catch-all: URL and model typed by hand, key block shown.
                    Assert.True(card.RequiresApiKey);
                    Assert.False(string.IsNullOrWhiteSpace(card.DefaultApiKeyEnv));
                    break;

                case LlmPresetKind.Cloud:
                default:
                    // A novice must be one click away: endpoint, model, key variable,
                    // and the vendor console where a key is obtained.
                    Assert.True(card.RequiresApiKey, $"{card.Name} should require a key");
                    Assert.False(string.IsNullOrWhiteSpace(card.DefaultApiKeyEnv), $"{card.Name} needs a key variable");
                    Assert.False(string.IsNullOrWhiteSpace(card.DefaultBaseUrl), $"{card.Name} needs a default endpoint");
                    Assert.False(string.IsNullOrWhiteSpace(card.DefaultModel), $"{card.Name} needs a default model");
                    Assert.False(string.IsNullOrWhiteSpace(card.KeyConsoleUrl), $"{card.Name} needs its key console");
                    break;
            }
        }
    }

    [Fact]
    public void The_init_catalogue_is_untouched_by_the_editor_catalogue()
    {
        // orkeon init parity is byte-for-byte on these five; the editor's wider catalogue
        // must not leak into it.
        Assert.Equal(
            [LlmPresets.Ollama, LlmPresets.DockerModelRunner, LlmPresets.OpenAI, LlmPresets.Custom, LlmPresets.None],
            LlmPresets.Catalog.Select(p => p.Name).ToList());
    }

    [Fact]
    public void A_profile_with_a_key_variable_lays_the_key_over_the_launch()
    {
        var profile = new ModelProfile
        {
            Name = "DeepSeek perso",
            Model = "deepseek-v4-flash",
            BaseUrl = "https://api.deepseek.com",
            KeyEnvName = "DEEPSEEK_API_KEY",
        };

        var overrides = profile.EnvironmentOverrides(name =>
            name == "DEEPSEEK_API_KEY" ? " sk-secret " : null);

        Assert.Equal("sk-secret", overrides["ORKEON_Llm__ApiKey"]);
        Assert.Equal("deepseek-v4-flash", overrides["ORKEON_Llm__Model"]);
        Assert.Equal("https://api.deepseek.com", overrides["ORKEON_Llm__BaseUrl"]);
    }

    [Fact]
    public void An_absent_or_empty_key_variable_adds_no_override()
    {
        var profile = new ModelProfile { Name = "p", Model = "m", KeyEnvName = "DEEPSEEK_API_KEY" };

        Assert.False(profile.EnvironmentOverrides(_ => null).ContainsKey("ORKEON_Llm__ApiKey"));
        Assert.False(profile.EnvironmentOverrides(_ => "  ").ContainsKey("ORKEON_Llm__ApiKey"));
        Assert.False(new ModelProfile { Name = "p", Model = "m" }
            .EnvironmentOverrides(_ => "sk").ContainsKey("ORKEON_Llm__ApiKey"));
    }

    [Fact]
    public void The_environment_store_round_trips_through_the_process_environment()
    {
        var store = new EnvironmentApiKeyStore();
        var name = $"ORKEON_TEST_KEY_{Guid.NewGuid():N}";
        try
        {
            Assert.Null(store.Peek(name));
            store.Save(name, "  sk-value  ");
            Assert.Equal("sk-value", store.Peek(name));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
