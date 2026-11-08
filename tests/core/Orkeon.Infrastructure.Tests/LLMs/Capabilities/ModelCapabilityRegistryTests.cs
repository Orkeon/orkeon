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
}
