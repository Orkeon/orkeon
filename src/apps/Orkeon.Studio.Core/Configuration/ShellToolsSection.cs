namespace Orkeon.Studio.Core.Configuration;

/// <summary>
/// Typed view over <c>Orkeon:Tools:Shell</c> (STUDIO-21), the one tool section of the file
/// the runner reads: the allow-list of the <c>shell_command</c> tool. Read by
/// <c>CodeToolExtensions</c> in <c>Orkeon.Tools.Code</c>, which does not reference the
/// constants satellite, so the path is spelt here and pinned by the tool catalogue.
/// <para>
/// The two lists have a third state the runtime treats as a trap: an EMPTY array replaces
/// the defaults with nothing and blocks every command. This view never writes one — an
/// empty list removes the key, which the runtime reads as "not set".
/// </para>
/// </summary>
public sealed class ShellToolsSection
{
    /// <summary>Configuration path of the section.</summary>
    public const string SectionPath = "Orkeon:Tools:Shell";

    private readonly AppSettingsDocument _document;

    internal ShellToolsSection(AppSettingsDocument document) => _document = document;

    /// <summary>True when the section is present with at least one key.</summary>
    public bool Exists => _document.SectionExists(SectionPath);

    /// <summary>
    /// Whether interpreters (shells, scripting runtimes) may run through the tool — the
    /// switch that makes <c>shell_command</c> the equivalent of remote code execution.
    /// </summary>
    public bool? AllowInterpreters
    {
        get => _document.GetBoolean($"{SectionPath}:AllowInterpreters");
        set => _document.SetBoolean($"{SectionPath}:AllowInterpreters", value);
    }

    /// <summary>The full replacement of the default allow-list; null when the defaults apply.</summary>
    public IReadOnlyList<string>? AllowedCommands
    {
        get => ListOrNull($"{SectionPath}:AllowedCommands");
        set => SetList($"{SectionPath}:AllowedCommands", value);
    }

    /// <summary>Commands added on top of the defaults; null when none.</summary>
    public IReadOnlyList<string>? ExtraAllowedCommands
    {
        get => ListOrNull($"{SectionPath}:ExtraAllowedCommands");
        set => SetList($"{SectionPath}:ExtraAllowedCommands", value);
    }

    /// <summary>Removes the whole section.</summary>
    public void Remove() => _document.Remove(SectionPath);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1168:Empty arrays and collections should be returned instead of null",
        Justification = "null is a third state here, not the absence of a value: an absent AllowedCommands means " +
                        "\"the built-in defaults apply\" (ReplacesDefaults reads it so), while an empty list would mean " +
                        "\"a custom allow-list with zero entries\" and block every command. ShellToolsSectionTests pins it.")]
    private IReadOnlyList<string>? ListOrNull(string path) =>
        _document.ContainsPath(path) ? _document.GetStringArray(path) : null;

    private void SetList(string path, IReadOnlyList<string>? values)
    {
        var kept = values?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList();
        if (kept is null || kept.Count == 0)
            _document.Remove(path);
        else
            _document.SetStringArray(path, kept);
    }
}
