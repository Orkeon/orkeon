using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Core.Teams;

/// <summary>Where the teams root in force came from (STUDIO-61).</summary>
public enum TeamsRootSource
{
    /// <summary>The <see cref="TeamsRootLocator.EnvironmentVariable"/> variable of the process.</summary>
    Environment,

    /// <summary>The <c>--teams-root</c> startup option of the WPF application.</summary>
    Argument,

    /// <summary>The preference Settings › Studio wrote into <c>ui-preferences.json</c> (WPF only).</summary>
    Preference,

    /// <summary><see cref="TeamCatalog.DefaultRoot"/>: nothing was set, or nothing usable.</summary>
    Default,
}

/// <summary>
/// The teams root Studio reads and writes under, and where it came from (STUDIO-61). Resolved
/// once at startup: a change of the variable, the option or the preference applies at the
/// next start, and every screen keeps the root it was built on.
/// </summary>
/// <param name="Path">The root in force, absolute, its ending separator trimmed; it need not exist.</param>
/// <param name="Source">Which of the four sources <paramref name="Path"/> came from.</param>
/// <param name="IgnoredValue">
/// The first value, in precedence order, that was set but could not be used — a relative path —,
/// so the settings card can say so; null when every value set was usable.
/// </param>
/// <param name="IgnoredReason">Why <paramref name="IgnoredValue"/> was ignored; null with it.</param>
public sealed record TeamsRootResolution(
    string Path,
    TeamsRootSource Source,
    string? IgnoredValue,
    string? IgnoredReason);

/// <summary>
/// Chooses the teams root (STUDIO-61): the <see cref="EnvironmentVariable"/> variable, then the
/// <c>--teams-root</c> option, then the preference Settings › Studio keeps, then
/// <see cref="TeamCatalog.DefaultRoot"/>. A value must be an absolute path to count: a relative
/// or blank one is skipped and the next source applies, the first relative value kept with its
/// reason for the settings card. The folder does not have to exist.
/// <para>
/// Nothing here reaches <c>appsettings.json</c>: that file is the one <c>orkeon run</c> reads,
/// and the run refuses at start any <c>Orkeon:*</c> section it does not know (GAP-54), so a
/// Studio preference written there would fail every run of the machine. The variable alone is
/// inherited by the child <c>orkeon</c> process, where it becomes the root-level key
/// <c>STUDIO_TEAMS_ROOT</c> — outside the containers the run judges, read by nothing.
/// </para>
/// </summary>
[SuppressVfsCompliance(
    "EXCEPTION-BOOTSTRAP: Studio is a host application; the teams root is user-owned storage on " +
    "the physical disk, chosen before any VFS mount exists.")]
public static class TeamsRootLocator
{
    // EXCEPTION-BOOTSTRAP: the root is read from the process environment and composed as a physical
    // path before any VFS mount exists; nothing on disk is touched here.

    /// <summary>The variable naming the teams root, read by the WPF application and the run TUI alike.</summary>
    public const string EnvironmentVariable = "ORKEON_STUDIO_TEAMS_ROOT";

    /// <summary>
    /// Resolves the teams root in force: the variable, then <paramref name="argument"/>, then
    /// <paramref name="preference"/>, then <see cref="TeamCatalog.DefaultRoot"/>.
    /// </summary>
    /// <param name="environment">
    /// Reads an environment variable by name; defaults to the process environment. The seam the
    /// tests pose the variable through, without touching the process.
    /// </param>
    /// <param name="argument">The value of <c>--teams-root</c>; null when the option was not given.</param>
    /// <param name="preference">The value Settings › Studio kept; null when nobody chose one.</param>
    public static TeamsRootResolution Resolve(
        Func<string, string?>? environment = null,
        string? argument = null,
        string? preference = null)
    {
        var read = environment ?? System.Environment.GetEnvironmentVariable;
        string? ignoredValue = null;
        string? ignoredReason = null;

        foreach (var (value, source) in Candidates(read(EnvironmentVariable), argument, preference))
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var trimmed = value.Trim();
            if (!System.IO.Path.IsPathRooted(trimmed))
            {
                // The first unusable value is the one the card names: it is the one that would have
                // won, and the one its author expects to see in force.
                ignoredValue ??= trimmed;
                ignoredReason ??= $"{Describe(source)} is not an absolute path.";
                continue;
            }

            return new TeamsRootResolution(
                System.IO.Path.TrimEndingDirectorySeparator(trimmed), source, ignoredValue, ignoredReason);
        }

        return new TeamsRootResolution(TeamCatalog.DefaultRoot(), TeamsRootSource.Default, ignoredValue, ignoredReason);
    }

    private static IEnumerable<(string? Value, TeamsRootSource Source)> Candidates(
        string? variable, string? argument, string? preference)
    {
        yield return (variable, TeamsRootSource.Environment);
        yield return (argument, TeamsRootSource.Argument);
        yield return (preference, TeamsRootSource.Preference);
    }

    private static string Describe(TeamsRootSource source) => source switch
    {
        TeamsRootSource.Environment => EnvironmentVariable,
        TeamsRootSource.Argument => "--teams-root",
        _ => "the teams folder chosen in Settings › Studio",
    };
}
