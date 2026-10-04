using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-55: a temperature that is no finite number is refused where it is typed — « Save » stays
/// inactive, exactly as for « abc » — and where it is read, as a model-settings file Studio cannot
/// convert. Nothing infinite reaches the store, the settings document or a launch, and nothing
/// throws. An address typed for the catch-all card must be an absolute http(s) URL too: the
/// settings screen would refuse afterwards what the editor took.
/// </summary>
public sealed class NonFiniteTemperatureTests
{
    private static ModelProfilesViewModel Screen(IModelProfileStore store, AppSettingsDocument document) =>
        new(store, new LlmSectionViewModel(() => document, () => { }), probe: new FakeLlmEndpointProbe(), keyStore: new FakeApiKeyStore());

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e400")]
    public async Task A_temperature_that_is_no_finite_number_blocks_the_save_and_writes_nothing(string typed)
    {
        var store = new InMemoryModelProfileStore();
        await store.SaveAsync(new ModelProfileSet
        {
            Profiles = [new ModelProfile { Name = "Kimi K3", Provider = "kimi", BaseUrl = "https://api.moonshot.ai/v1", Model = "kimi-k3", Temperature = 1 }],
            DefaultProfile = "Kimi K3",
        }, TestContext.Current.CancellationToken);
        var document = AppSettingsDocument.CreateEmpty();
        var profiles = Screen(store, document);
        await profiles.InitializeAsync(TestContext.Current.CancellationToken);
        var before = document.ToJson();

        profiles.BeginEdit(profiles.Set.Profiles[0]);
        profiles.Editor!.TemperatureText = typed;

        Assert.Null(profiles.Editor.ParsedTemperature);
        Assert.False(profiles.Editor.CanSave);
        profiles.Editor.SaveCommand.Execute(null);
        Assert.Equal(1, (await store.LoadAsync(TestContext.Current.CancellationToken)).Set.Profiles[0].Temperature);
        Assert.Equal(before, document.ToJson());
    }

    [Fact]
    public void The_catch_all_card_takes_only_an_absolute_http_address()
    {
        var profiles = Screen(new InMemoryModelProfileStore(), AppSettingsDocument.CreateEmpty());
        profiles.NewProfileCommand.Execute(null);
        var editor = profiles.Editor!;
        editor.Name = "Maison";
        editor.SelectedProvider = editor.Providers.Single(p => p.Name == LlmPresets.Custom);
        editor.Model = "local-model";

        editor.BaseUrl = "localhost:8080/v1";
        Assert.False(editor.CanSave);

        editor.BaseUrl = "http://localhost:8080/v1";
        Assert.True(editor.CanSave);
    }

    [Fact]
    public async Task A_model_settings_file_holding_an_infinite_temperature_reads_as_unreadable_and_launches_nothing_infinite()
    {
        var path = Path.Combine(Path.GetTempPath(), $"orkeon-infinite-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """
                {
                  "Profiles": [ { "Name": "Kimi", "Provider": "kimi", "BaseUrl": "https://api.moonshot.ai/v1", "Model": "kimi-k3", "Temperature": 1e400 } ],
                  "DefaultProfile": "Kimi"
                }
                """, TestContext.Current.CancellationToken);
            var profiles = Screen(new ModelProfileFileStore(path), AppSettingsDocument.CreateEmpty());

            await profiles.InitializeAsync(TestContext.Current.CancellationToken);

            Assert.True(profiles.HasLoadError);
            Assert.Contains("Temperature is not a finite number", profiles.LoadError, StringComparison.Ordinal);
            Assert.Empty(profiles.Set.Profiles);
            Assert.DoesNotContain(profiles.LaunchEnvironment().Values, value => value.Contains("Infinity", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
