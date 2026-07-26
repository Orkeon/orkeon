using Microsoft.Extensions.Configuration;
using Orkeon.Rag.Abstractions.Options;
using Orkeon.Rag.Configuration;

namespace Orkeon.Rag.Tests.Options;

/// <summary>
/// Tests for the <see cref="MmrOptions"/> node of the <c>Orkeon:Rag</c> tree
/// (RAG-05/C2): opt-in default, additive binding under <c>Retrieval:Mmr</c>.
/// </summary>
public class MmrOptionsBindingTests
{
    private static IConfiguration Config(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build();

    [Fact]
    public void Defaults_AreDisabledWithRelevanceLeaningLambda()
    {
        var options = new MmrOptions();

        Assert.False(options.Enabled);
        Assert.Equal(0.7, options.Lambda);
        Assert.Equal(MmrOptions.DefaultLambda, options.Lambda);
    }

    [Theory]
    [InlineData("fast")]
    [InlineData("balanced")]
    [InlineData("quality")]
    public void EveryProfilePreset_KeepsMmrOptIn(string profile)
    {
        var options = RagProfilePresets.Create(RagProfilePresets.Parse(profile));

        Assert.False(options.Retrieval.Mmr.Enabled);
        Assert.Equal(MmrOptions.DefaultLambda, options.Retrieval.Mmr.Lambda);
    }

    [Fact]
    public void Bind_RetrievalMmrSection_OverridesThePreset()
    {
        var options = RagOptionsFactory.Build(Config(
            ("Orkeon:Rag:Retrieval:Mmr:Enabled", "true"),
            ("Orkeon:Rag:Retrieval:Mmr:Lambda", "0.35")));

        Assert.True(options.Retrieval.Mmr.Enabled);
        Assert.Equal(0.35, options.Retrieval.Mmr.Lambda);
    }

    [Fact]
    public void Bind_WithoutMmrKeys_KeepsTheDefaults()
    {
        var options = RagOptionsFactory.Build(Config(("Orkeon:Rag:Profile", "balanced")));

        Assert.False(options.Retrieval.Mmr.Enabled);
        Assert.Equal(MmrOptions.DefaultLambda, options.Retrieval.Mmr.Lambda);
    }
}
