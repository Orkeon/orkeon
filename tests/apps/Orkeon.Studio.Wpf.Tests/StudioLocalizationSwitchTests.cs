using System.ComponentModel;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Wpf.ViewModels.Mounts;
using Orkeon.Studio.Wpf.ViewModels.Services;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-11 tranche 2: the ViewModels resolve their fabricated strings through the
/// localization port and re-emit their bindings when the culture changes at runtime —
/// the WPF hot language switch, exercised here with a hand-written switchable port.
/// </summary>
public sealed class StudioLocalizationSwitchTests
{
    /// <summary>A port that can flip between the English defaults and a tiny French table.</summary>
    private sealed class SwitchableStrings : IStudioStrings
    {
        private static readonly Dictionary<string, string> French = new(StringComparer.Ordinal)
        {
            [StudioStringKeys.ConfigNoProblem] = "Aucun problème détecté.",
            [StudioStringKeys.LaunchReady] = "Prêt à lancer.",
            [StudioStringKeys.RightsReadOnly] = "Lecture seule",
            [StudioStringKeys.PresetNoneTitle] = "Aucun / hors ligne",
            [StudioStringKeys.PresetCustomTitle] = "Autre compatible OpenAI",
            [StudioStringKeys.PresetNoneDescription] = "Pas de LLM : provider écho.",
            [StudioStringKeys.MountsSummaryOk] = "{0} montage(s), aucune erreur.",
            [StudioStringKeys.ProfileNewName] = "Nouveau réglage",
        };

        private bool _french;

        public string this[string key] => _french
            ? French.GetValueOrDefault(key, EnglishStudioStrings.Instance[key])
            : EnglishStudioStrings.Instance[key];

        public event EventHandler? CultureChanged;

