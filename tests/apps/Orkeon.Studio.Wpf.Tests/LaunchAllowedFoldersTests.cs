using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Launch;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// A team reaching outside « Réglages › Dossiers autorisés » does not start.
/// <para>
/// The settings are the list of what this machine allows. Letting such a team run meant
/// discovering the refusal from a run that failed halfway, with the reason buried in a log —
/// the screen now says which folder before anything is spawned, and names the way out.
/// </para>
/// </summary>
public sealed class LaunchAllowedFoldersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-allowed-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string NewTeam(string slug, params string[] mounts)
    {
        var team = Path.Combine(_root, slug);
        Directory.CreateDirectory(team);
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata { Name = slug, Mounts = mounts });
        return team;
    }

    private static LaunchTabViewModel Launcher(string teamPath, params string[] declared) =>
        Launcher(teamPath, () => declared);

    private static LaunchTabViewModel Launcher(string teamPath, Func<IReadOnlyList<string>> declared)
    {
        // A promoted team resolves as a multi-file crew directory: the folder plus its agents/.
        var probe = new FakeTargetProbe()
            .WithDirectory(teamPath)
            .WithDirectory(Path.Combine(teamPath, "agents"));

        var tab = new LaunchTabViewModel(
            new OrkeonProcessRunner(new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            probe,
            new FakeDirectoryProbe(teamPath),
            picker: null,
            new FakeLaunchHistoryStore(),
            new FakeAppSettingsStore(),
            declaredMounts: declared);

        tab.Target.Select(teamPath);
        return tab;
    }

    [Fact]
    public void A_team_using_only_declared_folders_launches()
    {
        var team = NewTeam("veille", "/data/docs:/docs:ro");
        var tab = Launcher(team, "/data/docs:/docs:ro");

        Assert.False(tab.IsBlockedByUndeclaredFolders);
        Assert.Empty(tab.UndeclaredTeamFolders);
        Assert.True(tab.RunCommand.CanExecute(null));
    }

    [Fact]
    public void A_team_using_a_folder_the_settings_do_not_allow_is_refused_and_told_why()
    {
        var team = NewTeam("veille", "/data/docs:/docs:ro", "/elsewhere/archives:/archives:ro");
        var tab = Launcher(team, "/data/docs:/docs:ro");

        Assert.True(tab.IsBlockedByUndeclaredFolders);
        Assert.Equal(["/archives"], tab.UndeclaredTeamFolders);
        Assert.False(tab.RunCommand.CanExecute(null));
        // The dry run is a run too: it spawns the same engine against the same mounts.
        Assert.False(tab.ValidateCommand.CanExecute(null));

        // The message names the folder — and, ADR-008, not the folder on this machine.
        Assert.Contains("/archives", tab.UndeclaredFoldersMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("/elsewhere", tab.UndeclaredFoldersMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void The_teams_own_output_and_input_never_refuse_it()
    {
        // They are created inside the team at adoption and are never declared; counting them
        // would make every adopted team unlaunchable.
        var team = NewTeam(
            "veille",
            $"{Path.Combine(_root, "veille", "output")}:/output:rw",
            $"{Path.Combine(_root, "veille", "input")}:/workspace:ro");
        var tab = Launcher(team);

        Assert.False(tab.IsBlockedByUndeclaredFolders);
        Assert.True(tab.RunCommand.CanExecute(null));
    }

    [Fact]
    public void Declaring_the_folder_in_the_settings_unblocks_the_very_same_launcher()
    {
        var team = NewTeam("veille", "/elsewhere/archives:/archives:ro");
        var declared = new List<string>();
        var tab = Launcher(team, () => declared);

        Assert.True(tab.IsBlockedByUndeclaredFolders);

        // The user went to the settings and declared it — the list is read live, not snapshotted.
        declared.Add("/elsewhere/archives:/archives:ro");
        tab.RefreshTeamDescription();

        Assert.False(tab.IsBlockedByUndeclaredFolders);
        Assert.True(tab.RunCommand.CanExecute(null));
    }

    [Fact]
    public void The_refusal_offers_the_settings_rather_than_leaving_the_user_to_find_them()
    {
        var team = NewTeam("veille", "/elsewhere/archives:/archives:ro");
        var tab = Launcher(team);
        var asked = 0;
        tab.OpenAllowedFoldersRequested += (_, _) => asked++;

        tab.OpenAllowedFoldersCommand.Execute(null);

        Assert.Equal(1, asked);
    }
}
