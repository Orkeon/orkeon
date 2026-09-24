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

    [Fact]
    public void A_profile_without_a_key_variable_reads_the_one_the_runtime_reads()
    {
        var account = ProviderBalanceAccount.For(Profile("Maison", LlmProviderEndpoints.OpenRouter, keyVariable: null));

        Assert.Equal(LlmPresets.DefaultApiKeyEnv, account.KeyVariable);
        Assert.Equal(LlmProviderKeys.OpenRouter, account.Provider);
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
    /// What a run does too: a profile whose variable is empty lays no key over the child, which
    /// then reads the runtime's own variable — the balance read presents the key the run would.
    /// </summary>
    [Fact]
    public void An_empty_variable_falls_back_to_the_runtimes_own_as_a_run_does()
    {
        var keys = new FakeApiKeyStore();
        keys.Values[LlmPresets.DefaultApiKeyEnv] = "sk-runtime";
        var target = Assert.Single(ProviderBalanceTarget.For([Profile("Rapide", LlmProviderEndpoints.DeepSeek)]));

        Assert.Equal("sk-runtime", target.RequestWith(keys).ApiKey);
        Assert.Null(target.RequestWith(new FakeApiKeyStore()).ApiKey);
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
