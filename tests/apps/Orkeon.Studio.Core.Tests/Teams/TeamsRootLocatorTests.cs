using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Core.Tests.Teams;

/// <summary>
/// The teams root is chosen, not fixed (STUDIO-61): the variable beats the option, which beats
/// the preference, which beats the default — and only an absolute path counts. No disk: the
/// variable is read through the seam, and the folder never has to exist.
/// </summary>
public sealed class TeamsRootLocatorTests
{
    private static readonly string Workshop = Path.Combine(Path.GetTempPath(), "orkeon-ws", "teams");
    private static readonly string Shortcut = Path.Combine(Path.GetTempPath(), "orkeon-shortcut", "teams");
    private static readonly string Chosen = Path.Combine(Path.GetTempPath(), "orkeon-chosen", "teams");

    private static Func<string, string?> Variable(string? value) =>
        name => name == TeamsRootLocator.EnvironmentVariable ? value : null;

    [Fact]
    public void The_variable_beats_the_option_which_beats_the_preference_which_beats_the_default()
    {
        var all = TeamsRootLocator.Resolve(Variable(Workshop), Shortcut, Chosen);
        Assert.Equal(Workshop, all.Path);
        Assert.Equal(TeamsRootSource.Environment, all.Source);

        var noVariable = TeamsRootLocator.Resolve(Variable(null), Shortcut, Chosen);
        Assert.Equal(Shortcut, noVariable.Path);
        Assert.Equal(TeamsRootSource.Argument, noVariable.Source);

        var preferenceOnly = TeamsRootLocator.Resolve(Variable(null), null, Chosen);
        Assert.Equal(Chosen, preferenceOnly.Path);
        Assert.Equal(TeamsRootSource.Preference, preferenceOnly.Source);

        var nothing = TeamsRootLocator.Resolve(Variable(null));
        Assert.Equal(TeamCatalog.DefaultRoot(), nothing.Path);
        Assert.Equal(TeamsRootSource.Default, nothing.Source);
        Assert.Null(nothing.IgnoredValue);
        Assert.Null(nothing.IgnoredReason);
    }

    [Fact]
    public void A_relative_variable_is_ignored_with_its_reason_and_the_option_wins()
    {
        var resolution = TeamsRootLocator.Resolve(Variable("my-workshop/teams"), Shortcut);

        Assert.Equal(Shortcut, resolution.Path);
        Assert.Equal(TeamsRootSource.Argument, resolution.Source);
        Assert.Equal("my-workshop/teams", resolution.IgnoredValue);
        Assert.Contains(TeamsRootLocator.EnvironmentVariable, resolution.IgnoredReason, StringComparison.Ordinal);
        Assert.Contains("absolute", resolution.IgnoredReason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_relative_option_and_preference_leave_the_default_and_the_first_ignored_value_is_named()
    {
        var resolution = TeamsRootLocator.Resolve(Variable(null), "teams", "also-relative");

        Assert.Equal(TeamCatalog.DefaultRoot(), resolution.Path);
        Assert.Equal(TeamsRootSource.Default, resolution.Source);
        Assert.Equal("teams", resolution.IgnoredValue);
        Assert.Contains("--teams-root", resolution.IgnoredReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"\ws\teams")]
    [InlineData("C:teams")]
    public void A_path_that_resolves_against_the_current_drive_or_folder_is_relative_and_ignored(string value)
    {
        // Under Windows both are "rooted" for Path.IsPathRooted, yet neither names one folder: the
        // first reads the current drive, the second the current folder of C:. Under Linux they are
        // plain relative names. The same answer everywhere: not an absolute path.
        var resolution = TeamsRootLocator.Resolve(Variable(value), Shortcut);

        Assert.Equal(Shortcut, resolution.Path);
        Assert.Equal(TeamsRootSource.Argument, resolution.Source);
        Assert.Equal(value, resolution.IgnoredValue);
    }

    [Fact]
    public void A_blank_value_is_absent_not_ignored()
    {
        var resolution = TeamsRootLocator.Resolve(Variable("   "), "", Chosen);

        Assert.Equal(Chosen, resolution.Path);
        Assert.Equal(TeamsRootSource.Preference, resolution.Source);
        Assert.Null(resolution.IgnoredValue);
        Assert.Null(resolution.IgnoredReason);
    }

    [Fact]
    public void The_ending_separator_is_trimmed_and_the_folder_need_not_exist()
    {
        var withSeparator = Workshop + Path.DirectorySeparatorChar;

        var resolution = TeamsRootLocator.Resolve(Variable(withSeparator));

        Assert.Equal(Workshop, resolution.Path);
        Assert.False(Directory.Exists(Workshop));
    }

    [Fact]
    public void Nothing_set_reads_the_process_environment_and_falls_back_to_the_catalog_default()
    {
        // The real environment of the test process does not carry the variable: the default applies.
        Assert.Null(Environment.GetEnvironmentVariable(TeamsRootLocator.EnvironmentVariable));

        var resolution = TeamsRootLocator.Resolve();

        Assert.Equal(TeamCatalog.DefaultRoot(), resolution.Path);
        Assert.Equal(TeamsRootSource.Default, resolution.Source);
    }

    [Fact]
    public void The_variable_name_follows_the_studio_convention()
    {
        Assert.Equal("ORKEON_STUDIO_TEAMS_ROOT", TeamsRootLocator.EnvironmentVariable);
    }
}
