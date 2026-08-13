using Orkeon.Studio.Config.Presentation;
using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Config.Tests.Presentation;

public class PresetFormTests
{
    private static int IndexOf(string preset)
    {
        for (var i = 0; i < PresetForm.Catalog.Count; i++)
        {
            if (string.Equals(PresetForm.Catalog[i].Name, preset, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    [Fact]
    public void The_chooser_offers_the_five_presets_of_orkeon_init()
    {
        Assert.Equal(LlmPresets.Names.Count, PresetForm.Choices.Count);
        Assert.Equal(LlmPresets.Names, PresetForm.Catalog.Select(preset => preset.Name).ToList());
    }

    [Fact]
    public void Selecting_a_preset_prefills_its_defaults()
    {
        var form = new PresetForm { SelectedIndex = IndexOf(LlmPresets.Ollama) };

        Assert.Equal(LlmPresets.Ollama, form.Selected.Name);
        Assert.False(string.IsNullOrWhiteSpace(form.BaseUrl));
        Assert.False(string.IsNullOrWhiteSpace(form.Model));
        Assert.Equal("", form.ApiKey);
    }

    [Fact]
    public void The_openai_preset_references_the_key_from_the_environment_by_default()
    {
        var form = new PresetForm { SelectedIndex = IndexOf(LlmPresets.OpenAI) };

        Assert.True(form.TryBuildPlan(out var plan, out var error));
        Assert.Null(error);
        Assert.NotNull(plan);

        Assert.Equal(LlmPresets.OpenAI, plan.Preset);
        Assert.Null(plan.InlineApiKey);
        Assert.Equal(LlmPresets.DefaultApiKeyEnv, plan.ApiKeyEnvName);
        Assert.Contains(PresetForm.Guidance(plan), line => line.Contains(LlmPresets.DefaultApiKeyEnv));
    }

    [Fact]
    public void Typing_a_key_stores_it_inline_and_the_guidance_warns_about_it()
    {
        var form = new PresetForm { SelectedIndex = IndexOf(LlmPresets.OpenAI), ApiKey = "sk-live" };

        Assert.True(form.TryBuildPlan(out var plan, out _));
        Assert.NotNull(plan);

        Assert.Equal("sk-live", plan.InlineApiKey);
        Assert.Null(plan.ApiKeyEnvName);
        Assert.Contains(PresetForm.Guidance(plan), line => line.Contains("WARNING"));
    }

    [Fact]
    public void The_custom_preset_requires_a_base_url_and_a_model()
    {
        var form = new PresetForm { SelectedIndex = IndexOf(LlmPresets.Custom) };

        Assert.False(form.TryBuildPlan(out _, out var error));
        Assert.NotNull(error);

        form.BaseUrl = "https://api.deepseek.com/v1";
        form.Model = "deepseek-chat";
        Assert.True(form.TryBuildPlan(out var plan, out _));
        Assert.NotNull(plan);
        Assert.Equal("deepseek-chat", plan.Model);
    }

    [Fact]
    public void Switching_preset_replaces_the_previous_defaults()
    {
        var form = new PresetForm { SelectedIndex = IndexOf(LlmPresets.OpenAI) };
        var openAiBaseUrl = form.BaseUrl;

        form.SelectedIndex = IndexOf(LlmPresets.Ollama);

        Assert.NotEqual(openAiBaseUrl, form.BaseUrl);
    }
}
