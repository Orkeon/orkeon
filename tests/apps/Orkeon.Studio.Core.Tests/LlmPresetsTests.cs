using System.Text.Json;
using Orkeon.Infrastructure.Constants.Llm;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Presets;

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
        string provider, string? baseUrl, string? model, string? inlineApiKey)
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
        Assert.DoesNotContain("ApiKey", LlmPresets.BuildJson(plan), StringComparison.Ordinal);
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
    public void A_custom_environment_variable_is_reported_as_not_read_by_the_runtime()
    {
        var plan = Plan(LlmPresets.OpenAI, new LlmPresetOverrides { ApiKeyEnv = "MY_KEY" });
        var guidance = string.Join(" ", LlmPresets.Guidance(plan));

        Assert.Equal("MY_KEY", plan.ApiKeyEnvName);
        Assert.Contains("export MY_KEY=", guidance, StringComparison.Ordinal);
        Assert.Contains(LlmPresets.DefaultApiKeyEnv, guidance, StringComparison.Ordinal);
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
}
