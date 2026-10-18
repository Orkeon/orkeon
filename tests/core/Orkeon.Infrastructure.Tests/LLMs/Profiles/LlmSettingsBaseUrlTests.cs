using Microsoft.Extensions.Configuration;
using Orkeon.Infrastructure.LLMs.Profiles;

namespace Orkeon.Infrastructure.Tests.LLMs.Profiles;

/// <summary>
/// CH-09: an address the Domain reading refuses stops the read, naming the setting's path and the
/// address, in the <c>Llm</c> section and in a profile.
/// </summary>
public sealed class LlmSettingsBaseUrlTests
{
    private static IConfiguration Configure(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    [Theory]
    [Trait("BR", "CH-09")]
    [InlineData("localhost:11434")]
    [InlineData("https://api.moonshot.ai/anthropic")]
    public void ShouldRefuseTheBaseUrl_WhenItHasNoScheme(string address)
    {
        var inDefault = Configure(("Llm:BaseUrl", address), ("Llm:Model", "m"));
        var inProfile = Configure(("Llm:Profiles:local:BaseUrl", address), ("Llm:Profiles:local:Model", "m"));

        var fromDefault = Assert.Throws<InvalidOperationException>(() => LlmSettings.ReadDefault(inDefault));
        var fromProfile = Assert.Throws<InvalidOperationException>(() => LlmSettings.ReadProfiles(inProfile));

        Assert.Contains("Llm:BaseUrl", fromDefault.Message, StringComparison.Ordinal);
        Assert.Contains(address, fromDefault.Message, StringComparison.Ordinal);
        Assert.Contains("Llm:Profiles:local:BaseUrl", fromProfile.Message, StringComparison.Ordinal);
        Assert.Contains(address, fromProfile.Message, StringComparison.Ordinal);
    }
}
