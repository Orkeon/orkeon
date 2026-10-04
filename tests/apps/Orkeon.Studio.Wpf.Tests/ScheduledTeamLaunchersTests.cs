using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Services;
using Orkeon.Studio.Wpf.ViewModels.Shell;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// STUDIO-50, on the window over a test disk: a team's launchers — what the operating system's
/// scheduled run executes — are written again whenever the team's setting or its folders change in
/// Studio. A setting the team names created or removed moves the run's model — a renamed one
/// carries the team along (STUDIO-52, TeamSettingFollowsTests) —; a save
/// of the team's folders moves its mounts. Never a key.
/// </summary>
public sealed class ScheduledTeamLaunchersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-scheduled-team-" + Guid.NewGuid().ToString("N"));
    private readonly string _teamsRoot;
    private readonly string _team;
    private readonly string _settingsPath;

    private static readonly ModelProfile DeepSeek = new()
    {
        Name = "DeepSeek", Provider = "DeepSeek", BaseUrl = "https://api.deepseek.com",
        Model = "deepseek-v4-flash", KeyEnvName = "DEEPSEEK_API_KEY", TimeoutSeconds = 600,
    };

    private static readonly ModelProfile Zai = new()
    {
        Name = "Z.AI", Provider = "Z.AI", BaseUrl = "https://api.z.ai/api/paas/v4",
        Model = "glm-5.2", KeyEnvName = "ZAI_API_KEY",
    };

    public ScheduledTeamLaunchersTests()
    {
        _teamsRoot = Path.Combine(_root, "teams");
        _team = Path.Combine(_teamsRoot, "veille-docs");
        Directory.CreateDirectory(Path.Combine(_team, "crew"));
        File.WriteAllText(Path.Combine(_team, "crew", "config.yaml"), "name: veille\n");
        // What `forge promote` wrote, before any setting of the team existed.
        File.WriteAllText(Path.Combine(_team, TeamLaunchers.PosixLauncherName), "#!/usr/bin/env sh\nexec orkeon run \"$DIR/crew\"\n");
        File.WriteAllText(Path.Combine(_team, TeamLaunchers.WindowsLauncherName), "@echo off\r\norkeon run \"%~dp0crew\"\r\n");
        TeamCatalog.SaveMetadata(_team, new StudioTeamMetadata
        {
            Name = "Veille docs",
            Profile = "DeepSeek",
            Mounts = ["./output:/output:rw"],
        });
        _settingsPath = Path.Combine(_root, "config", "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, "{}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private MainWindowViewModel Window() =>
        new(new StudioServices
            {
                SettingsStore = new FakeAppSettingsStore(),
                Directories = new FakeDirectoryProbe(),
                TargetProbe = new FakeTargetProbe(),
                Picker = new FakePathPicker(),
                ProcessRunner = new OrkeonProcessRunner(
                    new FakeProcessLauncher(),
                    new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
                HistoryStore = new FakeLaunchHistoryStore(),
                KeyStore = new FakeApiKeyStore(),
                LlmProbe = new FakeLlmEndpointProbe(),
            },
            globalPathOverride: _settingsPath,
            forgeWorkspace: Path.Combine(_root, "forge"),
            teamsRoot: _teamsRoot);

    private string Posix() => File.ReadAllText(Path.Combine(_team, TeamLaunchers.PosixLauncherName));

    private string Windows() => File.ReadAllText(Path.Combine(_team, TeamLaunchers.WindowsLauncherName));

    [Fact]
    public void The_setting_a_team_names_moves_its_launchers_when_it_is_created_or_removed()
    {
        var window = Window();
        Assert.DoesNotContain("--llm-profile", Posix(), StringComparison.Ordinal);

        // The team names « DeepSeek »: created, the setting is the host profile its run takes.
        window.Settings.Profiles.CommitEdit(DeepSeek, previousName: null);
        Assert.Contains("--llm-profile='deepseek'", Posix(), StringComparison.Ordinal);
        Assert.Contains($"--settings='{_settingsPath}'", Posix(), StringComparison.Ordinal);
        Assert.Contains("--llm-profile=\"deepseek\"", Windows(), StringComparison.Ordinal);

        // Another setting changes nothing for this team.
        window.Settings.Profiles.CommitEdit(Zai, previousName: null);
        Assert.Contains("--llm-profile='deepseek'", Posix(), StringComparison.Ordinal);

        // Removed, the setting is gone: the team runs on the default, as Studio then launches it.
        window.Settings.Profiles.Delete("DeepSeek");
        Assert.DoesNotContain("--llm-profile", Posix(), StringComparison.Ordinal);
        Assert.DoesNotContain("--llm-profile", Windows(), StringComparison.Ordinal);

        foreach (var launcher in new[] { Posix(), Windows() })
        {
            Assert.DoesNotContain("API_KEY", launcher, StringComparison.Ordinal);
            Assert.DoesNotContain("ApiKey", launcher, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Saving_a_teams_folders_writes_its_launchers_again()
    {
        var window = Window();
        window.Settings.Profiles.CommitEdit(DeepSeek, previousName: null);
        var team = TeamCatalog.Describe(_team);

        window.TeamMounts.Open(team.Path, team.Name, team.Mounts);
        window.TeamMounts.AddMount(new MountDefinition
        {
            PhysicalPath = Path.Combine(_team, "rapports"),
            VirtualPath = "/rapports",
            Rights = MountRights.ReadWrite,
        });
        window.TeamMounts.SaveCommand.Execute(null);

        var posix = Posix();
        Assert.Contains("--mount \"\\\"$DIR/output\\\":/output:rw\" \"\\\"$DIR/rapports\\\":/rapports:rw\"", posix, StringComparison.Ordinal);
        Assert.Contains("--llm-profile='deepseek'", posix, StringComparison.Ordinal);
        Assert.Contains("^\"\\\"%~dp0rapports\\\":/rapports:rw^\"", Windows(), StringComparison.Ordinal);
    }
}
