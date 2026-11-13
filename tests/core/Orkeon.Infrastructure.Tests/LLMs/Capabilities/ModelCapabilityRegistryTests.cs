using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Capabilities;

namespace Orkeon.Infrastructure.Tests.LLMs.Capabilities;

public class ModelCapabilityRegistryTests
{
    [Fact]
    public void The_longest_prefix_wins()
    {
        var registry = ModelCapabilityRegistry.Default;

        Assert.True(registry.TryGet("gpt-4o-audio-preview-2025-06-03")!.AudioInput);
        Assert.False(registry.TryGet("gpt-4o-2024-08-06")!.AudioInput);
    }

    [Fact]
    public void The_match_ignores_case()
    {
        Assert.True(ModelCapabilityRegistry.Default.TryGet("GPT-4O-Audio-Preview")!.AudioInput);
    }

    [Theory]
    [InlineData("google/gemini-3.7-flash")]
    [InlineData("moonshotai/kimi-k2.7-code")]
    [InlineData("zai-org/GLM-5.3-Flash")]
    public void A_vendor_prefixed_id_is_looked_up_by_the_part_after_its_last_slash(string model)
    {
        Assert.Equal(ThinkingSupport.EffortOnly, ModelCapabilityRegistry.Default.TryGet(model)!.Thinking);
    }

    [Theory]
    [InlineData("glm-5.30")]
    [InlineData("gemini-3.70")]
    [InlineData("kimi-k2.7-coder")]
    public void A_prefix_matches_at_a_boundary_only(string model)
    {
        // Each id extends an EffortOnly entry (glm-5.3, gemini-3.7, kimi-k2.7-code) past a non-separator.
        var entry = ModelCapabilityRegistry.Default.TryGet(model);

        Assert.NotEqual(ThinkingSupport.EffortOnly, entry?.Thinking);
    }

    [Theory]
    [InlineData("glm-5.3", true)]
    [InlineData("glm-5.3-flash", true)]
    [InlineData("glm-5.3.1", true)]
    [InlineData("glm-5.3:cloud", true)]
    [InlineData("glm-5.3@2026-11-01", true)]
    [InlineData("glm-5.3/fp8", true)]
    [InlineData("GLM-5.3-Flash", true)]
    [InlineData("glm-5.30", false)]
    [InlineData("glm-5.3x", false)]
    [InlineData("glm-5", false)]
    public void Each_separator_ends_a_prefix_and_any_other_character_does_not(string model, bool expected)
    {
        Assert.Equal(expected, ModelCapabilityRegistry.MatchesAtBoundary(model, "glm-5.3"));
    }

    [Fact]
    public void A_key_that_ends_with_a_separator_covers_any_continuation()
    {
        Assert.True(ModelCapabilityRegistry.MatchesAtBoundary("gemini-x", "gemini-"));
        Assert.False(ModelCapabilityRegistry.MatchesAtBoundary("geminix", "gemini-"));
    }

    [Theory]
    [InlineData("gemini-3.7", "mammouth")]
    [InlineData("gemini-3.7-pro", "mammouth")]
    [InlineData("gemini-3.8-flash", "Mammouth")]
    [InlineData("gemini-3.7", " mammouth ")]
    [InlineData("google/gemini-3.7-pro", "openrouter")]
    public void A_gateway_entry_wins_over_the_vendors_for_its_provider(string model, string provider)
    {
        var entry = ModelCapabilityRegistry.Default.TryGet(model, provider)!;

        Assert.Equal(["minimal", "low", "medium", "high", "xhigh", "max"], entry.ReasoningEfforts);
        Assert.Equal(ThinkingSupport.EffortOnly, entry.Thinking);
    }

    [Theory]
    [InlineData("gemini-3.7", null)]
    [InlineData("gemini-3.7", "")]
    [InlineData("gemini-3.7", " ")]
    [InlineData("gemini-3.7", "gemini")]
    [InlineData("gemini-3.7", "openrouter")]
    [InlineData("google/gemini-3.7", "together")]
    public void A_provider_without_its_own_entry_gets_the_vendors(string model, string? provider)
    {
        // `openrouter/gemini-3.7` is no prefix of `openrouter/google/gemini-3.7`: the vendor's row applies.
        var entry = ModelCapabilityRegistry.Default.TryGet(model, provider)!;

        Assert.Equal(["none", "low", "medium", "high"], entry.ReasoningEfforts);
        Assert.Same(ModelCapabilityRegistry.Default.TryGet(model), entry);
    }

