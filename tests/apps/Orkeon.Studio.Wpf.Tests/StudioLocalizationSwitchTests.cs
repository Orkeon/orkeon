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
            // STUDIO-56: what the open editor and the tab's lines say again.
            [StudioStringKeys.ProviderCustomShortDescription] = "tout point d'accès : tapez l'URL et le modèle",
            [StudioStringKeys.ProfileThinkingProviderDefault] = "Défaut du fournisseur",
            [StudioStringKeys.ProfileThinkingOn] = "Activée",
            [StudioStringKeys.ProfileThinkingOff] = "Désactivée",
            [StudioStringKeys.ProfileKeyStatusSet] = "clé mémorisée",
            [StudioStringKeys.ProfileKeyStatusMissing] = "aucune clé détectée",
            [StudioStringKeys.ProfileKeyTitleService] = "La clé d'API du service",
            [StudioStringKeys.ProfileKeyMissingTest] = "Clé d'API manquante — mémorisez-la d'abord",
            [StudioStringKeys.ProfileTimeoutHint] = "Vide : le délai du moteur, 30 s.",
            [StudioStringKeys.ProbeReachable] = "Point d'accès joignable — {0} modèle(s).",
            [StudioStringKeys.ProfileFileUnreadable] = "Le fichier des profils de modèle n'a pas pu être lu — {0}.",
            [StudioStringKeys.ProfileKeyPersistFailed] = "La clé est en place pour cette session, mais Windows ne l'a pas gardée : {0}",
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

    // STUDIO-56, decision 2: the open editor and the tab's lines follow the language ──────────

    private static (ModelProfilesViewModel Profiles, AppSettingsDocument Document, FakeApiKeyStore Keys, FakeLlmEndpointProbe Probe)
        ModelSettingsWithSeams(IStudioStrings strings, IModelProfileStore? store = null)
    {
        var document = AppSettingsDocument.CreateEmpty();
        var llm = new LlmSectionViewModel(() => document, () => { });
        var keys = new FakeApiKeyStore();
        var probe = new FakeLlmEndpointProbe();
        var profiles = new ModelProfilesViewModel(store ?? new InMemoryModelProfileStore(), llm, strings, probe, keys);
        return (profiles, document, keys, probe);
    }

    private static ModelProfileEditorViewModel OpenCompatibleEditor(ModelProfilesViewModel profiles)
    {
        profiles.NewProfileCommand.Execute(null);
        var editor = profiles.Editor!;
        editor.SelectedProvider = editor.Providers.Single(p => p.Name == LlmPresets.Custom);
        editor.Name = "Boîte";
        editor.BaseUrl = "https://llm.example.com/v1";
        editor.Model = "qwen3";
        editor.TimeoutText = "240";
        editor.SelectedThinking = editor.ThinkingChoices.Single(choice => choice.Value == false);
        return editor;
    }

    [Fact]
    public void The_open_editor_speaks_the_new_language_and_keeps_what_was_typed()
    {
        var strings = new SwitchableStrings();
        var (profiles, document, _, _) = ModelSettingsWithSeams(strings);
        var editor = OpenCompatibleEditor(profiles);
        var before = editor.SelectedProvider;
        var raised = new List<string>();
        editor.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        strings.SwitchToFrench();

        Assert.Equal("Autre compatible OpenAI", editor.SelectedProvider!.Title);
        Assert.Equal("tout point d'accès : tapez l'URL et le modèle", editor.SelectedProvider.Description);
        Assert.NotSame(before, editor.SelectedProvider);
        Assert.Same(editor.Providers.Single(p => p.Name == LlmPresets.Custom), editor.SelectedProvider);
        Assert.Contains(editor.OtherProviders, p => ReferenceEquals(p, editor.SelectedProvider));
        Assert.Equal("Boîte", editor.Name);
        Assert.Equal("https://llm.example.com/v1", editor.BaseUrl);
        Assert.Equal("qwen3", editor.Model);
        Assert.Equal("240", editor.TimeoutText);
        Assert.Equal("Désactivée", editor.SelectedThinking.Label);
        Assert.Equal(["Défaut du fournisseur", "Activée", "Désactivée"], editor.ThinkingChoices.Select(choice => choice.Label));
        Assert.Equal("La clé d'API du service", editor.KeyBlockTitle);
        Assert.Equal("aucune clé détectée", editor.KeyStatusText);
        foreach (var property in new[]
                 {
                     nameof(editor.Providers), nameof(editor.LocalProviders), nameof(editor.CloudProviders),
                     nameof(editor.OtherProviders), nameof(editor.SelectedProvider), nameof(editor.ThinkingChoices),
                     nameof(editor.SelectedThinking), nameof(editor.KeyStatusText), nameof(editor.KeyBlockTitle),
                     nameof(editor.KeyConsoleUrl), nameof(editor.HostIdText), nameof(editor.HostKeyHint),
                     nameof(editor.TimeoutHint), nameof(editor.ThinkingHint), nameof(editor.MaxTokensHint),
                 })
        {
            Assert.Contains(property, raised);
        }

        // The list before the choice: a picker shown a choice its list does not hold drops it.
        Assert.True(raised.IndexOf(nameof(editor.Providers)) < raised.IndexOf(nameof(editor.SelectedProvider)));
        Assert.True(raised.IndexOf(nameof(editor.ThinkingChoices)) < raised.IndexOf(nameof(editor.SelectedThinking)));

        editor.SaveCommand.Execute(null);

        var saved = profiles.Set.Find("Boîte")!;
        Assert.Equal(LlmPresets.Custom, saved.Provider);
        Assert.Equal("https://llm.example.com/v1", saved.BaseUrl);
        Assert.Equal("qwen3", saved.Model);
        Assert.Equal(240, saved.TimeoutSeconds);
        Assert.False(saved.ThinkingEnabled);
        Assert.Equal(LlmPresets.CustomApiKeyEnv, saved.KeyEnvName);
        var entry = document.Llm.Profiles.Get("boite");
        Assert.Equal("https://llm.example.com/v1", entry?.BaseUrl);
        Assert.Equal(LlmPresets.CustomApiKeyEnv, entry?.ApiKeyEnvVar);
    }

    [Fact]
    public async Task A_test_verdict_keeps_its_result_and_says_it_again_in_the_new_language()
    {
        var strings = new SwitchableStrings();
        var (profiles, _, keys, _) = ModelSettingsWithSeams(strings);
        var editor = OpenCompatibleEditor(profiles);
        keys.Stage(LlmPresets.CustomApiKeyEnv, "sk-test");
        await editor.TestConnectionAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Endpoint reachable — 2 model(s).", editor.ConnectionTestResult);
        var raised = new List<string>();
        editor.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        strings.SwitchToFrench();

        Assert.Contains(nameof(editor.ConnectionTestResult), raised);
        Assert.Equal("Point d'accès joignable — 2 modèle(s).", editor.ConnectionTestResult);
    }

    [Fact]
    public async Task A_missing_key_verdict_says_it_again_in_the_new_language()
    {
        var strings = new SwitchableStrings();
        var (profiles, _, _, probe) = ModelSettingsWithSeams(strings);
        var editor = OpenCompatibleEditor(profiles);
        await editor.TestConnectionAsync(TestContext.Current.CancellationToken);
        Assert.Equal("API key missing — remember it first", editor.ConnectionTestResult);

        strings.SwitchToFrench();

        Assert.Empty(probe.Requests);
        Assert.Equal("Clé d'API manquante — mémorisez-la d'abord", editor.ConnectionTestResult);
    }

    [Fact]
    public async Task A_key_not_kept_says_its_cause_again_in_the_new_language()
    {
        var strings = new SwitchableStrings();
        var (profiles, _, keys, _) = ModelSettingsWithSeams(strings);
        var editor = OpenCompatibleEditor(profiles);
        keys.PersistFailure = new UnauthorizedAccessException("registre refusé");
        editor.ApiKeyInput = "sk-test";
        await editor.StoreKeyAsync();

        strings.SwitchToFrench();

        Assert.Equal("La clé est en place pour cette session, mais Windows ne l'a pas gardée : registre refusé", editor.KeyStoreError);
    }

    [Fact]
    public void A_closed_editor_hears_no_language_switch()
    {
        var strings = new SwitchableStrings();
        var (profiles, _, _, _) = ModelSettingsWithSeams(strings);
        var editor = OpenCompatibleEditor(profiles);
        editor.CancelCommand.Execute(null);
        var raised = new List<string>();
        editor.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        strings.SwitchToFrench();

        Assert.Empty(raised);
    }

    [Fact]
    public void The_key_rows_of_the_models_and_tools_tabs_say_their_status_again()
    {
        var strings = new SwitchableStrings();
        var (profiles, _, _, _) = ModelSettingsWithSeams(strings);
        var editor = OpenCompatibleEditor(profiles);
        editor.SaveCommand.Execute(null);
        var modelRow = Assert.Single(profiles.Secrets);
        var tools = new ToolsSettingsViewModel(new FakeApiKeyStore(), strings);
        var toolRow = tools.Secrets[0];
        var raised = new List<string>();
        modelRow.PropertyChanged += (_, e) => raised.Add("model:" + e.PropertyName);
        toolRow.PropertyChanged += (_, e) => raised.Add("tool:" + e.PropertyName);

        strings.SwitchToFrench();

        Assert.Contains("model:" + nameof(SecretRowViewModel.StatusText), raised);
        Assert.Contains("tool:" + nameof(SecretRowViewModel.StatusText), raised);
        Assert.Equal("aucune clé détectée", modelRow.StatusText);
    }

    [Fact]
    public async Task A_key_row_not_kept_says_its_cause_again()
    {
        var strings = new SwitchableStrings();
        var keys = new FakeApiKeyStore { PersistFailure = new UnauthorizedAccessException("registre refusé") };
        var tools = new ToolsSettingsViewModel(keys, strings);
        var row = tools.Secrets[0];
        row.KeyInput = "sk-tool";
        await row.StoreAsync();

        strings.SwitchToFrench();

        Assert.Equal("La clé est en place pour cette session, mais Windows ne l'a pas gardée : registre refusé", row.StoreError);
    }

    [Fact]
    public async Task The_unreadable_file_line_says_its_cause_again()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "orkeon-l10n-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(root, ModelProfileFileStore.FileName);
            await File.WriteAllTextAsync(path, "{ not json", TestContext.Current.CancellationToken);
            var strings = new SwitchableStrings();
            var (profiles, _, _, _) = ModelSettingsWithSeams(strings, new ModelProfileFileStore(path));
            await profiles.InitializeAsync(TestContext.Current.CancellationToken);
            Assert.StartsWith("The model profiles file could not be read", profiles.LoadError, StringComparison.Ordinal);
            var raised = new List<string>();
            profiles.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

            strings.SwitchToFrench();

            Assert.Contains(nameof(ModelProfilesViewModel.LoadError), raised);
            Assert.StartsWith("Le fichier des profils de modèle n'a pas pu être lu — ", profiles.LoadError, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
