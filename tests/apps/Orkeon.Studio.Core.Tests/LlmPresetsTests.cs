using System.Text.Json;
using Orkeon.Constants.Llm;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Tests.Profiles;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// Studio's presets must produce the very file <c>orkeon init</c> produces. The
/// expected text is built here the way <c>InitCommand.BuildJson</c> builds it — an
/// ordered dictionary serialized indented, plus a trailing newline — so a drift on
/// either side (key set, key order, defaults, formatting) fails the test.
/// </summary>
public sealed class LlmPresetsTests
{
    private static readonly JsonSerializerOptions s_indentedJson = new() { WriteIndented = true };

    /// <summary>Faithful copy of <c>InitCommand.BuildJson</c> (Orkeon.Scripting.Cli, internal).</summary>
    private static string BuildJsonLikeInitCommand(
        string provider, string? baseUrl, string? model, string? inlineApiKey, string? apiKeyEnvVar = null)
    {
        var root = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (provider == "none")
        {
            root["_comment"] =
                "No LLM configured: Orkeon falls back to the <undefined-llm> echo provider. " +
                "Run `orkeon init` again to configure one.";
        }
        else
        {
            var llm = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Model"] = model,
                ["BaseUrl"] = baseUrl,
            };
            // STUDIO-49: --api-key-env names the variable, unless it is the native ORKEON_Llm__ApiKey.
            if (apiKeyEnvVar is not null)
                llm["ApiKeyEnvVar"] = apiKeyEnvVar;
            if (inlineApiKey is not null)
                llm["ApiKey"] = inlineApiKey;
            root["Llm"] = llm;
        }

