using Microsoft.Extensions.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Profiles;

namespace Orkeon.Infrastructure.Tests.LLMs.Profiles;

/// <summary>
/// BR-05, BR-13: <c>CallMode</c> of the <c>Llm</c> section and of each profile — Auto when absent
/// or empty, case ignored, an unknown value refuses the load.
/// </summary>
public sealed class LlmSettingsCallModeTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    [Trait("BR", "BR-05")]
    public void ShouldReadAuto_WhenTheSectionNamesNoCallMode()
    {
        var absent = Configuration(("Llm:Model", "m"), ("Llm:Profiles:a:Model", "m"));
        var empty = Configuration(("Llm:Model", "m"), ("Llm:CallMode", ""), ("Llm:Profiles:a:Model", "m"), ("Llm:Profiles:a:CallMode", ""));

        Assert.Equal(LlmCallMode.Auto, LlmSettings.ReadDefault(absent).CallMode);
        Assert.Equal(LlmCallMode.Auto, LlmSettings.ReadDefault(empty).CallMode);
        Assert.Equal(LlmCallMode.Auto, Assert.Single(LlmSettings.ReadProfiles(absent)).Config.CallMode);
        Assert.Equal(LlmCallMode.Auto, Assert.Single(LlmSettings.ReadProfiles(empty)).Config.CallMode);
    }

    [Fact]
    [Trait("BR", "BR-05")]
    [Trait("BR", "BR-13")]
    public void ShouldReadEachProfilesOwnCallMode()
    {
        var config = Configuration(
            ("Llm:Model", "m"),
            ("Llm:CallMode", "buffer"),
            ("Llm:Profiles:a:Model", "m"),
            ("Llm:Profiles:a:CallMode", "Stream"),
            ("Llm:Profiles:b:Model", "m"));

        var profiles = LlmSettings.ReadProfiles(config).ToDictionary(p => p.Name, p => p.Config.CallMode);

        Assert.Equal(LlmCallMode.Buffer, LlmSettings.ReadDefault(config).CallMode);
        Assert.Equal(LlmCallMode.Stream, profiles["a"]);
        Assert.Equal(LlmCallMode.Auto, profiles["b"]);
    }

    [Fact]
    [Trait("BR", "BR-05")]
    public void ShouldRefuseTheLoad_WhenTheCallModeIsUnknown()
    {
        var config = Configuration(("Llm:Profiles:a:Model", "m"), ("Llm:Profiles:a:CallMode", "fast"));

        var ex = Assert.Throws<InvalidOperationException>(() => LlmSettings.ReadProfiles(config));

        Assert.Contains("Llm:Profiles:a:CallMode", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Auto", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Buffer", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Stream", ex.Message, StringComparison.Ordinal);
    }
}
