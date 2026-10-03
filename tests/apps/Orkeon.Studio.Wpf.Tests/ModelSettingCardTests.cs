using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Capture.Fixtures;
using Orkeon.Studio.Wpf.ViewModels.Config;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-54, decision 1: a model setting keeps its card's stable name — <c>deepseek</c>,
/// <c>custom</c> —, never the card's title, and the editor opens it on that card whatever the
/// language. A value that is no card name — a title an older Studio wrote, in any language, a
/// provider the default blanked — is recognised as the store reads it, and written as a name with
/// the next gesture on the settings, never at startup.
/// </summary>
public sealed class ModelSettingCardTests
{
    private static ModelProfilesViewModel Screen(IModelProfileStore store, AppSettingsDocument document, List<int>? edits = null)
    {
        var llm = new LlmSectionViewModel(() => document, () => edits?.Add(edits.Count));
        return new ModelProfilesViewModel(store, llm, probe: new FakeLlmEndpointProbe(), keyStore: new FakeApiKeyStore());
    }

    [Fact]
    public async Task The_settings_of_the_capture_open_with_their_cards()
    {
        // The capture writes card names (Provider = LlmPresets.DeepSeek): they opened without a card.
        var store = new InMemoryModelProfileStore();
        await store.SaveAsync(StudioFixture.Seeded.Profiles, TestContext.Current.CancellationToken);
        var profiles = Screen(store, AppSettingsDocument.CreateEmpty());
        await profiles.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(profiles.Set.Profiles);
        foreach (var setting in profiles.Set.Profiles)
        {
            profiles.BeginEdit(setting);
            Assert.Equal(setting.Provider, profiles.Editor!.SelectedProvider?.Name);
            profiles.CancelEdit();
        }

        Assert.Equal("DeepSeek · deepseek-chat", profiles.Profiles.Single(row => row.Name == StudioFixture.AssistantProfile).Summary);
    }

    [Fact]
    public async Task An_older_french_settings_file_opens_with_its_cards_and_is_written_with_their_names_at_the_next_gesture()
    {
        var path = Path.Combine(Path.GetTempPath(), $"orkeon-cards-{Guid.NewGuid():N}.json");
        const string olderStudio = """
            {
              "Profiles": [
                { "Name": "Boîte", "Provider": "Autre compatible OpenAI", "Model": "qwen3", "BaseUrl": "https://llm.example.com/v1", "KeyEnvName": "ORKEON_CUSTOM_LLM_API_KEY" },
                { "Name": "Abîmé", "Model": "qwen3", "BaseUrl": "https://llm.example.com/v1" }
              ],
              "DefaultProfile": "Boîte"
            }
            """;
        try
        {
            await File.WriteAllTextAsync(path, olderStudio, TestContext.Current.CancellationToken);
            var edits = new List<int>();
            var document = AppSettingsDocument.CreateEmpty();
            var store = new MockModelProfileStore(new ModelProfileFileStore(path));
            var profiles = Screen(store, document, edits);

            await profiles.InitializeAsync(TestContext.Current.CancellationToken);

            // Startup: recognised, shown in the language of the moment — and nothing written.
            Assert.Equal(["Other OpenAI-compatible", "Other OpenAI-compatible"], profiles.Profiles.Select(row => row.Provider));
            Assert.Equal(0, store.Saves);
            Assert.Equal(olderStudio, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            Assert.Empty(edits);

            // The setting the default damaged finds its card again, and the card's own key variable.
            profiles.BeginEdit(profiles.Set.Find("Abîmé")!);
            var editor = profiles.Editor!;
            Assert.Equal(LlmPresets.Custom, editor.SelectedProvider?.Name);
            Assert.True(editor.RequiresApiKey);
            Assert.Equal(LlmPresets.CustomApiKeyEnv, editor.ApiKeyEnvName);
            editor.SaveCommand.Execute(null);

            Assert.Equal(LlmPresets.CustomApiKeyEnv, document.Llm.Profiles.Get("abime")?.ApiKeyEnvVar);
            Assert.Equal(LlmPresets.CustomApiKeyEnv, document.Llm.Profiles.Get("boite")?.ApiKeyEnvVar);
            await store.LastSave;
            var written = await new ModelProfileFileStore(path).LoadAsync(TestContext.Current.CancellationToken);
            Assert.Equal([LlmPresets.Custom, LlmPresets.Custom], written.Set.Profiles.Select(setting => setting.Provider));
            var raw = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.DoesNotContain("Autre compatible OpenAI", raw, StringComparison.Ordinal);
            Assert.Contains("\"Provider\": \"custom\"", raw, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
