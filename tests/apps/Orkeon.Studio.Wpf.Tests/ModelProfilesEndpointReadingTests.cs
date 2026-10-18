using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;

namespace Orkeon.Studio.Wpf.Tests;

public sealed class ModelProfilesEndpointReadingTests
{
    [Fact]
    [Trait("BR", "CH-09")]
    public async Task ShouldRefuseTheTestWithoutARequest_WhenAnOllamaCardPointsElsewhere()
    {
        var probe = new FakeLlmEndpointProbe();
        var document = AppSettingsDocument.CreateEmpty();
        var llm = new LlmSectionViewModel(() => document, () => { });
        var profiles = new ModelProfilesViewModel(
            new InMemoryModelProfileStore(), llm, probe: probe, keyStore: new FakeApiKeyStore());
        profiles.BeginEdit(new ModelProfile
        {
            Name = "Local",
            Provider = LlmPresets.Ollama,
            Model = "llama3",
            BaseUrl = "http://localhost:8080/v1",
        });
        var editor = profiles.Editor!;

        await editor.TestConnectionAsync(TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(editor.EndpointReadingLine));
        Assert.False(editor.ShowLocalNote);
        Assert.Empty(probe.Requests);
        Assert.False(string.IsNullOrWhiteSpace(editor.ConnectionTestResult));
    }
}
