using Microsoft.Extensions.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Profiles;

namespace Orkeon.Infrastructure.Tests.LLMs.Profiles;

/// <summary>
/// #254: <c>Cache</c> of the <c>Llm</c> section and of each profile - the host default prompt cache,
/// validated as a crew file's <c>cache:</c> block is; absent or marking nothing reads no cache.
/// </summary>
public sealed class LlmSettingsCacheTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    [Trait("BR", "CH-254")]
    public void ShouldReadNoCache_WhenTheSectionSetsNone()
    {
        var absent = Configuration(("Llm:Model", "m"), ("Llm:Profiles:a:Model", "m"));
        var nothingMarked = Configuration(("Llm:Model", "m"), ("Llm:Cache:SystemPrompt", "false"));

        Assert.Null(LlmSettings.ReadDefault(absent).Cache);
        Assert.Null(Assert.Single(LlmSettings.ReadProfiles(absent)).Config.Cache);
        Assert.Null(LlmSettings.ReadDefault(nothingMarked).Cache);
    }

    [Fact]
    [Trait("BR", "CH-254")]
    public void ShouldReadTheDefaultAndEachProfilesOwnCache()
    {
        var config = Configuration(
            ("Llm:Model", "m"),
            ("Llm:Cache:SystemPrompt", "true"),
            ("Llm:Cache:Ttl", "1h"),
            ("Llm:Profiles:a:Model", "m"),
            ("Llm:Profiles:a:Cache:Tools", "true"),
            ("Llm:Profiles:b:Model", "m"));

        var profiles = LlmSettings.ReadProfiles(config).ToDictionary(p => p.Name, p => p.Config.Cache);

        Assert.Equal(new LlmCacheConfig { CacheSystemPrompt = true, Ttl = "1h" }, LlmSettings.ReadDefault(config).Cache);
        Assert.Equal(new LlmCacheConfig { CacheTools = true }, profiles["a"]);
        Assert.Null(profiles["b"]);
    }

    [Theory]
    [Trait("BR", "CH-254")]
    [InlineData("true", "2h", "5m or 1h")]
    [InlineData("false", "1h", "caches nothing on its own")]
    public void ShouldRefuseTheLoad_WhenTheTtlIsInvalid(string systemPrompt, string ttl, string reason)
    {
        var config = Configuration(("Llm:Model", "m"), ("Llm:Cache:SystemPrompt", systemPrompt), ("Llm:Cache:Ttl", ttl));

        var ex = Assert.Throws<InvalidOperationException>(() => LlmSettings.ReadDefault(config));

        Assert.Contains("Llm:Cache:Ttl", ex.Message, StringComparison.Ordinal);
        Assert.Contains(reason, ex.Message, StringComparison.Ordinal);
    }
}
