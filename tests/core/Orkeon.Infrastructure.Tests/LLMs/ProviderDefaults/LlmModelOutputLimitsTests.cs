using Orkeon.Constants.Llm;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// The per-model output-cap catalogue (LLM-10): how an id is matched, and that every
/// provider default has a verdict — documented, unbounded, or a deliberate exception.
/// </summary>
public class LlmModelOutputLimitsTests
{
    [Theory]
    [InlineData("gpt-5.6-sol", 128_000)]
    [InlineData("GPT-5.6-SOL", 128_000)]                 // case never matters on an id
    [InlineData("claude-sonnet-5-20260401", 128_000)]    // a dated variant is the family
    [InlineData("qwen3.8-max-0902", 131_072)]            // a snapshot suffix is the family
    [InlineData("Qwen/Qwen3.5-9B:together", null)]       // a routing suffix on an id the bare catalogue does not hold
    [InlineData("google/gemini-3.7-flash", 65_536)]      // an aggregator's vendor prefix is dropped
    [InlineData("deepseek-ai/DeepSeek-V4.1-Flash", null)] // …but only to an id the catalogue holds
    [InlineData("gpt-5.6-solar", null)]                  // a prefix without a separator claims nothing
    [InlineData("mistral-medium-2604", LlmModelOutputLimits.Unbounded)]
    [InlineData("nobody/knows-this-one", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void An_id_is_matched_exactly_then_by_family_then_without_its_vendor_prefix(string? model, int? expected)
    {
        Assert.Equal(expected, LlmModelOutputLimits.MaxOutputTokens(model));
    }

    [Fact]
    public void A_provider_bound_entry_is_tried_first_and_only_for_that_provider()
    {
        Assert.Equal(262_144, LlmModelOutputLimits.MaxOutputTokens("Qwen/Qwen3.5-9B", LlmProviderKeys.Together));
        Assert.Null(LlmModelOutputLimits.MaxOutputTokens("Qwen/Qwen3.5-9B", LlmProviderKeys.HuggingFace));
        Assert.Equal(65_500, LlmModelOutputLimits.MaxOutputTokens("qwen3.7-plus", LlmProviderKeys.Mammouth));
        Assert.Equal(131_072, LlmModelOutputLimits.MaxOutputTokens("qwen3.7-plus", LlmProviderKeys.Qwen));
        Assert.Equal(131_072, LlmModelOutputLimits.MaxOutputTokens("qwen3.7-plus", "OpenAI"));   // a provider without an entry changes nothing
    }

    /// <summary>
    /// The defaults the catalogue knows nothing about, each for a stated reason. A new default
    /// that lands here without a verdict fails this test rather than silently riding the fallback.
    /// </summary>
    private static readonly Dictionary<string, string> DefaultsWithoutAnEntry = new(StringComparer.Ordinal)
    {
        [LlmProviderDefaultModels.Ollama] = "a local runtime: num_predict is left out, Ollama generates to its window",
        [LlmProviderDefaultModels.HuggingFace] = "the router's bound is the routed provider's context, which differs per route",
        [LlmProviderDefaultModels.DockerModelRunner] = "a local llama.cpp server; no vendor figure",
    };

    public static TheoryData<string, string> ProviderDefaults() => new()
    {
        { nameof(LlmProviderDefaultModels.OpenAI), LlmProviderDefaultModels.OpenAI },
        { nameof(LlmProviderDefaultModels.Anthropic), LlmProviderDefaultModels.Anthropic },
        { nameof(LlmProviderDefaultModels.Ollama), LlmProviderDefaultModels.Ollama },
        { nameof(LlmProviderDefaultModels.Together), LlmProviderDefaultModels.Together },
        { nameof(LlmProviderDefaultModels.DeepSeek), LlmProviderDefaultModels.DeepSeek },
        { nameof(LlmProviderDefaultModels.Kimi), LlmProviderDefaultModels.Kimi },
        { nameof(LlmProviderDefaultModels.Zai), LlmProviderDefaultModels.Zai },
        { nameof(LlmProviderDefaultModels.Qwen), LlmProviderDefaultModels.Qwen },
        { nameof(LlmProviderDefaultModels.Gemini), LlmProviderDefaultModels.Gemini },
        { nameof(LlmProviderDefaultModels.Mistral), LlmProviderDefaultModels.Mistral },
        { nameof(LlmProviderDefaultModels.Grok), LlmProviderDefaultModels.Grok },
        { nameof(LlmProviderDefaultModels.MiniMax), LlmProviderDefaultModels.MiniMax },
        { nameof(LlmProviderDefaultModels.HuggingFace), LlmProviderDefaultModels.HuggingFace },
        { nameof(LlmProviderDefaultModels.OpenRouter), LlmProviderDefaultModels.OpenRouter },
        { nameof(LlmProviderDefaultModels.Mammouth), LlmProviderDefaultModels.Mammouth },
        { nameof(LlmProviderDefaultModels.DockerModelRunner), LlmProviderDefaultModels.DockerModelRunner },
    };

    [Theory]
    [MemberData(nameof(ProviderDefaults))]
    public void Every_provider_default_has_a_verdict(string provider, string model)
    {
        var providerKey = provider switch
        {
            nameof(LlmProviderDefaultModels.Together) => LlmProviderKeys.Together,
            _ => null,
        };
        var verdict = LlmModelOutputLimits.MaxOutputTokens(model, providerKey);

        if (verdict is not null)
        {
            Assert.True(verdict >= 0, $"{provider}: {model} has a negative cap");
            return;
        }

        Assert.True(
            DefaultsWithoutAnEntry.ContainsKey(model),
            $"{provider}: the default model {model} is neither documented in LlmModelOutputLimits nor listed as a deliberate exception — it would ride the 4096 fallback unnoticed.");
    }
}
