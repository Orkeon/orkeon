using Orkeon.Domain.SharedKernel.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs.Capabilities;
using Orkeon.Infrastructure.LLMs.Profiles;

namespace Orkeon.Infrastructure.Tests.LLMs.Capabilities;

/// <summary>FWK-21: the <c>Llm:ModelCapabilities:&lt;id&gt;</c> overrides.</summary>
public sealed class ModelCapabilitySettingsTests
{
    private static IConfiguration Settings(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(p => KeyValuePair.Create<string, string?>(p.Key, p.Value))).Build();

    private static IModelCapabilityRegistry Resolved(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddOrkeonLlmProfiles(configuration);
        using var container = services.BuildServiceProvider();
        return container.GetRequiredService<IModelCapabilityRegistry>();
    }

    [Fact]
    public void An_entry_reaches_the_registry_the_host_resolves()
    {
        var registry = Resolved(Settings(
            ("Llm:ModelCapabilities:acme-voice:AudioInput", "true"),
            ("Llm:ModelCapabilities:acme-voice:Vision", "true"),
            ("Llm:ModelCapabilities:acme-voice:ReasoningEfforts:0", "low"),
            ("Llm:ModelCapabilities:acme-voice:ReasoningEfforts:1", "high")));

        var entry = registry.TryGet("acme-voice-2");
        Assert.NotNull(entry);
        Assert.True(entry.AudioInput);
        Assert.True(entry.Vision);
        Assert.False(entry.AudioOutput);
        Assert.Equal(["low", "high"], entry.ReasoningEfforts);
    }

    [Fact]
    public void Efforts_may_be_one_comma_separated_value_and_an_empty_one_means_none()
    {
        var registry = Resolved(Settings(
            ("Llm:ModelCapabilities:a:ReasoningEfforts", "low, medium"),
            ("Llm:ModelCapabilities:b:ReasoningEfforts", "")));

        Assert.Equal(["low", "medium"], registry.TryGet("a")!.ReasoningEfforts);
        Assert.Empty(registry.TryGet("b")!.ReasoningEfforts!);
    }

    [Fact]
    public void An_entry_that_leaves_efforts_out_does_not_restrict_them()
    {
        var registry = Resolved(Settings(("Llm:ModelCapabilities:a:AudioInput", "true")));

        Assert.Null(registry.TryGet("a")!.ReasoningEfforts);
    }

    [Fact]
    public void A_switch_that_is_no_boolean_refuses_the_host_naming_the_key()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddOrkeonLlmProfiles(Settings(
            ("Llm:ModelCapabilities:a:AudioInput", "maybe"))));

        Assert.Contains("Llm:ModelCapabilities:a:AudioInput", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("EffortOnly", ThinkingSupport.EffortOnly)]
    [InlineData("none", ThinkingSupport.None)]
    [InlineData("Toggle", ThinkingSupport.Toggle)]
    public void The_thinking_key_names_a_support(string value, ThinkingSupport expected)
    {
        var registry = Resolved(Settings(("Llm:ModelCapabilities:acme-thinker:Thinking", value)));

        Assert.Equal(expected, registry.TryGet("acme-thinker-2")!.Thinking);
    }

    [Theory]
    [InlineData("always")]
    [InlineData("2")]
    [InlineData("EffortOnly,Toggle")]
    [InlineData("None, Budget")]
    public void A_thinking_value_that_names_no_support_refuses_the_host_naming_the_key(string value)
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddOrkeonLlmProfiles(Settings(
            ("Llm:ModelCapabilities:a:Thinking", value))));

        Assert.Contains("Llm:ModelCapabilities:a:Thinking", ex.Message, StringComparison.Ordinal);
        Assert.Contains("EffortOnly", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_section_alone_is_not_a_default_provider()
    {
        Assert.False(LlmSettings.HasDefault(Settings(("Llm:ModelCapabilities:a:AudioInput", "true"))));
    }

    [Fact]
    public void A_host_without_the_section_still_has_the_built_in_table()
    {
        var registry = Resolved(Settings(("Llm:Model", "gpt-4o")));

        Assert.True(registry.TryGet("gpt-4o-audio-preview")!.AudioInput);
    }

    [Fact]
    public void A_configured_entry_merges_with_the_built_in_one_field_by_field()
    {
        var registry = Resolved(Settings(("Llm:ModelCapabilities:gpt-4o:AudioInput", "true")));

        var entry = registry.TryGet("gpt-4o-2024-08-06")!;
        Assert.True(entry.AudioInput);
        Assert.Empty(entry.ReasoningEfforts!);
    }

    [Fact]
    public void A_registry_the_host_registered_first_wins_over_the_configured_one()
    {
        var own = new ModelCapabilityRegistry();
        var services = new ServiceCollection();
        services.AddSingleton<IModelCapabilityRegistry>(own);
        services.AddOrkeonLlmProfiles(Settings(("Llm:ModelCapabilities:acme-voice:AudioInput", "true")));
        using var container = services.BuildServiceProvider();

        Assert.Same(own, container.GetRequiredService<IModelCapabilityRegistry>());
    }
}
