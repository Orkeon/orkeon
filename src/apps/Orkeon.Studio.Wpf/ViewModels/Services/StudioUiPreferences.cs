namespace Orkeon.Studio.Wpf.ViewModels.Services;

/// <summary>
/// The presentation choices a window opens with — which mode, which language — and where each
/// one is written back when the user changes it.
/// <para>
/// Null everywhere means nobody has chosen: the machine decides the language and nothing is
/// persisted. That is what lets the screenshot campaign toggle mode and language constantly
/// without ever rewriting the operator's stored preferences.
/// </para>
/// </summary>
public sealed record StudioUiPreferences
{
    /// <summary>The mode the window opens in; null falls back to the default one.</summary>
    public string? InitialMode { get; init; }

    /// <summary>Writes a mode the user picked; null keeps the choice for this session only.</summary>
    public Action<string>? PersistMode { get; init; }

    /// <summary>The language the user picked last time; null when nobody ever did.</summary>
    public string? InitialLanguage { get; init; }

    /// <summary>Writes a language the user picked; null keeps the choice for this session only.</summary>
    public Action<string>? PersistLanguage { get; init; }

    /// <summary>Applies a language to the running application.</summary>
    public Action<string>? ApplyLanguage { get; init; }

    /// <summary>The machine's own language, consulted when nobody has chosen one.</summary>
    public string? SystemLanguage { get; init; }
}
