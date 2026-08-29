using System.ComponentModel;
using Orkeon.Studio.Core.Configuration;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Config;
using Orkeon.Studio.Wpf.ViewModels.Launch;
using Orkeon.Studio.Wpf.ViewModels.Mounts;

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
    }

    [Fact]
    public void ConfigTab_ReEmitsItsSummary_When_TheCultureChanges()
    {
        var strings = new SwitchableStrings();
        var tab = new ConfigTabViewModel(
            new FakeAppSettingsStore(),
            new FakeDirectoryProbe(),
            strings: strings);

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
        var llm = new LlmSectionViewModel(() => document, () => { }, new FakeLlmEndpointProbe(), strings: strings);
        var profiles = new ModelProfilesViewModel(null, llm, strings, new FakeLlmEndpointProbe());

        profiles.NewProfileCommand.Execute(null);

        Assert.NotNull(profiles.Editor);
        Assert.Equal("Nouveau réglage", profiles.Editor!.Name);
        Assert.Contains(profiles.Editor.Providers, p => p.Title == "Aucun / hors ligne");
    }

    [Fact]
    public void LaunchTab_ReEmitsItsStatusLines_When_TheCultureChanges()
    {
        var strings = new SwitchableStrings();
        var tab = new LaunchTabViewModel(
            targetProbe: new FakeTargetProbe(),
            directories: new FakeDirectoryProbe(),
            historyStore: new FakeLaunchHistoryStore(),
            settingsStore: new FakeAppSettingsStore(),
            strings: strings);

        var raised = new List<string>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        strings.SwitchToFrench();

        Assert.Contains(nameof(LaunchTabViewModel.BinaryStatus), raised);
        Assert.Contains(nameof(LaunchTabViewModel.ValidationSummary), raised);
    }
}
