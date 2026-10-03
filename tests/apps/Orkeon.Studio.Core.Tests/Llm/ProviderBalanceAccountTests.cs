using Orkeon.Constants.Llm;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Llm;

/// <summary>
/// Which accounts a balance read asks about (STUDIO-35 D-01): one per provider, host and key
/// variable, so two profiles on the same account cost one request — and a profile with no
/// account behind it (a local runtime, no endpoint) costs none.
/// </summary>
public sealed class ProviderBalanceAccountTests
{
    private const string DeepSeekKey = "DEEPSEEK_API_KEY";

    private static ModelProfile Profile(string name, string? baseUrl, string? keyVariable = DeepSeekKey, string model = "deepseek-chat") =>
        new() { Name = name, Provider = "DeepSeek", Model = model, BaseUrl = baseUrl, KeyEnvName = keyVariable };

    [Fact]
    public void Two_profiles_on_one_provider_and_one_key_variable_make_one_target()
    {
        var targets = ProviderBalanceTarget.For(
        [
            Profile("Rapide", LlmProviderEndpoints.DeepSeek),
            Profile("Raisonneur", LlmProviderEndpoints.DeepSeek, model: "deepseek-reasoner"),
        ]);

        var target = Assert.Single(targets);
        Assert.Equal(new ProviderBalanceAccount(LlmProviderKeys.DeepSeek, "api.deepseek.com", DeepSeekKey), target.Account);
        Assert.Equal(["Rapide", "Raisonneur"], target.Profiles);
    }

    [Fact]
    public void The_same_provider_under_two_key_variables_makes_two_targets()
    {
        var targets = ProviderBalanceTarget.For(
        [
            Profile("Perso", LlmProviderEndpoints.DeepSeek),
            Profile("Travail", LlmProviderEndpoints.DeepSeek, keyVariable: "DEEPSEEK_WORK_KEY"),
        ]);

        Assert.Equal([DeepSeekKey, "DEEPSEEK_WORK_KEY"], targets.Select(t => t.Account.KeyVariable));
    }

    /// <summary>A key of Kimi's .cn platform is refused by the .ai host: the host is part of the account.</summary>
    [Fact]
    public void Kimi_on_its_two_platforms_makes_two_targets()
    {
        var targets = ProviderBalanceTarget.For(
        [
            Profile("Kimi", LlmProviderEndpoints.Kimi, "MOONSHOT_API_KEY"),
            Profile("Kimi Chine", $"https://{LlmProviderEndpoints.KimiChinaHost}/v1", "MOONSHOT_API_KEY"),
        ]);

        Assert.Equal(["api.moonshot.ai", LlmProviderEndpoints.KimiChinaHost], targets.Select(t => t.Account.Host));
        Assert.All(targets, t => Assert.Equal(LlmProviderKeys.Kimi, t.Account.Provider));
    }

    [Fact]
    public void The_path_of_an_endpoint_does_not_split_its_account()
    {
        var targets = ProviderBalanceTarget.For(
        [
            Profile("Sans chemin", LlmProviderEndpoints.DeepSeek),
            Profile("Avec chemin", $"{LlmProviderEndpoints.DeepSeek}/v1"),
        ]);

        var target = Assert.Single(targets);
        Assert.Equal(LlmProviderEndpoints.DeepSeek, target.BaseUrl);
    }

