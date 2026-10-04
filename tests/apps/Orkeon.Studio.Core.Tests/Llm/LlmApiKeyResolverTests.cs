using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Llm;

/// <summary>
/// The key the <c>orkeon-studio-config</c> probe presents is the one a run presents, read in the
/// run's order (STUDIO-54, decision 2): the configuration's <c>Llm:ApiKey</c> as the runner composes
/// it — the <c>ORKEON_</c> variables over the file's key over the unprefixed ones (GAP-36) —, then the
/// variable <c>Llm:ApiKeyEnvVar</c> names, in the process and then in the user scope (STUDIO-49). And
/// under every spelling a run reads (STUDIO-56, decision 1): the configuration compares the prefix and
/// the keys without case and reads <c>__</c> as <c>:</c>; the probe looked for the exact name. The
/// double compares names as Linux does. <see cref="LlmApiKeyResolverParityTests"/> holds the rule
/// against the runtime's own reader.
/// </summary>
public sealed class LlmApiKeyResolverTests
{
    private const string UnprefixedApiKey = "Llm__ApiKey";

    private static FakeEnvironmentVariables Environment(params (string Name, string Value)[] process)
    {
        var environment = new FakeEnvironmentVariables();
        foreach (var (name, value) in process)
            environment.Process[name] = value;
        return environment;
    }

    private static FakeEnvironmentVariables OrkeonKey(string? apiKey) =>
        apiKey is null ? new FakeEnvironmentVariables() : Environment((LlmPresets.DefaultApiKeyEnv, apiKey));

    private static string? Key(string? inline, string? reference, IEnvironmentVariables environment) =>
        LlmApiKeyResolver.Resolve(inline, reference, environment).Key;

