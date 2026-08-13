using Orkeon.Studio.Core.Targets;

namespace Orkeon.Studio.Run.Launcher;

/// <summary>
/// The framework prerequisite a multi-file crew directory carries (SPEC §6, "P1").
/// <para>
/// <c>orkeon run</c> dispatches on the file extension only, so a directory target needs a
/// CLI that has shipped directory dispatch. Studio states the version it needs — the raw
/// "unsupported file" error the CLI would produce tells the user nothing actionable.
/// </para>
/// </summary>
internal static class DirectoryRunSupport
{
    /// <summary>First CLI version expected to dispatch <c>orkeon run &lt;directory&gt;</c>.</summary>
    public const string MinimumCliVersion = "0.10.0";

    /// <summary>The version requirement in front of Core's own explanation of the shape.</summary>
    public static string Notice { get; } =
        $"Requires Orkeon >= {MinimumCliVersion}. {RunTargetRequirements.DirectoryRunNotice}";

    /// <summary>The notice for <paramref name="target"/>, or null when it needs nothing special.</summary>
    public static string? NoticeFor(RunTarget? target) =>
        target is { RequiresDirectoryRunSupport: true } ? Notice : null;
}