    /// <summary>
    /// A profile that names no variable has no key to present (GAP-36, decision 2): its account
    /// names none either — not the runtime's own, the default's key, which a run of that profile
    /// never reads. The real store refuses a blank name: the request must not even ask for one.
    /// </summary>
    [Fact]
    public void A_profile_without_a_key_variable_has_an_account_without_one_and_presents_no_key()
    {
        var environment = new FakeEnvironmentVariables();
        environment.User[LlmPresets.DefaultApiKeyEnv] = "sk-runtime";
        var target = Assert.Single(ProviderBalanceTarget.For(
            [Profile("Maison", LlmProviderEndpoints.OpenRouter, keyVariable: null)]));

        Assert.Equal("", target.Account.KeyVariable);
        Assert.Equal(LlmProviderKeys.OpenRouter, target.Account.Provider);
        Assert.Null(target.RequestWith(new EnvironmentApiKeyStore(environment)).ApiKey);
        Assert.Equal("sk-typed", target.RequestWith(new EnvironmentApiKeyStore(environment), typedKey: "sk-typed").ApiKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("http://localhost:12434/engines/llama.cpp/v1")]
    [InlineData("http://127.0.0.1:8080/v1")]
    [InlineData("http://host.docker.internal:11434/v1")]
    public void A_local_runtime_or_a_missing_endpoint_has_no_account_to_ask(string? baseUrl)
    {
        Assert.Empty(ProviderBalanceTarget.For([Profile("Local", baseUrl, keyVariable: null)]));
    }

    [Fact]
    public void The_request_presents_the_key_of_the_accounts_variable()
    {
        var keys = new FakeApiKeyStore();
        keys.Values[DeepSeekKey] = "sk-deepseek";
        keys.Values[LlmPresets.DefaultApiKeyEnv] = "sk-runtime";
        var target = Assert.Single(ProviderBalanceTarget.For([Profile("Rapide", LlmProviderEndpoints.DeepSeek)]));

        var request = target.RequestWith(keys);

        Assert.Equal(LlmProviderEndpoints.DeepSeek, request.BaseUrl);
        Assert.Equal("sk-deepseek", request.ApiKey);
    }

    /// <summary>
    /// The account's key and it alone (GAP-36, decision 2): a variable that holds no key presents
    /// none — never the runtime's own, the default's key, which no run of this profile reads
    /// (STUDIO-49): the read refuses without a request, as the connection test does.
    /// </summary>
    [Fact]
    public void An_empty_variable_presents_no_key_even_with_the_runtimes_own_remembered()
    {
        var keys = new FakeApiKeyStore();
        keys.Values[LlmPresets.DefaultApiKeyEnv] = "sk-runtime";
        var target = Assert.Single(ProviderBalanceTarget.For([Profile("Rapide", LlmProviderEndpoints.DeepSeek)]));

        Assert.Null(target.RequestWith(keys).ApiKey);
        Assert.Null(target.RequestWith(new FakeApiKeyStore()).ApiKey);
    }

    /// <summary>The runtime's own variable serves when it is the account's: a profile that keeps its key there.</summary>
    [Fact]
    public void An_account_whose_variable_is_the_runtimes_own_presents_it()
    {
        var keys = new FakeApiKeyStore();
        keys.Values[LlmPresets.DefaultApiKeyEnv] = "sk-runtime";
        var target = Assert.Single(ProviderBalanceTarget.For(
            [Profile("Maison", LlmProviderEndpoints.DeepSeek, keyVariable: LlmPresets.DefaultApiKeyEnv)]));

        Assert.Equal(LlmPresets.DefaultApiKeyEnv, target.Account.KeyVariable);
        Assert.Equal("sk-runtime", target.RequestWith(keys).ApiKey);
    }

    [Fact]
    public void A_key_typed_in_the_editor_goes_before_the_remembered_one()
    {
        var keys = new FakeApiKeyStore();
        keys.Values[DeepSeekKey] = "sk-remembered";
        var target = Assert.Single(ProviderBalanceTarget.For([Profile("Rapide", LlmProviderEndpoints.DeepSeek)]));

        Assert.Equal("sk-typed", target.RequestWith(keys, typedKey: " sk-typed ").ApiKey);
    }

    /// <summary>The providers a threshold makes sense for: the three whose balance an inference key reads (STUDIO-33).</summary>
    [Fact]
    public void Only_deepseek_kimi_and_openrouter_serve_their_balance_to_an_inference_key()
    {
        Assert.Equal(
            [LlmProviderKeys.DeepSeek, LlmProviderKeys.Kimi, LlmProviderKeys.OpenRouter],
            HttpProviderBalanceProbe.ReadableProviders);
    }
}