    [Fact]
    public void The_orkeon_variable_wins_over_the_files_key_as_in_a_run()
    {
        Assert.Equal("from-env", Key("from-file", null, OrkeonKey("from-env")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void The_orkeon_variable_serves_a_file_without_a_key(string? inline)
    {
        Assert.Equal("from-env", Key(inline, null, OrkeonKey("from-env")));
    }

    [Fact]
    public void The_files_key_serves_when_no_orkeon_variable_is_set()
    {
        Assert.Equal("from-file", Key("from-file", null, OrkeonKey(null)));
    }

    [Fact]
    public void The_unprefixed_variable_is_the_lowest_layer_under_the_file()
    {
        // GAP-36: the variables without a prefix are read too, below the file and the ORKEON_ ones.
        var environment = Environment((UnprefixedApiKey, "sk-unprefixed"));

        Assert.Equal("sk-unprefixed", Key(null, "ZAI_API_KEY", environment));
        Assert.Equal("sk-file", Key("sk-file", null, environment));

        environment.Process[LlmPresets.DefaultApiKeyEnv] = "sk-orkeon";
        Assert.Equal("sk-orkeon", Key("sk-file", null, environment));
    }

    [Fact]
    public void A_blank_orkeon_variable_hides_the_files_key_as_in_a_run()
    {
        // A layer that sets the key hides the ones under it, blank or not, and a blank value reads as
        // absent: the run then reads the reference — never the file's key.
        var environment = Environment((LlmPresets.DefaultApiKeyEnv, ""), ("ZAI_API_KEY", "sk-zai"));

        Assert.Equal("sk-zai", Key("sk-file", "ZAI_API_KEY", environment));
        Assert.Null(Key("sk-file", null, environment));
    }

    [Fact]
    public void No_key_anywhere_resolves_to_none()
    {
        Assert.Equal(LlmApiKeyResolution.None, LlmApiKeyResolver.Resolve(null, null, OrkeonKey(null)));
        Assert.Null(Key("  ", "  ", OrkeonKey("   ")));
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_off_every_source()
    {
        Assert.Equal("sk-a", Key("  sk-a  ", null, OrkeonKey(null)));
        Assert.Equal("sk-b", Key(null, null, OrkeonKey(" sk-b ")));
        Assert.Equal("sk-c", Key(null, " ZAI_API_KEY ", Environment(("ZAI_API_KEY", " sk-c "))));
    }

    [Fact]
    public void The_reference_is_read_after_every_configured_key_in_the_process_then_the_user_scope()
    {
        var environment = new FakeEnvironmentVariables();
        environment.User["ZAI_API_KEY"] = "sk-user-scope";

        Assert.Equal("sk-user-scope", Key(null, "ZAI_API_KEY", environment));

        environment.Process["ZAI_API_KEY"] = "sk-process";
        Assert.Equal("sk-process", Key(null, "ZAI_API_KEY", environment));
        Assert.Equal("sk-inline", Key("sk-inline", "ZAI_API_KEY", environment));

        environment.Process[LlmPresets.DefaultApiKeyEnv] = "sk-native";
        Assert.Equal("sk-native", Key(null, "ZAI_API_KEY", environment));
        Assert.Equal("sk-native", Key("sk-inline", "ZAI_API_KEY", environment));
        // Read, never copied: the probe leaves the process as it found it.
        Assert.Empty(environment.Writes);
    }

    [Fact]
    public void The_reference_itself_is_read_in_the_runs_layers()
    {
        // Llm:ApiKeyEnvVar composes like every other key: ORKEON_ over the file over the unprefixed.
        var environment = Environment(
            ("ZAI_API_KEY", "sk-zai"),
            ("DEEPSEEK_API_KEY", "sk-ds"),
            ("Llm__ApiKeyEnvVar", "ZAI_API_KEY"));

        Assert.Equal("sk-zai", Key(null, null, environment));
        Assert.Equal("sk-ds", Key(null, "DEEPSEEK_API_KEY", environment));

        environment.Process["ORKEON_Llm__ApiKeyEnvVar"] = "ZAI_API_KEY";
        Assert.Equal("sk-zai", Key(null, "DEEPSEEK_API_KEY", environment));
    }

    [Fact]
    public void A_user_scope_that_cannot_be_read_is_a_key_not_found()
    {
        Assert.Null(Key(null, "ZAI_API_KEY", new UnreadableUserScope()));
    }

    // STUDIO-56, decision 1: every spelling the configuration reads ────────────────────────────

    [Theory]
    [InlineData("ORKEON_LLM__APIKEY")]
    [InlineData("orkeon_llm__apikey")]
    [InlineData("Orkeon_lLm__aPiKeY")]
    [InlineData("ORKEON_Llm:ApiKey")]
    public void An_orkeon_spelling_a_run_reads_wins_over_the_files_key(string name)
    {
        Assert.Equal("sk-spelled", Key("sk-file", null, Environment((name, "sk-spelled"))));
    }

    [Theory]
    [InlineData("LLM__APIKEY")]
    [InlineData("llm__apikey")]
    [InlineData("Llm:ApiKey")]
    public void An_unprefixed_spelling_a_run_reads_serves_beneath_the_file(string name)
    {
        var environment = Environment((name, "sk-spelled"));

        Assert.Equal("sk-file", Key("sk-file", null, environment));
        Assert.Equal("sk-spelled", Key(null, null, environment));
    }

    [Fact]
    public void A_reference_under_another_spelling_names_the_variable_read()
    {
        var environment = Environment(("ORKEON_LLM__APIKEYENVVAR", "ZAI_API_KEY"), ("ZAI_API_KEY", "sk-zai"));

        Assert.Equal("sk-zai", Key(null, "DEEPSEEK_API_KEY", environment));
    }

    [Theory]
    [InlineData("ORKEON_Llm___ApiKey")]
    [InlineData("ORKEON_Llm_ApiKey")]
    [InlineData("ORKEONLlm__ApiKey")]
    [InlineData("XORKEON_Llm__ApiKey")]
    public void A_name_the_configuration_does_not_read_as_the_key_is_nothing(string name)
    {
        Assert.Null(Key(null, null, Environment((name, "sk-not-read"))));
    }

    [Fact]
    public void The_variable_a_reference_names_is_read_by_its_exact_name_as_in_a_run()
    {
        // The run reads Llm:ApiKeyEnvVar's variable by its name: under Linux, case included.
        Assert.Null(Key(null, "zai_api_key", Environment(("ZAI_API_KEY", "sk-zai"))));
    }

    [Fact]
    public void Two_spellings_with_different_values_are_a_conflict_naming_both()
    {
        var resolution = LlmApiKeyResolver.Resolve(
            "sk-file", null, Environment(("ORKEON_Llm__ApiKey", "sk-a"), ("ORKEON_LLM__APIKEY", "sk-b")));

        Assert.True(resolution.IsConflict);
        Assert.Null(resolution.Key);
        Assert.Equal(LlmApiKeyResolver.ApiKeySetting, resolution.Setting);
        Assert.Equal(["ORKEON_LLM__APIKEY", "ORKEON_Llm__ApiKey"], resolution.ConflictingVariables);
    }

    [Fact]
    public void A_blank_spelling_beside_a_full_one_is_a_conflict()
    {
        var resolution = LlmApiKeyResolver.Resolve(
            null, null, Environment(("ORKEON_Llm__ApiKey", ""), ("orkeon_llm__apikey", "sk-a")));

        Assert.True(resolution.IsConflict);
    }

    [Fact]
    public void Two_spellings_with_the_same_value_are_that_value()
    {
        var resolution = LlmApiKeyResolver.Resolve(
            null, null, Environment(("ORKEON_Llm__ApiKey", "sk-a"), ("ORKEON_LLM__APIKEY", "sk-a")));

        Assert.False(resolution.IsConflict);
        Assert.Equal("sk-a", resolution.Key);
    }

    [Fact]
    public void A_conflict_in_a_hidden_layer_is_no_conflict()
    {
        var environment = Environment(
            ("ORKEON_Llm__ApiKey", "sk-orkeon"),
            ("Llm__ApiKey", "sk-a"),
            ("LLM__APIKEY", "sk-b"));

        var resolution = LlmApiKeyResolver.Resolve(null, null, environment);

        Assert.False(resolution.IsConflict);
        Assert.Equal("sk-orkeon", resolution.Key);
        // The file's key hides the unprefixed layer just as well.
        environment.Process.Remove("ORKEON_Llm__ApiKey");
        Assert.Equal("sk-file", Key("sk-file", null, environment));
        Assert.True(LlmApiKeyResolver.Resolve(null, null, environment).IsConflict);
    }

    [Fact]
    public void Two_spellings_of_the_reference_with_different_names_are_a_conflict_on_the_reference()
    {
        var resolution = LlmApiKeyResolver.Resolve(
            null, null, Environment(("ORKEON_Llm__ApiKeyEnvVar", "ZAI_API_KEY"), ("ORKEON_LLM__APIKEYENVVAR", "DEEPSEEK_API_KEY")));

        Assert.True(resolution.IsConflict);
        Assert.Equal(LlmApiKeyResolver.ApiKeyEnvVarSetting, resolution.Setting);
    }

    /// <summary>A user scope whose every read is refused, as a locked-down registry.</summary>
    private sealed class UnreadableUserScope : IEnvironmentVariables
    {
        public string? Read(string name, EnvironmentVariableTarget target) =>
            target == EnvironmentVariableTarget.User
                ? throw new UnauthorizedAccessException("registry access denied")
                : null;

        public IReadOnlyList<KeyValuePair<string, string>> ReadAll(EnvironmentVariableTarget target) => [];

        public void Write(string name, string? value, EnvironmentVariableTarget target) =>
            throw new InvalidOperationException("a probe never writes");
    }
}
