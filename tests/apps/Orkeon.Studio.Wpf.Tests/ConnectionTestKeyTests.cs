using Orkeon.Constants.Llm;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Serialises the tests that set a variable in this process's own environment — the runtime's
/// <c>ORKEON_Llm__ApiKey</c> —, which every other test of the assembly would otherwise see.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    public const string Name = "studio-wpf-process-environment";
}

/// <summary>
/// GAP-36, decision 2: the editor's « Test connection » presents the setting's key and it alone —
/// the one typed, else the one remembered under the setting's variable. The runtime's own,
/// <c>ORKEON_Llm__ApiKey</c> — the default's key, which Studio's process holds when the user set
/// it —, serves only a setting whose variable it is: a setting that needs a key and has none is
/// refused without a request, and a setting that needs none presents none.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ConnectionTestKeyTests
{
    private const string DefaultsKey = "sk-the-defaults-deepseek-key";

    private static string CardTitle(string provider) =>
        LlmPresets.ProviderCatalogFor(EnglishStudioStrings.Instance).Single(card => card.Name == provider).Title;

    private static ModelProfileEditorViewModel Editor(FakeApiKeyStore keys, FakeLlmEndpointProbe probe, ModelProfile profile)
    {
        var document = AppSettingsDocument.CreateEmpty();
        var llm = new LlmSectionViewModel(() => document, () => { });
        var profiles = new ModelProfilesViewModel(new InMemoryModelProfileStore(), llm, probe: probe, keyStore: keys);
        profiles.BeginEdit(profile);
        return profiles.Editor!;
    }

    private static ModelProfile Zai(string keyVariable = "ZAI_API_KEY") => new()
    {
        Name = "GLM",
        Provider = CardTitle(LlmPresets.Zai),
        Model = "glm-5.2",
        BaseUrl = LlmProviderEndpoints.Zai,
        KeyEnvName = keyVariable,
    };

    /// <summary>
    /// The runtime's own key in this process while <paramref name="act"/> runs — as when the user
    /// set it before starting Studio —, and what was there before put back after.
    /// </summary>
    private static async Task WithTheDefaultsKeyInTheProcess(Func<Task> act)
    {
        var previous = Environment.GetEnvironmentVariable(LlmPresets.DefaultApiKeyEnv);
        Environment.SetEnvironmentVariable(LlmPresets.DefaultApiKeyEnv, DefaultsKey);
        try
        {
            await act();
        }
        finally
        {
            Environment.SetEnvironmentVariable(LlmPresets.DefaultApiKeyEnv, previous);
        }
    }

    [Fact]
    public async Task A_setting_without_its_key_is_refused_without_a_request_never_sent_the_defaults()
    {
        var keys = new FakeApiKeyStore();
        keys.Stage(LlmPresets.DefaultApiKeyEnv, DefaultsKey);   // what Studio's store reads in its own process
        var probe = new FakeLlmEndpointProbe();
        var editor = Editor(keys, probe, Zai());

        await WithTheDefaultsKeyInTheProcess(() => editor.TestConnectionAsync(TestContext.Current.CancellationToken));

        Assert.True(editor.RequiresApiKey);
        Assert.Empty(probe.Requests);
        Assert.Equal("API key missing — remember it first", editor.ConnectionTestResult);
    }

    [Fact]
    public async Task A_setting_that_needs_no_key_presents_none_not_even_a_draft_typed_for_another_card()
    {
        var keys = new FakeApiKeyStore();
        keys.Stage(LlmPresets.DefaultApiKeyEnv, DefaultsKey);
        var probe = new FakeLlmEndpointProbe();
        var editor = Editor(keys, probe, new ModelProfile
        {
            Name = "Docker",
            Provider = CardTitle(LlmPresets.DockerModelRunner),
            Model = LlmPresets.DockerModelRunnerDefaultModel,
            BaseUrl = LlmPresets.DockerModelRunnerBaseUrl,
        });

        await WithTheDefaultsKeyInTheProcess(async () =>
        {
            await editor.TestConnectionAsync(TestContext.Current.CancellationToken);
            // A key pasted on a card that needs one, then a click on Docker Model Runner: the
            // draft stays in the hidden field, and is not this setting's.
            editor.ApiKeyInput = "sk-typed-for-another-card";
            await editor.TestConnectionAsync(TestContext.Current.CancellationToken);
        });

        Assert.False(editor.RequiresApiKey);
        Assert.Equal(2, probe.Requests.Count);
        Assert.All(probe.Requests, request => Assert.Null(request.ApiKey));
    }

    [Fact]
    public async Task The_setting_presents_the_key_typed_else_the_one_remembered_under_its_variable()
    {
        var keys = new FakeApiKeyStore();
        keys.Stage(LlmPresets.DefaultApiKeyEnv, DefaultsKey);
        keys.Stage("ZAI_API_KEY", "sk-zai-remembered");
        var probe = new FakeLlmEndpointProbe();
        var editor = Editor(keys, probe, Zai());

        await WithTheDefaultsKeyInTheProcess(async () =>
        {
            await editor.TestConnectionAsync(TestContext.Current.CancellationToken);
            editor.ApiKeyInput = " sk-zai-typed ";
            await editor.TestConnectionAsync(TestContext.Current.CancellationToken);
        });

        Assert.Equal(["sk-zai-remembered", "sk-zai-typed"], probe.Requests.Select(request => request.ApiKey));
    }

    /// <summary>
    /// A setting created on « Other OpenAI-compatible » before STUDIO-49 keeps its key in the
    /// runtime's own variable: that variable is the setting's, so its key is presented.
    /// </summary>
    [Fact]
    public async Task The_runtimes_own_variable_serves_a_setting_whose_variable_it_is()
    {
        var keys = new FakeApiKeyStore();
        keys.Stage(LlmPresets.DefaultApiKeyEnv, DefaultsKey);
        var probe = new FakeLlmEndpointProbe();
        var editor = Editor(keys, probe, new ModelProfile
        {
            Name = "Box",
            Provider = CardTitle(LlmPresets.Custom),
            Model = "qwen3",
            BaseUrl = "https://llm.example.com/v1",
            KeyEnvName = LlmPresets.DefaultApiKeyEnv,
        });

        await WithTheDefaultsKeyInTheProcess(() => editor.TestConnectionAsync(TestContext.Current.CancellationToken));

        Assert.Equal(DefaultsKey, Assert.Single(probe.Requests).ApiKey);
    }
}
