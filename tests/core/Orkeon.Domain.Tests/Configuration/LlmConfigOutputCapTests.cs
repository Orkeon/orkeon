using Orkeon.Constants.Llm;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.Configuration;

/// <summary>
/// The output cap a request carries (LLM-10): pinned value, else the model's documented
/// maximum, else the engine fallback — and nothing at all for a model the vendor documents
/// as unbounded. The blanket 4096 that starved reasoning models is only the last resort.
/// </summary>
public class LlmConfigOutputCapTests
{
    [Fact]
    public void Nothing_pinned_resolves_to_the_models_documented_maximum()
    {
        var config = LlmConfig.Create(LlmProviderDefaultModels.DeepSeek);

        Assert.Null(config.MaxTokens);
        Assert.Equal(LlmModelOutputLimits.MaxOutputTokens(LlmProviderDefaultModels.DeepSeek), config.ResolveMaxTokens());
        Assert.Equal(393_216, config.ResolveMaxTokens());
    }

    [Fact]
    public void A_pinned_value_wins_over_the_catalogue()
    {
        var config = LlmConfig.Create(LlmProviderDefaultModels.DeepSeek) with { MaxTokens = 512 };

        Assert.Equal(512, config.ResolveMaxTokens());
    }

    [Fact]
    public void A_pin_equal_to_the_old_engine_default_is_still_a_pin()
    {
        // The first version could not tell an explicit 4096 from "nothing set".
        var config = LlmConfig.Create(LlmProviderDefaultModels.DeepSeek) with { MaxTokens = 4096 };

        Assert.Equal(4096, config.ResolveMaxTokens());
    }

    [Fact]
    public void An_unknown_model_falls_back_to_4096_exactly_as_before()
    {
        var config = LlmConfig.Create("some-vendor/some-model-nobody-documented");

        Assert.Equal(LlmDefaults.FallbackMaxOutputTokens, config.ResolveMaxTokens());
        Assert.Equal(4096, config.ResolveMaxTokens());
    }

    [Fact]
    public void A_model_the_vendor_documents_as_unbounded_yields_no_cap_at_all()
    {
        // Mistral bounds prompt + max_tokens by the window: any fixed value fails on a real
        // prompt, so the field is left out and the model writes to its window.
        var config = LlmConfig.Create(LlmProviderDefaultModels.Mistral);

        Assert.Null(config.ResolveMaxTokens());
        Assert.Equal(7_000, (config with { MaxTokens = 7_000 }).ResolveMaxTokens());
    }

    [Fact]
    public void The_providers_default_model_is_used_when_the_config_names_none()
    {
        var config = LlmConfig.Default() with { Model = null! };

        Assert.Equal(128_000, config.ResolveMaxTokens(defaultModel: LlmProviderDefaultModels.Anthropic));
    }

    [Fact]
    public void A_provider_bound_entry_holds_only_on_that_provider()
    {
        // Together clamps max_tokens to the window (truncate), so its window is a safe cap;
        // the same id routed by HuggingFace has no such clamp and keeps the fallback.
        var config = LlmConfig.Create("Qwen/Qwen3.5-9B");

        Assert.Equal(262_144, config.ResolveMaxTokens(provider: LlmProviderKeys.Together));
        Assert.Equal(LlmDefaults.FallbackMaxOutputTokens, config.ResolveMaxTokens(provider: LlmProviderKeys.HuggingFace));
        Assert.Equal(LlmDefaults.FallbackMaxOutputTokens, config.ResolveMaxTokens());
    }

    [Fact]
    public void CreateValidated_accepts_no_pin_and_refuses_a_non_positive_one()
    {
        Assert.Null(LlmConfig.CreateValidated("m").MaxTokens);
        Assert.Equal(300, LlmConfig.CreateValidated("m", maxTokens: 300).MaxTokens);
        Assert.Throws<ArgumentOutOfRangeException>(() => LlmConfig.CreateValidated("m", maxTokens: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LlmConfig.CreateValidated("m", maxTokens: -5));
    }
}
