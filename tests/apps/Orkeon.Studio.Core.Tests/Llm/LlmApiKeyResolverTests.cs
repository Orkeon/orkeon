using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Llm;

/// <summary>
/// The key the <c>orkeon-studio-config</c> probe presents is the one a run presents, read in the
/// run's order (STUDIO-54, decision 2): the configuration's <c>Llm:ApiKey</c> as the runner composes
/// it — <c>ORKEON_Llm__ApiKey</c> over the file's key over the unprefixed <c>Llm__ApiKey</c> (GAP-36)
/// —, then the variable <c>Llm:ApiKeyEnvVar</c> names, in the process and then in the user scope
/// (STUDIO-49). It presented the file's key first: with both, the test judged a key the run would not
/// send. <see cref="LlmApiKeyResolverParityTests"/> holds the order against the runtime's own reader.
/// </summary>
public sealed class LlmApiKeyResolverTests
{
    private const string UnprefixedApiKey = "Llm__ApiKey";

    private static Func<string, string?> Environment(string? apiKey) =>
        name => string.Equals(name, LlmPresets.DefaultApiKeyEnv, StringComparison.Ordinal) ? apiKey : null;

    [Fact]
    public void The_orkeon_variable_wins_over_the_files_key_as_in_a_run()
    {
        Assert.Equal("from-env", LlmApiKeyResolver.Resolve("from-file", null, Environment("from-env")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void The_orkeon_variable_serves_a_file_without_a_key(string? inline)
    {
        Assert.Equal("from-env", LlmApiKeyResolver.Resolve(inline, null, Environment("from-env")));
    }

    [Fact]
    public void The_files_key_serves_when_no_orkeon_variable_is_set()
    {
        Assert.Equal("from-file", LlmApiKeyResolver.Resolve("from-file", null, Environment(null)));
    }

    [Fact]
    public void The_unprefixed_variable_is_the_lowest_layer_under_the_file()
    {
        // GAP-36: the variables without a prefix are read too, below the file and the ORKEON_ ones.
        var environment = new FakeEnvironmentVariables();
        environment.Process[UnprefixedApiKey] = "sk-unprefixed";

        Assert.Equal("sk-unprefixed", LlmApiKeyResolver.Resolve(null, "ZAI_API_KEY", environment));
        Assert.Equal("sk-file", LlmApiKeyResolver.Resolve("sk-file", null, environment));

        environment.Process[LlmPresets.DefaultApiKeyEnv] = "sk-orkeon";
        Assert.Equal("sk-orkeon", LlmApiKeyResolver.Resolve("sk-file", null, environment));
    }

    [Fact]
    public void A_blank_orkeon_variable_hides_the_files_key_as_in_a_run()
    {
        // A layer that sets the key hides the ones under it, blank or not, and a blank value reads as
        // absent: the run then reads the reference — never the file's key.
        var environment = new FakeEnvironmentVariables();
        environment.Process[LlmPresets.DefaultApiKeyEnv] = "";
        environment.Process["ZAI_API_KEY"] = "sk-zai";

        Assert.Equal("sk-zai", LlmApiKeyResolver.Resolve("sk-file", "ZAI_API_KEY", environment));
        Assert.Null(LlmApiKeyResolver.Resolve("sk-file", null, environment));
    }

    [Fact]
    public void No_key_anywhere_resolves_to_null()
    {
        Assert.Null(LlmApiKeyResolver.Resolve(null, null, Environment(null)));
        Assert.Null(LlmApiKeyResolver.Resolve("  ", "  ", Environment("   ")));
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_off_every_source()
    {
        Assert.Equal("sk-a", LlmApiKeyResolver.Resolve("  sk-a  ", null, Environment(null)));
        Assert.Equal("sk-b", LlmApiKeyResolver.Resolve(null, null, Environment(" sk-b ")));
        Assert.Equal("sk-c", LlmApiKeyResolver.Resolve(null, " ZAI_API_KEY ", name => name == "ZAI_API_KEY" ? " sk-c " : null));
    }

    [Fact]
    public void The_reference_is_read_after_every_configured_key_in_the_process_then_the_user_scope()
    {
        var environment = new FakeEnvironmentVariables();
        environment.User["ZAI_API_KEY"] = "sk-user-scope";

        Assert.Equal("sk-user-scope", LlmApiKeyResolver.Resolve(null, "ZAI_API_KEY", environment));

        environment.Process["ZAI_API_KEY"] = "sk-process";
        Assert.Equal("sk-process", LlmApiKeyResolver.Resolve(null, "ZAI_API_KEY", environment));
        Assert.Equal("sk-inline", LlmApiKeyResolver.Resolve("sk-inline", "ZAI_API_KEY", environment));

        environment.Process[LlmPresets.DefaultApiKeyEnv] = "sk-native";
        Assert.Equal("sk-native", LlmApiKeyResolver.Resolve(null, "ZAI_API_KEY", environment));
        Assert.Equal("sk-native", LlmApiKeyResolver.Resolve("sk-inline", "ZAI_API_KEY", environment));
        // Read, never copied: the probe leaves the process as it found it.
        Assert.Empty(environment.Writes);
    }

    [Fact]
    public void The_reference_itself_is_read_in_the_runs_layers()
    {
        // Llm:ApiKeyEnvVar composes like every other key: ORKEON_ over the file over the unprefixed.
        var environment = new FakeEnvironmentVariables();
        environment.Process["ZAI_API_KEY"] = "sk-zai";
        environment.Process["DEEPSEEK_API_KEY"] = "sk-ds";
        environment.Process["Llm__ApiKeyEnvVar"] = "ZAI_API_KEY";

        Assert.Equal("sk-zai", LlmApiKeyResolver.Resolve(null, null, environment));
        Assert.Equal("sk-ds", LlmApiKeyResolver.Resolve(null, "DEEPSEEK_API_KEY", environment));

        environment.Process["ORKEON_Llm__ApiKeyEnvVar"] = "ZAI_API_KEY";
        Assert.Equal("sk-zai", LlmApiKeyResolver.Resolve(null, "DEEPSEEK_API_KEY", environment));
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
