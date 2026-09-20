using System.Globalization;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Core.Tools;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// The Tools tab (STUDIO-21): the keys the tools need, one row each on the same store as the
/// profile keys, and the catalogue of what a run exposes, by family, with what each tool
/// needs said next to it. Nothing here is read from the settings file — the keys live in the
/// user environment, the catalogue is declared — so the tab has no dirty state of its own; the
/// one file-backed card of the tab, the shell allow-list, is a section form of the editor.
/// </summary>
public sealed class ToolsSettingsViewModel : ObservableObject
{
    private readonly IStudioStrings _strings;

    /// <summary>Builds the tab over the key store; both default to the real environment and English.</summary>
    public ToolsSettingsViewModel(IApiKeyStore? keyStore = null, IStudioStrings? strings = null)
    {
        _strings = strings ?? EnglishStudioStrings.Instance;
        var store = keyStore ?? new EnvironmentApiKeyStore();

        Secrets = [.. ToolCatalog.Secrets.Select(secret =>
            new SecretRowViewModel(secret.EnvName, secret.UsedBy, store, _strings, secret.ConsoleUrl))];
        Families = [.. ToolCatalog.Families.Select(family => new ToolFamilyViewModel(family, _strings))];

        // The labels are read live; a language switch re-emits every one of them.
        _strings.CultureChanged += (_, _) =>
        {
            foreach (var family in Families)
                family.RefreshLabels();
        };
    }

    /// <summary>The keys the tools need, in the card's order.</summary>
    public IReadOnlyList<SecretRowViewModel> Secrets { get; }

    /// <summary>Whether the tool-keys card shows at all.</summary>
    public bool HasSecrets => Secrets.Count > 0;

    /// <summary>The catalogue, by family.</summary>
    public IReadOnlyList<ToolFamilyViewModel> Families { get; }

    /// <summary>How many tools a run exposes, for the intro line.</summary>
    public int ToolCount => Families.Sum(family => family.Tools.Count);
}

/// <summary>One family of the catalogue: its localized label, its tools, and what some of them need.</summary>
public sealed class ToolFamilyViewModel : ObservableObject
{
    private readonly ToolFamily _family;
    private readonly IStudioStrings _strings;

    internal ToolFamilyViewModel(ToolFamily family, IStudioStrings strings)
    {
        _family = family;
        _strings = strings;
        Tools = [.. family.Tools.Select(tool => new ToolChipViewModel(tool))];
        Requirements = [.. family.Tools
            .Where(tool => tool.Requirement != ToolRequirement.None)
            .Select(tool => new ToolRequirementViewModel(tool, strings))];
    }

    /// <summary>The family key, for tests and the capture catalogue.</summary>
    public string Key => _family.Key;

    /// <summary>The localized family name.</summary>
    public string Label => _strings[_family.Key switch
    {
        ToolCatalog.WebFamily => StudioStringKeys.ToolFamilyWeb,
        ToolCatalog.SearchFamily => StudioStringKeys.ToolFamilySearch,
        ToolCatalog.FilesFamily => StudioStringKeys.ToolFamilyFiles,
        ToolCatalog.DataFamily => StudioStringKeys.ToolFamilyData,
        ToolCatalog.CodeFamily => StudioStringKeys.ToolFamilyCode,
        ToolCatalog.SessionFamily => StudioStringKeys.ToolFamilySession,
        ToolCatalog.EventsFamily => StudioStringKeys.ToolFamilyEvents,
        ToolCatalog.AnalysisFamily => StudioStringKeys.ToolFamilyAnalysis,
        ToolCatalog.CollaborationFamily => StudioStringKeys.ToolFamilyCollaboration,
        _ => StudioStringKeys.ToolFamilyMounts,
    }];

    /// <summary>The tools, as chips.</summary>
    public IReadOnlyList<ToolChipViewModel> Tools { get; }

    /// <summary>The tools that need something, one line each.</summary>
    public IReadOnlyList<ToolRequirementViewModel> Requirements { get; }

    /// <summary>Whether any tool of the family needs something.</summary>
    public bool HasRequirements => Requirements.Count > 0;

    /// <summary>The line a family with nothing to configure ends on.</summary>
    public string QuietLine => string.Format(
        CultureInfo.CurrentCulture, _strings[StudioStringKeys.ToolFamilyQuietPattern], Tools.Count);

    internal void RefreshLabels()
    {
        OnPropertiesChanged(nameof(Label), nameof(QuietLine));
        foreach (var requirement in Requirements)
            requirement.RefreshText();
    }
}

/// <summary>One tool of the catalogue, as the chip shows it.</summary>
public sealed class ToolChipViewModel
{
    internal ToolChipViewModel(ToolInfo tool)
    {
        Name = tool.Name;
        NeedsSomething = tool.Requirement != ToolRequirement.None;
    }

    /// <summary>The registry name, mono, untranslated.</summary>
    public string Name { get; }

    /// <summary>Whether the tool has a line under the family saying what it needs.</summary>
    public bool NeedsSomething { get; }
}

/// <summary>What one tool needs, as a sentence next to its name.</summary>
public sealed class ToolRequirementViewModel : ObservableObject
{
    private readonly ToolInfo _tool;
    private readonly IStudioStrings _strings;

    internal ToolRequirementViewModel(ToolInfo tool, IStudioStrings strings)
    {
        _tool = tool;
        _strings = strings;
    }

    /// <summary>The registry name, mono.</summary>
    public string Name => _tool.Name;

    /// <summary>The sentence, localized, with the variable or the section it points at.</summary>
    public string Text => _tool.Requirement switch
    {
        ToolRequirement.StoredKey => Format(StudioStringKeys.ToolNeedsStoredKey),
        ToolRequirement.OnlyWithStoredKey => Format(StudioStringKeys.ToolOnlyWithStoredKey),
        ToolRequirement.KeyAtCall => _strings[StudioStringKeys.ToolKeyAtCall],
        ToolRequirement.ParametersAtCall => _strings[StudioStringKeys.ToolParametersAtCall],
        ToolRequirement.ExpertSetting => Format(StudioStringKeys.ToolExpertSetting),
        _ => "",
    };

    /// <summary>Whether the line points at a key of the card above.</summary>
    public bool IsKey => _tool.Requirement is ToolRequirement.StoredKey or ToolRequirement.OnlyWithStoredKey;

    internal void RefreshText() => OnPropertyChanged(nameof(Text));

    private string Format(string key) =>
        string.Format(CultureInfo.CurrentCulture, _strings[key], _tool.Argument ?? "");
}