        public void SwitchToFrench()
        {
            _french = true;
            CultureChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SwitchToEnglish()
        {
            _french = false;
            CultureChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Fact]
    public void ConfigTab_ReEmitsItsSummary_When_TheCultureChanges()
    {
        var strings = new SwitchableStrings();
        var tab = new ConfigTabViewModel(new StudioServices
        {
            SettingsStore = new FakeAppSettingsStore(),
            Directories = new FakeDirectoryProbe(),
            Strings = strings,
        });

        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        strings.SwitchToFrench();

        Assert.Contains(nameof(ConfigTabViewModel.ValidationSummary), raised);
    }

    [Fact]
    public void MountsEditor_TranslatesItsSummary_When_TheCultureChanges()
    {
        var strings = new SwitchableStrings();
        var editor = new MountsEditorViewModel(
            new FakeDirectoryProbe(), requireAtLeastOne: false, strings: strings);

        Assert.Equal("0 mount(s), no error.", editor.Summary);

        strings.SwitchToFrench();

        Assert.Equal("0 montage(s), aucune erreur.", editor.Summary);
    }

    [Fact]
    public void MountRow_TranslatesItsRightsLabel_When_TheCultureChanges()
    {
        var strings = new SwitchableStrings();
        var editor = new MountsEditorViewModel(
            new FakeDirectoryProbe(), requireAtLeastOne: false, strings: strings);
        var row = editor.AddMount();

        Assert.Equal("Read only", row.RightsLabel);

        var raised = new List<string>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        strings.SwitchToFrench();

        // The editor cascades the refresh to its transient rows.
        Assert.Contains(nameof(MountEditorViewModel.RightsLabel), raised);
        Assert.Contains(nameof(MountEditorViewModel.RightsChoices), raised);
        Assert.Equal("Lecture seule", row.RightsLabel);
    }

    [Fact]
    public void ProfileEditor_OpensWithTheCultureLabels_When_FrenchIsActive()
    {
        // The provider catalogue and the seeded name go through the culture port: a French
        // Studio proposes the French new-profile name over French provider rows, not English ones.
        var strings = new SwitchableStrings();
        strings.SwitchToFrench();
        var document = AppSettingsDocument.CreateEmpty();
        var llm = new LlmSectionViewModel(() => document, () => { });
        var profiles = new ModelProfilesViewModel(null, llm, strings, new FakeLlmEndpointProbe());

        profiles.NewProfileCommand.Execute(null);

        Assert.NotNull(profiles.Editor);
        Assert.Equal("Nouveau réglage", profiles.Editor!.Name);
        Assert.Contains(profiles.Editor.Providers, p => p.Title == "Aucun / hors ligne");
    }

    private static (ModelProfilesViewModel Profiles, AppSettingsDocument Document) ModelSettings(IStudioStrings strings)
    {
        var document = AppSettingsDocument.CreateEmpty();
        var llm = new LlmSectionViewModel(() => document, () => { });
        var profiles = new ModelProfilesViewModel(
            new InMemoryModelProfileStore(), llm, strings, new FakeLlmEndpointProbe(), new FakeApiKeyStore());
        return (profiles, document);
    }

    /// <summary>
    /// STUDIO-54, decision 1: the editor recognised a setting's card by its title, in the language
    /// of the moment. A setting created in French on « Autre compatible OpenAI » opened in English
    /// without a card — no key block, no test line —, and saved so, it lost the variable holding its
    /// key, in the store and in its <c>Llm:Profiles</c> entry: its runs, Studio's included, ran
    /// without a key. A setting now keeps its card's name, and the screen titles it in the language.
    /// </summary>
    [Fact]
    public void A_compatible_openai_setting_created_in_french_keeps_its_card_and_its_key_variable_in_english()
    {
        var strings = new SwitchableStrings();
        strings.SwitchToFrench();
        var (profiles, document) = ModelSettings(strings);
        profiles.NewProfileCommand.Execute(null);
        var editor = profiles.Editor!;
        editor.SelectedProvider = editor.Providers.Single(p => p.Title == "Autre compatible OpenAI");
        editor.Name = "Boîte";
        editor.BaseUrl = "https://llm.example.com/v1";
        editor.Model = "qwen3";
        editor.SaveCommand.Execute(null);
        Assert.Equal("Autre compatible OpenAI", profiles.Profiles.Single().Provider);

        var row = profiles.Profiles.Single();
        var raised = new List<string>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
        strings.SwitchToEnglish();

        Assert.Equal("Other OpenAI-compatible", row.Provider);
        Assert.Equal("Other OpenAI-compatible · qwen3", row.Summary);
        Assert.Contains(nameof(ModelProfileItemViewModel.Provider), raised);
        Assert.Contains(nameof(ModelProfileItemViewModel.Summary), raised);

        profiles.BeginEdit(profiles.Set.Find("Boîte")!);
        var reopened = profiles.Editor!;
        Assert.Equal(LlmPresets.Custom, reopened.SelectedProvider?.Name);
        Assert.True(reopened.RequiresApiKey);
        Assert.True(reopened.ShowTestRow);
        Assert.Equal(LlmPresets.CustomApiKeyEnv, reopened.ApiKeyEnvName);

        reopened.Name = "Boîte noire";
        reopened.SaveCommand.Execute(null);

        var renamed = profiles.Set.Find("Boîte noire")!;
        Assert.Equal(LlmPresets.Custom, renamed.Provider);
        Assert.Equal(LlmPresets.CustomApiKeyEnv, renamed.KeyEnvName);
        Assert.Equal(LlmPresets.CustomApiKeyEnv, document.Llm.Profiles.Get("boite-noire")?.ApiKeyEnvVar);
        Assert.Equal("https://llm.example.com/v1", document.Llm.Profiles.Get("boite-noire")?.BaseUrl);

        strings.SwitchToFrench();
        Assert.Equal("Autre compatible OpenAI", profiles.Profiles.Single().Provider);
    }

    [Fact]
    public void A_no_model_setting_created_in_french_keeps_its_card_in_english()
    {
        var strings = new SwitchableStrings();
        strings.SwitchToFrench();
        var (profiles, _) = ModelSettings(strings);
        profiles.NewProfileCommand.Execute(null);
        var editor = profiles.Editor!;
        editor.SelectedProvider = editor.Providers.Single(p => p.Title == "Aucun / hors ligne");
        editor.Name = "Écho";
        editor.SaveCommand.Execute(null);
        Assert.Equal("Aucun / hors ligne", profiles.Profiles.Single().Provider);

        strings.SwitchToEnglish();

        Assert.Equal("None / offline", profiles.Profiles.Single().Provider);
        profiles.BeginEdit(profiles.Set.Find("Écho")!);
        var reopened = profiles.Editor!;
        Assert.Equal(LlmPresets.None, reopened.SelectedProvider?.Name);
        Assert.True(reopened.IsNone);
        Assert.False(reopened.ShowTestRow);

        reopened.Name = "Écho muet";
        reopened.SaveCommand.Execute(null);

        Assert.Equal(LlmPresets.None, profiles.Set.Find("Écho muet")?.Provider);
        strings.SwitchToFrench();
        Assert.Equal("Aucun / hors ligne", profiles.Profiles.Single().Provider);
    }

    [Fact]
    public void LaunchTab_ReEmitsItsStatusLines_When_TheCultureChanges()
    {
        var strings = new SwitchableStrings();
        var tab = new LaunchTabViewModel(new LaunchTabDependencies
        {
            TargetProbe = new FakeTargetProbe(),
            Directories = new FakeDirectoryProbe(),
            HistoryStore = new FakeLaunchHistoryStore(),
            SettingsStore = new FakeAppSettingsStore(),
            Strings = strings,
        });

        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        strings.SwitchToFrench();

        Assert.Contains(nameof(LaunchTabViewModel.BinaryStatus), raised);
        Assert.Contains(nameof(LaunchTabViewModel.ValidationSummary), raised);
    }
}
