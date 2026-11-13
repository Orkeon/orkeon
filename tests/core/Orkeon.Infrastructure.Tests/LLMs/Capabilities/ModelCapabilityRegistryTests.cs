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
    [InlineData("gpt-60")]
    [InlineData("kimi-k2.7-coder")]
    public void A_prefix_matches_at_a_boundary_only(string model)
    {
        var entry = ModelCapabilityRegistry.Default.TryGet(model);

        Assert.NotEqual(ThinkingSupport.EffortOnly, entry?.Thinking);
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
