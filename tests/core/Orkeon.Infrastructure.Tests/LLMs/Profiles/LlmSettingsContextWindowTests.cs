using Microsoft.Extensions.Configuration;
using Orkeon.Infrastructure.LLMs.Profiles;

namespace Orkeon.Infrastructure.Tests.LLMs.Profiles;

/// <summary>#432: <c>ContextWindowTokens</c> of the <c>Llm</c> section and of each profile, carried on the config.</summary>
public sealed class LlmSettingsContextWindowTests
{
    [Fact]
    [Trait("BR", "CH-432")]
    public void ShouldCarryEachSectionsOwnWindow_WhenTheDefaultAndAProfileSetOne()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Model"] = "m",
                ["Llm:ContextWindowTokens"] = "128000",
                ["Llm:Profiles:a:Model"] = "m",
                ["Llm:Profiles:a:ContextWindowTokens"] = "32000",
                ["Llm:Profiles:b:Model"] = "m",
            })
            .Build();

        // Act
        var profiles = LlmSettings.ReadProfiles(config).ToDictionary(p => p.Name, p => p.Config.ContextWindowTokens);

        // Assert
        Assert.Equal(128_000, LlmSettings.ReadDefault(config).ContextWindowTokens);
        Assert.Equal(32_000, profiles["a"]);
        Assert.Null(profiles["b"]);
    }
}
