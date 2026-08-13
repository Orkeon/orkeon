using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Presets;

namespace Orkeon.Studio.Core.Tests.Llm;

/// <summary>
/// Studio recommends keeping the key out of the file, so the probe has to look where the
/// runtime itself looks — otherwise "Test connection" would fail for the users who followed
/// that advice.
/// </summary>
public sealed class LlmApiKeyResolverTests
{
    private static Func<string, string?> Environment(string? apiKey) =>
        name => string.Equals(name, LlmPresets.DefaultApiKeyEnv, StringComparison.Ordinal) ? apiKey : null;

    [Fact]
    public void The_inline_key_wins_over_the_environment()
    {
        Assert.Equal("from-file", LlmApiKeyResolver.Resolve("from-file", Environment("from-env")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void The_environment_variable_is_the_fallback(string? inline)
    {
        Assert.Equal("from-env", LlmApiKeyResolver.Resolve(inline, Environment("from-env")));
    }

    [Fact]
    public void No_key_anywhere_resolves_to_null()
    {
        Assert.Null(LlmApiKeyResolver.Resolve(null, Environment(null)));
        Assert.Null(LlmApiKeyResolver.Resolve("  ", Environment("   ")));
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_off_either_source()
    {
        Assert.Equal("sk-a", LlmApiKeyResolver.Resolve("  sk-a  ", Environment(null)));
        Assert.Equal("sk-b", LlmApiKeyResolver.Resolve(null, Environment(" sk-b ")));
    }
}
