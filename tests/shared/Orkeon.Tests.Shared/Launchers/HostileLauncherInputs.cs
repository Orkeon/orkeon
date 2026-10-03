using Xunit;

namespace Orkeon.Tests.Shared.Launchers;

/// <summary>
/// The values, launcher folders and team folders every suite that writes a team's launcher holds
/// it against (STUDIO-51 § 3): the composer itself, <c>forge promote</c> and Orkeon Studio. Each
/// one broke a launcher, or would have: a <c>%</c> <c>cmd</c> expands, a <c>"</c> that takes it
/// out of its quotes, an operator after it, a trailing backslash the runner reads as an escape, a
/// leading dash the grammar reads as an option, a byte the console's code page reads otherwise, a
/// line break that ends the command.
/// </summary>
public static class HostileLauncherInputs
{
    /// <summary>Longer than a <c>cmd</c> command holds once expanded (8 191 characters).</summary>
    public const int OverLimitLength = 9000;

    /// <summary>Every value, as a sample input, a settings path or a mount could carry it.</summary>
    public static IReadOnlyList<string> Values { get; } =
    [
        "10%",
        "10% à 20%",
        "%PATH%",
        "%~dp0",
        "dit \"oui\"",
        "a \"b & c\" d",
        "x\" & calc & \"",
        @"C:\dossier\",
        "a\\\"b",
        @"\\serveur\partage\",
        "& | < > ^ ( )",
        "!bang!",
        "- puce",
        "--x",
        "  \t",
        "Économie",
        "日本語",
        "🚀",
        "",
        "ligne 1\nligne 2",
        "ligne 1\r\nligne 2",
        "avant\u001Aaprès",
    ];

    /// <summary><see cref="Values"/>, for a theory.</summary>
    public static TheoryData<string> ValueData => [.. Values];

    /// <summary>The non-blank <see cref="Values"/>: what a single-value option is written with.</summary>
    public static TheoryData<string> NonBlankValueData => [.. Values.Where(value => !string.IsNullOrWhiteSpace(value))];

    /// <summary>What <c>%~dp0</c> expands to: the launcher's folder, its trailing backslash included.</summary>
    public static IReadOnlyList<string> LauncherDirectories { get; } =
    [
        @"C:\Users\Zoé\R&D 100%\équipe\",
        @"C:\a^b (x)!\c;d,e=f\",
        @"D:\données 日本\",
    ];

    /// <summary><see cref="LauncherDirectories"/>, for a theory.</summary>
    public static TheoryData<string> LauncherDirectoryData => [.. LauncherDirectories];

    /// <summary>Folders inside a team, anchored to the launcher's folder.</summary>
    public static IReadOnlyList<string> TeamFolders { get; } = ["R&D docs", "données", "100%"];

    /// <summary>
    /// What <c>run.cmd</c> hands over for <paramref name="value"/>: a line break and every control
    /// character but the tab become a space — a <c>cmd</c> command is one line, and Ctrl-Z reads as
    /// its end. <c>run.sh</c> hands the value over as it is.
    /// </summary>
    public static string CmdHandsOver(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new string([.. value.Select(c => char.IsControl(c) && c != '\t' ? ' ' : c)]);
    }

    /// <summary>A value of <paramref name="length"/> characters, all of them ordinary.</summary>
    public static string Long(int length = OverLimitLength) =>
        string.Concat(Enumerable.Repeat("abcdefghij", (length / 10) + 1))[..length];
}
