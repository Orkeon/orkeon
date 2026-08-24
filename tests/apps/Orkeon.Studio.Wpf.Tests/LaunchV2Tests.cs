using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Wpf.Tests.Doubles;
using Orkeon.Studio.Wpf.ViewModels.Launch;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// Remediation v2 on the Exécuter screen (F-05): « Validation à blanc d'abord » really
/// runs a --validate pass before the run and stops on a failed one; the team meta line
/// speaks the sidecar's mounts.
/// </summary>
public sealed class LaunchV2Tests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-launchv2-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static (LaunchTabViewModel Tab, FakeProcessLauncher Launcher) Build(FakeTargetProbe probe)
    {
        var launcher = new FakeProcessLauncher();
        var tab = new LaunchTabViewModel(
            new OrkeonProcessRunner(launcher, new OrkeonBinaryLocator(FakeExecutableProbe.WithOrkeonInstalled())),
            probe,
            new FakeDirectoryProbe(),
            picker: null,
            new FakeLaunchHistoryStore(),
            new FakeAppSettingsStore());
        return (tab, launcher);
    }

    [Fact]
    public async Task Validate_first_runs_the_dry_pass_then_the_real_one()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var (tab, launcher) = Build(probe);
        tab.Target.Select("/crews/team.yaml");
        tab.Options.ValidateFirst = true;

        await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, launcher.Requests.Count);
        Assert.Contains("--validate", launcher.Requests[0].Arguments);
        Assert.DoesNotContain("--validate", launcher.Requests[1].Arguments);
    }

    [Fact]
    public async Task A_failed_dry_pass_stops_the_launch()
    {
        var probe = new FakeTargetProbe().WithFile("/crews/team.yaml");
        var (tab, launcher) = Build(probe);
        tab.Target.Select("/crews/team.yaml");
        tab.Options.ValidateFirst = true;
        launcher.ExitCode = 1;

        var result = await tab.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(launcher.Requests);   // the real run never started
        Assert.Contains("--validate", launcher.Requests[0].Arguments);
        Assert.Equal(1, result!.ExitCode);
    }

    [Fact]
    public void The_meta_line_speaks_the_team_mounts()
    {
        var team = Path.Combine(_root, "veille");
        Directory.CreateDirectory(team);
        TeamCatalog.SaveMetadata(team, new StudioTeamMetadata
        {
            Name = "Veille",
            Mounts = ["C:/docs:/docs:ro", "C:/out:/output:rw"],
        });

        var probe = new FakeTargetProbe().WithDirectory(team);
        var (tab, _) = Build(probe);
        tab.Target.Select(team);

        Assert.Contains("reads /docs", tab.TeamMetaLine, StringComparison.Ordinal);
        Assert.Contains("writes to /output", tab.TeamMetaLine, StringComparison.Ordinal);
        // And the sidecar mounts ride the launch as --mount values.
        Assert.Equal(["C:/docs:/docs:ro", "C:/out:/output:rw"], tab.Mounts.TeamMounts);
    }
}
