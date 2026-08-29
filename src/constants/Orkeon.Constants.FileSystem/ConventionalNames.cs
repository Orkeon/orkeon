namespace Orkeon.Constants.FileSystem;

/// <summary>
/// File and folder names Orkeon relies on by convention rather than by configuration.
/// <para>
/// Each is written by one component and looked for by another, across projects that cannot
/// reference each other (ADR-009). Nothing validates them: a settings file the tooling writes
/// under one name and the runner looks for under another simply does not load, and the run
/// proceeds on defaults without a word.
/// </para>
/// </summary>
public static class ConventionalNames
{
    /// <summary>
    /// The settings file, wherever it sits: beside a crew, in the per-user configuration
    /// directory, or next to the daemon. Written by <c>orkeon init</c> and by Orkeon Studio,
    /// resolved by every runner.
    /// </summary>
    public const string SettingsFile = "appsettings.json";

    /// <summary>
    /// The state directory a workspace accumulates — forge sessions, the codebase index cache.
    /// Hidden by convention, and never a mount root: it holds Orkeon's own bookkeeping, not the
    /// agents' material.
    /// </summary>
    public const string StateDirectory = ".orkeon";

    /// <summary>
    /// The flat YAML crew layout: a directory holding these three files is a crew, as opposed to
    /// the multi-file layout with its <c>agents/</c> and <c>tasks/</c> sub-folders.
    /// <para>
    /// Exposed as the SET, not as three names. The detector and the layout classifier live in
    /// projects that cannot reference each other and each used to carry its own copy of the
    /// array; a fourth marker added to one and not the other would have gone unnoticed, which is
    /// the failure mode a set makes impossible rather than merely unlikely.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> FlatCrewLayoutFiles { get; } = ["crew.yaml", "agents.yaml", "tasks.yaml"];
}
