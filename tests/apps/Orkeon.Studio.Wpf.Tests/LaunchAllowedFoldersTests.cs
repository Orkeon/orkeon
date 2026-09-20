using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Launch;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// A team reaching outside the Settings > Allowed folders list does not start.
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

        var tab = new LaunchTabViewModel(new LaunchTabDependencies
        {
            ProcessRunner = new OrkeonProcessRunner(
                new FakeProcessLauncher(), new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            TargetProbe = probe,
            Directories = new FakeDirectoryProbe(teamPath),
            HistoryStore = new FakeLaunchHistoryStore(),
            SettingsStore = new FakeAppSettingsStore(),
            DeclaredMounts = declared,
        });

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

    /// <summary>
    /// STUDIO-15 D-05. The chooser copies the settings entry verbatim into the sidecar, so an
    /// adopted team carries the machine's own defaults: laid as <c>--mount</c> they met their
    /// twin from the settings and the run failed on "Duplicate virtual paths". An entry the
    /// settings already hold is in force without a word from Studio; only the team's own
    /// folders go on the command line.
    /// </summary>
    [Fact]
    public void The_launch_command_never_carries_a_settings_entry_twice()
    {
        var team = NewTeam(
            "veille",
            "/data/docs:/docs:ro",
            $"{Path.Combine(_root, "veille", "output")}:/output:rw");
        var tab = Launcher(team, "/data/docs:/docs:ro", "/data/archives:/archives:ro");

        Assert.Equal(["/data/docs:/docs:ro", "/data/archives:/archives:ro"], tab.Mounts.SettingsMounts);
        var arguments = tab.BuildArguments().ToList();
        var mountIndex = arguments.IndexOf("--mount");
        Assert.True(mountIndex >= 0);
        var mountValues = arguments.Skip(mountIndex + 1).TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal));
        Assert.Equal([$"{Path.Combine(_root, "veille", "output")}:/output:rw"], mountValues);
        Assert.DoesNotContain("/data/docs:/docs:ro", arguments);
    }

    /// <summary>
    /// VFS-90: a settings declaration the team names by id goes on the command line as
    /// <c>--mount-id</c> — no path, the machine's own entry as it stands today, even when the
    /// sidecar's copy is stale.
    /// </summary>
    [Fact]
    public void A_settings_declaration_the_team_names_is_selected_by_id_not_laid_as_a_path()
    {
        var id = Orkeon.Domain.Common.MountId.Create();
        var team = NewTeam("veille", $"{id}|/old/place:/docs:ro", "./output:/output:rw");
        var tab = Launcher(team, $"{id}|/data/docs:/docs:ro", "/data/archives:/archives:ro");

        Assert.False(tab.IsBlockedByUndeclaredFolders);
        Assert.False(tab.IsBlockedByUnknownMountIds);
        var arguments = tab.BuildArguments().ToList();
        var idIndex = arguments.IndexOf("--mount-id");
        Assert.True(idIndex >= 0);
        Assert.Equal(id.ToString(), arguments[idIndex + 1]);
        Assert.DoesNotContain(arguments, a => a.Contains("/old/place", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, a => a.Contains("/data/docs", StringComparison.Ordinal));
        // The declared folder sits outside the team: the flag follows (D-12), by the entry's folder.
        Assert.True(tab.Mounts.AllowExternalMounts);
        // The chips and the meta line read the declaration, not the stale copy.
        Assert.Contains($"{id}|/data/docs:/docs:ro", tab.Mounts.TeamMounts);
    }

    /// <summary>
    /// VFS-90 D-06: a team naming a declaration this machine does not have — an import, or an
    /// entry removed since — is refused before anything is spawned, with the id and the way out.
    /// </summary>
    [Fact]
    public void A_team_naming_a_declaration_missing_here_is_refused_with_the_id()
    {
        var unknown = Orkeon.Domain.Common.MountId.Create();
        var team = NewTeam("veille", $"{unknown}|/home/elsewhere/docs:/docs:ro");
        var tab = Launcher(team, "/data/docs:/docs:ro");

        Assert.True(tab.IsBlockedByUnknownMountIds);
        Assert.Equal([unknown.ToString()], tab.UnknownTeamMountIds);
        Assert.Contains(unknown.ToString(), tab.UnknownMountIdsMessage, StringComparison.Ordinal);
        Assert.False(tab.RunCommand.CanExecute(null));
        Assert.False(tab.ValidateCommand.CanExecute(null));
        Assert.DoesNotContain(tab.BuildArguments(), a => a.Contains("/home/elsewhere", StringComparison.Ordinal));
    }

    /// <summary>
    /// The effective table on a shared root: the entry the team selects reads as kept, the
    /// other as not mounted — what the runner will do, said before it does it.
    /// </summary>
    [Fact]
    public void The_effective_table_says_which_entry_of_a_shared_root_the_team_keeps()
    {
        var a = Orkeon.Domain.Common.MountId.Create();
        var b = Orkeon.Domain.Common.MountId.Create();
        var team = NewTeam("veille", $"{b}|/srv/b:/output:rw");
        var tab = Launcher(team, $"{a}|/srv/a:/output:rw", $"{b}|/srv/b:/output:rw");

        var rows = tab.Mounts.EffectiveMounts.Where(r => r.Origin == MountOrigin.Settings).ToList();
        Assert.Equal(EffectiveMountSelection.NotSelected, rows[0].Selection);
        Assert.Equal(EffectiveMountSelection.SelectedById, rows[1].Selection);
        Assert.Contains("selected by id among 2", rows[1].OriginDisplay, StringComparison.Ordinal);
        Assert.Contains("not mounted for this run", rows[0].OriginDisplay, StringComparison.Ordinal);
        Assert.Contains("--mount-id", tab.BuildArguments());
    }

    /// <summary>
    /// D-04. A team may associate a declared folder under a name the settings spend on another
    /// folder: that is a replacement the runner performs by root, and the table says so.
    /// </summary>
    [Fact]
    public void The_effective_mounts_table_names_the_settings_entry_a_team_folder_replaces()
    {
        var team = NewTeam("veille", "/data/docs:/archives:ro");
        var tab = Launcher(team, "/data/docs:/docs:ro", "/data/archives:/archives:ro");

        // Laid: the settings hold the folder under another name, so this is the team's own mount.
        Assert.Contains("/data/docs:/archives:ro", tab.BuildArguments());

        var rows = tab.Mounts.EffectiveMounts;
        var replaced = Assert.Single(rows, r => r.OverridesSettings);
        Assert.Equal("/data/docs:/archives:ro", replaced.Value);
        Assert.Equal("/data/archives:/archives:ro", replaced.ReplacedSettingsMount);
        Assert.Contains("/data/archives:/archives:ro", replaced.OriginDisplay, StringComparison.Ordinal);
        // One row per root: the declared /docs entry stays, as the settings' own.
        Assert.Single(rows, r => r.Value == "/data/docs:/docs:ro" && r.Origin == MountOrigin.Settings);
        Assert.Equal(1, tab.Mounts.OverriddenCount);
    }

    /// <summary>
    /// STUDIO-14 D-12. The engine whitelists a <c>--mount</c> base path for the file tools only
    /// under <c>--allow-external-mounts</c>, and a team reading a declared folder elsewhere on
    /// the disk was refused file by file unless the user found the expert checkbox. The flag
    /// now follows the sidecar: a team folder outside the team turns it on.
    /// </summary>
    [Fact]
    public void A_team_folder_outside_the_team_turns_allow_external_mounts_on()
    {
        // A declared folder, under the team's own name for it: laid as --mount, and outside the team.
        var team = NewTeam("veille", "/data/docs:/sources:ro");
        var tab = Launcher(team, "/data/docs:/docs:ro");

        Assert.True(tab.Mounts.AllowExternalMounts);
        var arguments = tab.BuildArguments();
        Assert.Contains("/data/docs:/sources:ro", arguments);
        Assert.Contains("--allow-external-mounts", arguments);
        // Not a refusal: the folder is declared, the flag is what lets the tools reach it.
        Assert.False(tab.IsBlockedByUndeclaredFolders);
    }

    /// <summary>
    /// The other half of D-12: a team-relative folder (<c>./output</c>) resolves under the team,
    /// which is the launch's working directory, so the tools reach it without any flag. The
    /// launch carries the resolved absolute path, never the <c>./</c> spelling — the runtime
    /// would resolve that against its own cwd and nothing else.
    /// </summary>
    [Fact]
    public void A_relative_in_team_folder_launches_absolute_and_needs_no_flag()
    {
        var team = NewTeam("veille", "./output:/output:rw", "./input:/workspace:ro");
        var tab = Launcher(team);

        Assert.False(tab.Mounts.AllowExternalMounts);
        Assert.False(tab.IsBlockedByUndeclaredFolders);

        var arguments = tab.BuildArguments().ToList();
        Assert.DoesNotContain("--allow-external-mounts", arguments);
        var mountIndex = arguments.IndexOf("--mount");
        Assert.True(mountIndex >= 0);
        var mountValues = arguments.Skip(mountIndex + 1).TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            [$"{Path.Combine(team, "output")}:/output:rw", $"{Path.Combine(team, "input")}:/workspace:ro"],
            mountValues);
        Assert.DoesNotContain(mountValues, m => m.StartsWith("./", StringComparison.Ordinal));
    }

    /// <summary>
    /// Replay is refused for the same reason a fresh run is.
    /// <para>
    /// A replay deliberately re-runs a RECORDED argument list, so that it cannot drift from what
    /// actually ran. But "Settings &gt; Allowed folders" is machine policy and it changes: without
    /// this gate, revoking a folder left every past launch of that team one click away from
    /// running against it anyway, from the one button whose whole promise is that it changes
    /// nothing. The history panel's one-click path goes through the same command, and
    /// <c>AsyncRelayCommand.ExecuteAsync</c> checks <c>CanExecute</c> before doing anything, so
    /// gating the command gates both ways in.
    /// </para>
    /// </summary>
    [Fact]
    public void A_past_launch_cannot_be_replayed_against_a_folder_since_revoked()
    {
        var team = NewTeam("veille", "/elsewhere/archives:/archives:ro");
        var declared = new List<string> { "/elsewhere/archives:/archives:ro" };
        var tab = Launcher(team, () => declared);

        Assert.False(tab.IsBlockedByUndeclaredFolders);
        Assert.True(tab.ReplayCommand.CanExecute(null));

        // The folder is taken off the machine's allow-list after that launch was recorded.
        declared.Clear();
        tab.RefreshTeamDescription();

        Assert.True(tab.IsBlockedByUndeclaredFolders);
        Assert.False(tab.ReplayCommand.CanExecute(null));
    }
}