    [Fact]
    public void Together_serves_glm_5_3_without_the_vendors_restrictions()
    {
        var served = ModelCapabilityRegistry.Default.TryGet("zai-org/GLM-5.3-Flash", "together")!;
        var vendor = ModelCapabilityRegistry.Default.TryGet("zai-org/GLM-5.3-Flash", "zai")!;

        Assert.Null(served.Thinking);
        Assert.Null(served.ReasoningEfforts);
        Assert.Equal(ThinkingSupport.EffortOnly, vendor.Thinking);
        Assert.Equal(["low", "high", "max"], vendor.ReasoningEfforts);
    }

    [Fact]
    public void A_scoped_lookup_of_no_model_is_null()
    {
        Assert.Null(ModelCapabilityRegistry.Default.TryGet(null, "mammouth"));
        Assert.Null(ModelCapabilityRegistry.Default.TryGet(" ", "mammouth"));
        Assert.Null(ModelCapabilityRegistry.Default.TryGet("acme-llm-9", "mammouth"));
    }

    [Fact]
    public void A_registry_that_does_not_scope_by_provider_answers_the_plain_lookup()
    {
        IModelCapabilityRegistry registry = new UnscopedRegistry();

        Assert.Same(UnscopedRegistry.Entry, registry.TryGet("gemini-3.7", "mammouth"));
    }

    /// <summary>A host's registry written against the one-argument contract only.</summary>
    private sealed class UnscopedRegistry : IModelCapabilityRegistry
    {
        public static readonly ModelCapabilities Entry = new() { ReasoningEfforts = ["low"] };

        public ModelCapabilities? TryGet(string? model) => Entry;
    }

    [Fact]
    public void A_later_tier_is_not_caught_by_an_earlier_one()
    {
        // `gpt-5` would refuse `none`; `gpt-5.6` takes it.
        Assert.Contains("none", ModelCapabilityRegistry.Default.TryGet("gpt-5.6-sol")!.ReasoningEfforts!);
        Assert.Equal(["low", "high", "max"], ModelCapabilityRegistry.Default.TryGet("glm-5.3-flashx")!.ReasoningEfforts);
    }

    [Fact]
    public void An_override_of_thinking_keeps_the_built_in_efforts()
    {
        var registry = new ModelCapabilityRegistry(new Dictionary<string, ModelCapabilityOverride>
        {
            ["glm-5.3"] = new() { Thinking = ThinkingSupport.Toggle },
        });

        var entry = registry.TryGet("glm-5.3")!;
        Assert.Equal(ThinkingSupport.Toggle, entry.Thinking);
        Assert.Equal(["low", "high", "max"], entry.ReasoningEfforts);
    }

    [Fact]
    public void A_model_the_table_does_not_know_is_null()
    {
        Assert.Null(ModelCapabilityRegistry.Default.TryGet("acme-llm-9"));
        Assert.Null(ModelCapabilityRegistry.Default.TryGet(null));
        Assert.Null(ModelCapabilityRegistry.Default.TryGet(" "));
    }

    [Fact]
    public void The_table_lists_the_efforts_a_model_takes_and_none_for_one_that_takes_none()
    {
        var registry = ModelCapabilityRegistry.Default;

        Assert.Contains("high", registry.TryGet("o3-mini")!.ReasoningEfforts!);
        Assert.Empty(registry.TryGet("gpt-4o")!.ReasoningEfforts!);
    }

    [Theory]
    [InlineData("gpt-3.5-turbo")]
    [InlineData("gpt-4-0613")]
    [InlineData("gpt-4-turbo-2024-04-09")]
    [InlineData("gpt-4o-2024-08-06")]
    [InlineData("gpt-4.1-mini")]
    [InlineData("gpt-4o-audio-preview")]
    [InlineData("gpt-4o-mini-audio-preview")]
    [InlineData("gpt-audio")]
    [InlineData("gpt-5-chat-latest")]
    [InlineData("gemini-1.5-pro")]
    [InlineData("gemini-2.0-flash")]
    public void A_model_with_no_reasoning_pass_has_no_thinking(string model)
    {
        Assert.Equal(ThinkingSupport.None, ModelCapabilityRegistry.Default.TryGet(model)!.Thinking);
    }

