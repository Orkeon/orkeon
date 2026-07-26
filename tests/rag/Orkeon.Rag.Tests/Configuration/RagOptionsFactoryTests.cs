using Microsoft.Extensions.Configuration;
using Orkeon.Rag.Configuration;

namespace Orkeon.Rag.Tests.Configuration;

/// <summary>
/// Tests for <see cref="RagOptionsFactory"/> (RagOptions v2 binding, plan §8.1):
/// profile = preset, configuration = individual override.
/// </summary>
public class RagOptionsFactoryTests
{
    private static IConfiguration Config(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build();

    [Fact]
    public void Build_NoConfiguration_YieldsTheFastPreset()
    {
        var options = RagOptionsFactory.Build(Config());

        Assert.Equal("fast", options.Profile);
        Assert.False(options.Rerank.Enabled);
        Assert.False(options.Retrieval.Hybrid.Enabled);
    }

    [Fact]
    public void Build_ProfileFromConfiguration_SelectsThePreset()
    {
        var options = RagOptionsFactory.Build(Config(("Orkeon:Rag:Profile", "balanced")));

        Assert.Equal("balanced", options.Profile);
        Assert.True(options.Rerank.Enabled);
        Assert.Equal("onnx", options.Rerank.Kind);
        Assert.True(options.Retrieval.Hybrid.Enabled);
        Assert.Equal(50, options.Retrieval.CandidateK);
    }

    [Fact]
    public void Build_IndividualKeys_OverrideThePreset()
    {
        var options = RagOptionsFactory.Build(Config(
            ("Orkeon:Rag:Profile", "balanced"),
            ("Orkeon:Rag:Rerank:TopN", "8"),
            ("Orkeon:Rag:Retrieval:CandidateK", "25"),
            ("Orkeon:Rag:Context:Ordering", "linear"),
            ("Orkeon:Rag:Generation:Temperature", "0.1")));

        // Overridden keys win…
        Assert.Equal(8, options.Rerank.TopN);
        Assert.Equal(25, options.Retrieval.CandidateK);
        Assert.Equal("linear", options.Context.Ordering);
        Assert.Equal(0.1f, options.Generation.Temperature);

        // …everything else keeps the balanced preset.
        Assert.True(options.Rerank.Enabled);
        Assert.Equal("onnx", options.Rerank.Kind);
        Assert.True(options.Retrieval.Hybrid.Enabled);
    }

    [Fact]
    public void Build_ExplicitProfileName_WinsOverTheConfiguredProfileKey()
    {
        // The resolver builds 'fast' even when configuration says balanced —
        // but the shared overrides still apply.
        var options = RagOptionsFactory.Build(
            Config(
                ("Orkeon:Rag:Profile", "balanced"),
                ("Orkeon:Rag:Context:MaxTokens", "4000")),
            "fast");

        Assert.Equal("fast", options.Profile);
        Assert.False(options.Rerank.Enabled);
        Assert.Equal(4000, options.Context.MaxTokens);
    }

    [Fact]
    public void Build_UnknownProfile_FailsLoudly_ListingKnownProfiles()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => RagOptionsFactory.Build(Config(("Orkeon:Rag:Profile", "warp"))));

        Assert.Contains("warp", ex.Message, StringComparison.Ordinal);
        Assert.Contains("fast", ex.Message, StringComparison.Ordinal);
        Assert.Contains("balanced", ex.Message, StringComparison.Ordinal);
        Assert.Contains("quality", ex.Message, StringComparison.Ordinal);
    }
}
