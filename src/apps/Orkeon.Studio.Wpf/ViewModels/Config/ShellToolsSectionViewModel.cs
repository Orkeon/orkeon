using Orkeon.Studio.Core.Configuration;

namespace Orkeon.Studio.Wpf.ViewModels.Config;

/// <summary>
/// The <c>Orkeon:Tools:Shell</c> form of the Tools tab (STUDIO-21, expert): the switch that
/// lets interpreters through, and the two command lists as text, one command per line. The
/// lists never reach the file as an empty array — the runtime reads one as "block every
/// command" — and the switch is written only when it is on, since off is the default.
/// </summary>
public sealed class ShellToolsSectionViewModel : DocumentSectionViewModel
{
    /// <summary>Binds the form to the <c>Orkeon:Tools:Shell</c> section of the document.</summary>
    public ShellToolsSectionViewModel(Func<AppSettingsDocument> document, Action onChanged)
        : base(document, onChanged)
    {
    }

    private ShellToolsSection Section => Document.ShellTools;

    /// <inheritdoc />
    public override bool Exists => Section.Exists;

    /// <summary>Whether interpreters may run; off (the default) removes the key.</summary>
    public bool AllowInterpreters
    {
        get => Section.AllowInterpreters ?? false;
        set => SetValue(AllowInterpreters, value, v => Section.AllowInterpreters = v ? true : null);
    }

    /// <summary>Commands added to the defaults, one per line.</summary>
    public string ExtraAllowedCommandsText
    {
        get => Join(Section.ExtraAllowedCommands);
        set => SetValue(ExtraAllowedCommandsText, value ?? "", v => Section.ExtraAllowedCommands = Split(v));
    }

    /// <summary>The full replacement of the default list, one per line; empty means the defaults apply.</summary>
    public string AllowedCommandsText
    {
        get => Join(Section.AllowedCommands);
        set => SetValue(AllowedCommandsText, value ?? "", v => Section.AllowedCommands = Split(v));
    }

    /// <summary>Whether the file replaces the default list rather than adding to it.</summary>
    public bool ReplacesDefaults => Section.AllowedCommands is not null;

    /// <inheritdoc />
    protected override void OnSectionChanged() => OnPropertiesChanged(nameof(Exists), nameof(ReplacesDefaults));

    private static string Join(IReadOnlyList<string>? values) =>
        values is null ? "" : string.Join(Environment.NewLine, values);

    private static IReadOnlyList<string> Split(string text) =>
        [.. text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
