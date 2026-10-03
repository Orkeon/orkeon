namespace Orkeon.Studio.Core.Forge;

/// <summary>
/// The <c>code</c>s of the engine's <c>warning</c> events (<see cref="ForgeEventKinds.Warning"/>) a
/// screen gives a sentence of its own — re-declared like the event kinds, Studio.Core never
/// referencing the CLI; each suite pins the same literal. A code not listed here is said as the
/// engine wrote it.
/// </summary>
public static class ForgeWarningCodes
{
    /// <summary>The promotion is written, but the session folder could not take the team folder's name (STUDIO-26, D-05).</summary>
    public const string SessionNotRenamed = "FORGE-SESSION-NOT-RENAMED";

    /// <summary>
    /// The team's <c>run.cmd</c> launches nothing: its <c>orkeon</c> command is longer than a
    /// <c>cmd</c> command holds (STUDIO-51). Studio writes the launchers again at adoption, and says
    /// what its own say.
    /// </summary>
    public const string LauncherTooLong = "FORGE-LAUNCHER-TOO-LONG";
}
