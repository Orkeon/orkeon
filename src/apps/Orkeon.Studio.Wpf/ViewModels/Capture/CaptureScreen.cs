namespace Orkeon.Studio.Wpf.ViewModels.Capture;

/// <summary>
/// Where a stop stands. A closed set of destinations rather than a control name, so the catalogue
/// says WHICH screen and the WPF side alone knows which radio button that is.
/// </summary>
internal enum CaptureScreen
{
    /// <summary>Créer une équipe.</summary>
    Create,

    /// <summary>Mes équipes.</summary>
    Teams,

    /// <summary>Importer.</summary>
    Import,

    /// <summary>Tester — expert only.</summary>
    Test,

    /// <summary>Exécuter.</summary>
    Run,

    /// <summary>Historique.</summary>
    History,

    /// <summary>Réglages, on the model tab.</summary>
    SettingsModel,

    /// <summary>Réglages, on the authorized-folders tab.</summary>
    SettingsFolders,

    /// <summary>Réglages, on the limits tab — expert only.</summary>
    SettingsLimits,

    /// <summary>Réglages, on the raw-JSON tab — expert only.</summary>
    SettingsJson,

    /// <summary>Diagnostic.</summary>
    Diagnostic,
}

/// <summary>Which mode passes a stop belongs to.</summary>
[Flags]
internal enum CaptureModes
{
    /// <summary>The novice pass.</summary>
    Novice = 1,

    /// <summary>The expert pass.</summary>
    Expert = 2,

    /// <summary>Both passes — the default, because most screens differ between them.</summary>
    Both = Novice | Expert,

    /// <summary>Once, in the novice pass: the shot does not depend on the mode at all.</summary>
    Either = 4,
}

/// <summary>Which grouping a stop belongs to; the middle of its file name.</summary>
internal enum CaptureCategory
{
    /// <summary>Splash, guided tour, About, the language menu.</summary>
    Onboarding,

    /// <summary>The create-a-team wizard.</summary>
    Wizard,

    /// <summary>Mes équipes.</summary>
    Teams,

    /// <summary>Importer.</summary>
    Import,

    /// <summary>The expert trial screen.</summary>
    TestScreen,

    /// <summary>Exécuter.</summary>
    Run,

    /// <summary>Historique.</summary>
    History,

    /// <summary>Réglages and its four tabs.</summary>
    Settings,

    /// <summary>Diagnostic.</summary>
    Diagnostic,

    /// <summary>The scrim modals.</summary>
    Modal,

    /// <summary>The assistant conversation.</summary>
    Assistant,
}

/// <summary>The lowercase spelling each category carries in a file name.</summary>
internal static class CaptureCategories
{
    /// <summary>
    /// The slug of a category. Written as literals rather than lower-cased at run time: the value
    /// lands in a file name, so it must be culture-invariant by construction.
    /// </summary>
    public static string Slug(CaptureCategory category) => category switch
    {
        CaptureCategory.Onboarding => "accueil",
        CaptureCategory.Wizard => "creer",
        CaptureCategory.Teams => "equipes",
        CaptureCategory.Import => "importer",
        CaptureCategory.TestScreen => "tester",
        CaptureCategory.Run => "executer",
        CaptureCategory.History => "historique",
        CaptureCategory.Settings => "reglages",
        CaptureCategory.Diagnostic => "diagnostic",
        CaptureCategory.Modal => "modale",
        _ => "autre",
    };
}

/// <summary>Which screens exist at all in a given mode.</summary>
internal static class CaptureReachability
{
    /// <summary>True when <paramref name="screen"/> can be reached in <paramref name="mode"/>.</summary>
    public static bool IsReachableIn(CaptureScreen screen, CaptureModes mode) =>
        screen is CaptureScreen.Test or CaptureScreen.SettingsLimits or CaptureScreen.SettingsJson
            ? mode == CaptureModes.Expert
            : true;
}
