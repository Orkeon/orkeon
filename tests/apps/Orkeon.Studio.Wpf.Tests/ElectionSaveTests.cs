using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Validation;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-53: the settings screen saves what the election just wrote. Electing a setting that pins
/// a timeout — the 600 s prefilled for DeepSeek, Kimi, Z.AI and MiniMax — wrote an integer the
/// validator then read as « not a number », and every save was refused, the novice's automatic one
/// included, until Studio read the file again.
/// </summary>
public sealed class ElectionSaveTests
{
    private const string SettingsPath = "/home/user/.config/Orkeon/appsettings.json";

    private static void Create(ModelProfilesViewModel profiles, string name, string provider, string? key = null)
    {
        profiles.NewProfileCommand.Execute(null);
        var editor = profiles.Editor!;
        editor.SelectedProvider = editor.Providers.First(p => p.Name == provider);
        editor.Name = name;
        if (key is not null)
            editor.ApiKeyInput = key;
        Assert.True(editor.SaveCommand.CanExecute(null), editor.HostIdText);
        editor.SaveCommand.Execute(null);
    }

    [Theory]
    [InlineData("novice")]
    [InlineData("expert")]
    public async Task Electing_a_setting_that_pins_a_timeout_then_saving_writes_the_election(string mode)
    {
        var store = new FakeAppSettingsStore();
        var config = new ConfigTabViewModel(
            new StudioServices { SettingsStore = store, Directories = new FakeDirectoryProbe("/data") },
            globalPathOverride: SettingsPath);
        var profiles = new ModelProfilesViewModel(
            new InMemoryModelProfileStore(), config.Llm, probe: new FakeLlmEndpointProbe(), keyStore: new FakeApiKeyStore());
        _ = new SettingsScreenViewModel(config, profiles, new UiModeViewModel(mode));
        config.Mounts.AddMount().PhysicalPath = "/data";
        Create(profiles, "Local", LlmPresets.Ollama);
        Create(profiles, "DeepSeek", LlmPresets.DeepSeek, key: "sk-ds");

        profiles.SetDefault("DeepSeek");
        if (mode == "expert")
            Assert.True(await config.SaveAsync(TestContext.Current.CancellationToken), config.StatusMessage);

        // The novice's edit saved itself; the expert's explicit save went through.
        Assert.DoesNotContain(config.ValidationMessages, m => m.Code == ValidationCodes.InvalidFieldType);
        Assert.False(config.IsDirty, config.StatusMessage);
        // Studio reopened reads the election from the file, its timeout included.
        var saved = AppSettingsDocument.Parse(store.Files[SettingsPath]);
        Assert.Equal("https://api.deepseek.com", saved.Llm.BaseUrl);
        Assert.Equal("DEEPSEEK_API_KEY", saved.Llm.ApiKeyEnvVar);
        Assert.Equal(LlmPresets.ReasoningTimeoutSeconds, saved.Llm.TimeoutSeconds);
    }
}
