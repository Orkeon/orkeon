using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Llm;

/// <summary>
/// Studio recommends keeping the key out of the file, so the probe has to look where the
/// runtime itself looks — otherwise "Test connection" would fail for the users who followed
/// that advice. STUDIO-49: the order is the runtime's — the inline key, then
/// <c>ORKEON_Llm__ApiKey</c>, then the variable <c>Llm:ApiKeyEnvVar</c> names, in the process and
/// then in the user scope.
/// </summary>
public sealed class LlmApiKeyResolverTests
{
    private static Func<string, string?> Environment(string? apiKey) =>
        name => string.Equals(name, LlmPresets.DefaultApiKeyEnv, StringComparison.Ordinal) ? apiKey : null;

    [Fact]
    public void The_inline_key_wins_over_the_environment()
    {
        Assert.Equal("from-file", LlmApiKeyResolver.Resolve("from-file", null, Environment("from-env")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void The_environment_variable_is_the_fallback(string? inline)
    {
        Assert.Equal("from-env", LlmApiKeyResolver.Resolve(inline, null, Environment("from-env")));
    }

    [Fact]
    public void No_key_anywhere_resolves_to_null()
    {
        Assert.Null(LlmApiKeyResolver.Resolve(null, null, Environment(null)));
        Assert.Null(LlmApiKeyResolver.Resolve("  ", "  ", Environment("   ")));
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_off_either_source()
    {
        Assert.Equal("sk-a", LlmApiKeyResolver.Resolve("  sk-a  ", null, Environment(null)));
        Assert.Equal("sk-b", LlmApiKeyResolver.Resolve(null, null, Environment(" sk-b ")));
    }

    [Fact]
    public void The_reference_is_read_after_the_native_variable_in_the_process_then_the_user_scope()
    {
        var environment = new FakeEnvironmentVariables();
        environment.User["ZAI_API_KEY"] = "sk-user-scope";

        Assert.Equal("sk-user-scope", LlmApiKeyResolver.Resolve(null, "ZAI_API_KEY", environment));

        environment.Process["ZAI_API_KEY"] = "sk-process";
        Assert.Equal("sk-process", LlmApiKeyResolver.Resolve(null, "ZAI_API_KEY", environment));

        environment.Process[LlmPresets.DefaultApiKeyEnv] = "sk-native";
        Assert.Equal("sk-native", LlmApiKeyResolver.Resolve(null, "ZAI_API_KEY", environment));
        Assert.Equal("sk-inline", LlmApiKeyResolver.Resolve("sk-inline", "ZAI_API_KEY", environment));
        // Read, never copied: the probe leaves the process as it found it.
        Assert.Empty(environment.Writes);
    }

    [Fact]
    public void A_user_scope_that_cannot_be_read_is_a_key_not_found()
    {
        Assert.Null(LlmApiKeyResolver.Resolve(null, "ZAI_API_KEY", new UnreadableUserScope()));
    }

    /// <summary>A user scope whose every read is refused, as a locked-down registry.</summary>
    private sealed class UnreadableUserScope : IEnvironmentVariables
    {
        public string? Read(string name, EnvironmentVariableTarget target) =>
            target == EnvironmentVariableTarget.User
                ? throw new UnauthorizedAccessException("registry access denied")
                : null;

        public void Write(string name, string? value, EnvironmentVariableTarget target) =>
            throw new InvalidOperationException("a probe never writes");
    }
}