        return JsonSerializer.Serialize(root, s_indentedJson)
            + Environment.NewLine;
    }

    private static LlmPresetPlan Plan(string preset, LlmPresetOverrides? overrides = null)
    {
        var created = LlmPresets.TryCreatePlan(preset, overrides, out var plan, out var error);
        Assert.True(created, error);
        Assert.NotNull(plan);
        return plan;
    }

    [Fact]
    public void Ollama_preset_matches_the_cli_output()
    {
        var expected = BuildJsonLikeInitCommand(
            "ollama", LlmEndpoints.OllamaDefault, ProviderDefaults.ForProvider("ollama"), inlineApiKey: null);

        Assert.Equal(expected, LlmPresets.BuildJson(Plan(LlmPresets.Ollama)));
    }

    [Fact]
    public void Docker_model_runner_preset_matches_the_cli_output()
    {
        var expected = BuildJsonLikeInitCommand(
            "docker-model-runner",
            "http://localhost:12434/engines/llama.cpp/v1",
            "ai/granite-4.0-h-tiny",
            inlineApiKey: "not-needed");

        Assert.Equal(expected, LlmPresets.BuildJson(Plan(LlmPresets.DockerModelRunner)));
    }

    [Fact]
    public void OpenAI_preset_matches_the_cli_output_and_keeps_the_key_out_of_the_file()
    {
        var plan = Plan(LlmPresets.OpenAI);
        var expected = BuildJsonLikeInitCommand(
            "openai", LlmEndpoints.OpenAI, ProviderDefaults.ForProvider("openai"), inlineApiKey: null);

        Assert.Equal(expected, LlmPresets.BuildJson(plan));
        Assert.Equal(LlmPresets.DefaultApiKeyEnv, plan.ApiKeyEnvName);
        // The native variable needs no reference: no key, and no ApiKeyEnvVar either.
        KeyTripwire.AssertNamesNoKey(LlmPresets.BuildJson(plan));
    }

    [Fact]
    public void Custom_preset_matches_the_cli_output()
    {
        var overrides = new LlmPresetOverrides { BaseUrl = "https://api.deepseek.com", Model = "deepseek-chat" };
        var expected = BuildJsonLikeInitCommand(
            "custom", "https://api.deepseek.com", "deepseek-chat", inlineApiKey: null);

        Assert.Equal(expected, LlmPresets.BuildJson(Plan(LlmPresets.Custom, overrides)));
    }

    [Fact]
    public void None_preset_matches_the_cli_output()
    {
        var expected = BuildJsonLikeInitCommand("none", null, null, null);

        Assert.Equal(expected, LlmPresets.BuildJson(Plan(LlmPresets.None)));
    }

    [Fact]
    public void An_inline_key_is_written_and_suppresses_the_environment_variable()
    {
        var plan = Plan(LlmPresets.OpenAI, new LlmPresetOverrides { ApiKey = "sk-live-123" });

        var expected = BuildJsonLikeInitCommand(
            "openai", LlmEndpoints.OpenAI, ProviderDefaults.ForProvider("openai"), inlineApiKey: "sk-live-123");

        Assert.Equal(expected, LlmPresets.BuildJson(plan));
        Assert.Null(plan.ApiKeyEnvName);
        Assert.Contains("plain text", string.Join(" ", LlmPresets.Guidance(plan)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_custom_environment_variable_is_named_in_the_file_and_the_runtime_reads_it()
    {
        // STUDIO-49: the old guidance said such a variable was only read by the probes — the
        // trap a terminal run fell into. The file now names it, as `orkeon init --api-key-env`.
        var plan = Plan(LlmPresets.OpenAI, new LlmPresetOverrides { ApiKeyEnv = "MY_KEY" });
        var guidance = string.Join(" ", LlmPresets.Guidance(plan));
        var expected = BuildJsonLikeInitCommand(
            "openai", LlmEndpoints.OpenAI, ProviderDefaults.ForProvider("openai"), inlineApiKey: null, apiKeyEnvVar: "MY_KEY");

        Assert.Equal("MY_KEY", plan.ApiKeyEnvName);
        Assert.Equal(expected, LlmPresets.BuildJson(plan));
        KeyTripwire.AssertNamesNoKey(LlmPresets.BuildJson(plan), "MY_KEY");
        Assert.Contains("export MY_KEY=", guidance, StringComparison.Ordinal);
        Assert.Contains("Llm:ApiKeyEnvVar", guidance, StringComparison.Ordinal);
        Assert.DoesNotContain("only", guidance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Applying_a_preset_writes_its_reference_and_a_keyless_preset_removes_it()
    {
        var document = AppSettingsDocument.Parse("""{ "Llm": { "Model": "old", "ApiKeyEnvVar": "OLD_KEY" } }""");

        LlmPresets.Apply(document, Plan(LlmPresets.Custom, new LlmPresetOverrides
        {
            BaseUrl = "https://api.deepseek.com", Model = "deepseek-chat", ApiKeyEnv = "DEEPSEEK_API_KEY",
        }));
        Assert.Equal("DEEPSEEK_API_KEY", document.Llm.ApiKeyEnvVar);

        LlmPresets.Apply(document, Plan(LlmPresets.Ollama));
        Assert.Null(document.Llm.ApiKeyEnvVar);
        Assert.False(document.ContainsPath("Llm:ApiKeyEnvVar"));
    }

    [Fact]
    public void Custom_preset_requires_a_base_url_and_a_model()
    {
        Assert.False(LlmPresets.TryCreatePlan(
            LlmPresets.Custom, new LlmPresetOverrides { BaseUrl = "https://x" }, out var plan, out var error));

        Assert.Null(plan);
        Assert.Contains("base URL", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_preset_is_rejected_and_lists_the_supported_ones()
    {
        Assert.False(LlmPresets.TryCreatePlan("gpt-please", null, out _, out var error));

        foreach (var name in LlmPresets.Names)
            Assert.Contains(name, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Preset_names_are_case_and_whitespace_tolerant()
    {
        var plan = Plan("  OLLAMA ");

        Assert.Equal(LlmPresets.Ollama, plan.Preset);
    }

    [Fact]
    public void The_catalog_covers_every_preset_name()
    {
        Assert.Equal(LlmPresets.Names, LlmPresets.Catalog.Select(p => p.Name).ToList());
        Assert.All(LlmPresets.Names, name => Assert.NotNull(LlmPresets.Describe(name)));
    }

    [Fact]
    public void Applying_a_preset_to_an_existing_file_keeps_the_other_sections()
    {
        var document = AppSettingsDocument.Parse("""
            {
              "Llm": { "Model": "old", "BaseUrl": "http://old", "ApiKey": "old-key" },
              "RateLimiting": { "QueueLimit": 32 },
              "Unknown": { "keep": true }
            }
            """);

        LlmPresets.Apply(document, Plan(LlmPresets.Ollama));

        Assert.Equal(LlmEndpoints.OllamaDefault, document.Llm.BaseUrl);
        Assert.Equal(ProviderDefaults.ForProvider("ollama"), document.Llm.Model);
        Assert.Null(document.Llm.ApiKey); // Ollama needs none — the stale key is removed
        Assert.Equal(32, document.RateLimiting.QueueLimit);
        Assert.True(document.GetBoolean("Unknown:keep"));
    }

    [Fact]
    public void The_none_preset_drops_the_llm_section_and_adds_the_note()
    {
        var document = AppSettingsDocument.Parse("""{ "Llm": { "Model": "m" }, "Keep": 1 }""");

        LlmPresets.Apply(document, Plan(LlmPresets.None));

        Assert.False(document.Llm.Exists);
        Assert.Equal(LlmPresets.NoLlmComment, document.GetString(LlmPresets.CommentKey));
        Assert.Equal(1, document.GetInt32("Keep"));
    }

    [Fact]
    public void Configuring_an_llm_again_removes_the_note_the_none_preset_left()
    {
        var document = LlmPresets.BuildDocument(Plan(LlmPresets.None));

        LlmPresets.Apply(document, Plan(LlmPresets.Ollama));

        Assert.False(document.ContainsPath(LlmPresets.CommentKey));
        Assert.True(document.Llm.Exists);
    }

    [Fact]
    public void A_user_written_comment_key_is_not_removed()
    {
        var document = AppSettingsDocument.Parse("""{ "_comment": "mine, hands off" }""");

        LlmPresets.Apply(document, Plan(LlmPresets.Ollama));

        Assert.Equal("mine, hands off", document.GetString(LlmPresets.CommentKey));
    }

    // ── the card of a setting (STUDIO-54, decision 1) ───────────────────────

    private const string UnknownEndpoint = "https://llm.example.com/v1";

    private static ModelProfile Setting(string? provider, string? baseUrl = null, string? model = null) =>
        new() { Name = "setting", Provider = provider, BaseUrl = baseUrl, Model = model };

    [Theory]
    [InlineData("deepseek")]
    [InlineData("DEEPSEEK")]
    [InlineData(" DeepSeek ")]
    public void A_setting_names_its_card_whatever_the_case(string provider) =>
        Assert.Equal(LlmPresets.DeepSeek, LlmPresets.CardOf(Setting(provider, LlmProviderEndpoints.OllamaDefault, "m")));

    [Theory]
    [InlineData("Z.AI (GLM)", LlmPresets.Zai)]
    [InlineData("Docker Model Runner", LlmPresets.DockerModelRunner)]
    [InlineData("Mammouth AI", LlmPresets.Mammouth)]
    [InlineData("Other OpenAI-compatible", LlmPresets.Custom)]
    [InlineData("None / offline", LlmPresets.None)]
    public void A_setting_written_with_an_english_title_is_recognised_by_it_before_its_address(string title, string card) =>
        Assert.Equal(card, LlmPresets.CardOf(Setting(title, LlmProviderEndpoints.OllamaDefault, "m")));

    [Theory]
    [InlineData("Autre compatible OpenAI")]
    [InlineData("其他 OpenAI 兼容")]
    [InlineData(null)]
    [InlineData("")]
    public void A_title_of_another_language_on_an_address_no_card_carries_is_the_compatible_openai_card(string? provider) =>
        Assert.Equal(LlmPresets.Custom, LlmPresets.CardOf(Setting(provider, UnknownEndpoint, "qwen3")));

    [Theory]
    [InlineData("Aucun / hors ligne")]
    [InlineData("无 / 离线")]
    [InlineData(null)]
    public void A_setting_without_an_address_nor_a_model_is_the_no_model_card(string? provider) =>
        Assert.Equal(LlmPresets.None, LlmPresets.CardOf(Setting(provider)));

    [Fact]
    public void A_setting_with_a_model_and_no_address_is_the_compatible_openai_card()
    {
        // Not « no model »: it names one, and only the catch-all takes an endpoint typed by hand.
        Assert.Equal(LlmPresets.Custom, LlmPresets.CardOf(Setting("Autre compatible OpenAI", model: "qwen3")));
    }

    [Theory]
    [InlineData(LlmProviderEndpoints.Zai, LlmPresets.Zai)]
    [InlineData(LlmProviderEndpoints.DeepSeek, LlmPresets.DeepSeek)]
    [InlineData("http://localhost:11434", LlmPresets.Ollama)]
    [InlineData(LlmProviderEndpoints.DockerModelRunner, LlmPresets.DockerModelRunner)]
    // Azure OpenAI has no card by design: its per-resource endpoint is a « Compatible OpenAI » entry.
    [InlineData("https://my-resource.openai.azure.com/openai/deployments/gpt", LlmPresets.Custom)]
    [InlineData("not a url", LlmPresets.Custom)]
    public void A_setting_whose_provider_was_blanked_is_recognised_by_its_address(string baseUrl, string card)
    {
        Assert.Equal(card, LlmPresets.CardOf(Setting("", baseUrl, "m")));
        Assert.Equal(card, LlmPresets.CardOf(Setting(null, baseUrl, "m")));
    }

    [Fact]
    public void Every_card_of_the_editor_is_recognised_by_its_name_and_by_its_english_title()
    {
        foreach (var card in LlmPresets.ProviderCatalogFor(EnglishStudioStrings.Instance))
        {
            Assert.Equal(card.Name, LlmPresets.CardOf(Setting(card.Name, UnknownEndpoint, "m")));
            Assert.Equal(card.Name, LlmPresets.CardOf(Setting(card.Title, UnknownEndpoint, "m")));
        }
    }

    [Fact]
    public void A_card_is_titled_in_the_language_of_the_moment_and_an_unknown_name_is_shown_as_it_is()
    {
        var french = new FrenchTitles();

        Assert.Equal("Other OpenAI-compatible", LlmPresets.TitleFor(LlmPresets.Custom, EnglishStudioStrings.Instance));
        Assert.Equal("Autre compatible OpenAI", LlmPresets.TitleFor(LlmPresets.Custom, french));
        Assert.Equal("Aucun / hors ligne", LlmPresets.TitleFor("NONE", french));
        Assert.Equal("DeepSeek", LlmPresets.TitleFor(LlmPresets.DeepSeek, french));
        Assert.Equal("azure-openai", LlmPresets.TitleFor("azure-openai", french));
        Assert.Equal("", LlmPresets.TitleFor(null, french));
    }

    [Theory]
    [InlineData(LlmProviderEndpoints.DeepSeek, true)]
    [InlineData(LlmProviderEndpoints.OpenAI, true)]
    [InlineData(UnknownEndpoint, true)]
    // Docker Model Runner is read as OpenAI, which refuses to call without a key: it needs its placeholder.
    [InlineData(LlmProviderEndpoints.DockerModelRunner, true)]
    [InlineData(LlmProviderEndpoints.OllamaDefault, false)]
    [InlineData("http://127.0.0.1:11434/v1", false)]
    // Nothing a run could call: the probe says what it says.
    [InlineData(null, false)]
    [InlineData("  ", false)]
    [InlineData("localhost:11434", false)]
    [InlineData("not a url", false)]
    public void Every_endpoint_but_ollama_needs_a_key_as_a_run_does(string? baseUrl, bool needsKey) =>
        Assert.Equal(needsKey, LlmPresets.NeedsApiKey(baseUrl));

    [Fact]
    public void Only_the_docker_model_runner_card_writes_a_placeholder_key()
    {
        Assert.Equal("not-needed", LlmPresets.PlaceholderKeyOf(LlmPresets.DockerModelRunner));
        Assert.Equal(LlmProviderDefaultModels.DockerModelRunnerApiKeyPlaceholder, LlmPresets.PlaceholderKeyOf(LlmPresets.DockerModelRunner));
        Assert.Null(LlmPresets.PlaceholderKeyOf(LlmPresets.Ollama));
        Assert.Null(LlmPresets.PlaceholderKeyOf(LlmPresets.Custom));
        Assert.Null(LlmPresets.PlaceholderKeyOf(LlmPresets.DeepSeek));
        Assert.Null(LlmPresets.PlaceholderKeyOf(null));
    }

    /// <summary>The English strings with the two card titles a language changes, in French.</summary>
    private sealed class FrenchTitles : IStudioStrings
    {
        public string this[string key] => key switch
        {
            StudioStringKeys.PresetCustomTitle => "Autre compatible OpenAI",
            StudioStringKeys.PresetNoneTitle => "Aucun / hors ligne",
            _ => EnglishStudioStrings.Instance[key],
        };

        public event EventHandler? CultureChanged
        {
            add { }
            remove { }
        }
    }
}