    [Fact]
    public void An_override_adds_a_model_the_table_does_not_know()
    {
        var registry = new ModelCapabilityRegistry(new Dictionary<string, ModelCapabilityOverride>
        {
            ["acme-voice"] = new() { AudioInput = true },
        });

        Assert.True(registry.TryGet("acme-voice-2")!.AudioInput);
    }

    [Fact]
    public void An_override_of_the_same_id_replaces_the_built_in_entry()
    {
        var registry = new ModelCapabilityRegistry(new Dictionary<string, ModelCapabilityOverride>
        {
            ["gpt-4o"] = new() { AudioInput = true },
        });

        Assert.True(registry.TryGet("gpt-4o-2024-08-06")!.AudioInput);
        Assert.True(registry.TryGet("gpt-4o-audio-preview")!.AudioInput);
        Assert.False(ModelCapabilityRegistry.Default.TryGet("gpt-4o-2024-08-06")!.AudioInput);
    }

    [Fact]
    public void A_longer_override_beats_a_shorter_built_in_one()
    {
        var registry = new ModelCapabilityRegistry(new Dictionary<string, ModelCapabilityOverride>
        {
            ["gpt-4o-audio-preview"] = new() { AudioInput = false },
        });

        Assert.False(registry.TryGet("gpt-4o-audio-preview-1")!.AudioInput);
        Assert.True(registry.TryGet("gpt-4o-audio-2025")!.AudioInput);
    }

    [Fact]
    public void An_override_that_sets_one_field_keeps_the_built_in_values_of_the_others()
    {
        var registry = new ModelCapabilityRegistry(new Dictionary<string, ModelCapabilityOverride>
        {
            ["gpt-4o"] = new() { AudioInput = true },
        });

        var entry = registry.TryGet("gpt-4o-2024-08-06")!;
        Assert.True(entry.AudioInput);
        Assert.True(entry.Vision);
        Assert.Empty(entry.ReasoningEfforts!);
    }

    [Fact]
    public void An_override_of_the_efforts_alone_keeps_the_built_in_audio_and_vision()
    {
        var registry = new ModelCapabilityRegistry(new Dictionary<string, ModelCapabilityOverride>
        {
            ["gemini-2.5"] = new() { ReasoningEfforts = ["high"] },
        });

        var entry = registry.TryGet("gemini-2.5-pro")!;
        Assert.Equal(["high"], entry.ReasoningEfforts);
        Assert.True(entry.AudioInput);
        Assert.True(entry.Vision);
    }

    [Fact]
    public void An_override_longer_than_a_built_in_prefix_merges_with_that_built_in_entry()
    {
        var registry = new ModelCapabilityRegistry(new Dictionary<string, ModelCapabilityOverride>
        {
            ["gpt-4o-audio-preview-2025"] = new() { Vision = true },
        });

        var entry = registry.TryGet("gpt-4o-audio-preview-2025-06")!;
        Assert.True(entry.AudioInput);
        Assert.True(entry.AudioOutput);
        Assert.True(entry.Vision);
    }

    [Fact]
    public void An_override_of_an_unknown_model_leaves_the_fields_it_does_not_set_unset()
    {
        var registry = new ModelCapabilityRegistry(new Dictionary<string, ModelCapabilityOverride>
        {
            ["acme-voice"] = new() { AudioInput = true },
        });

        var entry = registry.TryGet("acme-voice-1")!;
        Assert.False(entry.Vision);
        Assert.Null(entry.ReasoningEfforts);
    }

    [Fact]
    public void The_gpt_5_prefix_does_not_cover_the_chat_model_that_does_not_reason()
    {
        var registry = ModelCapabilityRegistry.Default;

        Assert.Empty(registry.TryGet("gpt-5-chat-latest")!.ReasoningEfforts!);
        Assert.Contains("minimal", registry.TryGet("gpt-5-mini")!.ReasoningEfforts!);
    }
}
