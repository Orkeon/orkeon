using Orkeon.Studio.Core.FileSystem;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Targets;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Run.Launcher;
using Orkeon.Studio.Run.Tests.Doubles;

namespace Orkeon.Studio.Run.Tests;

/// <summary>
/// Builds a launcher whose every collaborator is a hand-written double: a declared file
/// tree, a scripted process launcher, an in-memory history. No test in this project ever
/// needs a real <c>orkeon</c> binary on the machine.
/// </summary>
internal sealed class LauncherFixture
{
    /// <summary>Directory the fake install layout puts the CLI in.</summary>
    public static string InstallDirectory { get; } = Path.Combine("/", "opt", "orkeon");

    /// <summary>Path the binary locator will resolve to.</summary>
    public static string BinaryPath { get; } = Path.Combine(InstallDirectory, "orkeon");

    public LauncherFixture() => Executables.BaseDirectory = InstallDirectory;

    /// <summary>The declared file tree target detection sees.</summary>
    public FakeTargetProbe Targets { get; } = new();

    /// <summary>The declared directories mount validation sees.</summary>
    public FakeDirectoryProbe Directories { get; } = new();

    /// <summary>The declared install layout binary resolution sees.</summary>
    public FakeExecutableProbe Executables { get; } = new();

    /// <summary>The scripted child process.</summary>
    public FakeProcessLauncher Processes { get; } = new();

    /// <summary>The in-memory recent-launch list.</summary>
    public FakeLaunchHistoryStore History { get; } = new();

    /// <summary>The declared appsettings files.</summary>
    public FakeAppSettingsReader Settings { get; } = new();

    /// <summary>The teams root the launcher is built with (STUDIO-61); null, the default, stamps nothing and finds no team file.</summary>
    public TeamsRootResolution? TeamsRoot { get; private set; }

    /// <summary>Declares the co-installed CLI as present.</summary>
    public LauncherFixture WithInstalledCli()
    {
        Executables.WithFile(BinaryPath);
        return this;
    }

    /// <summary>Gives the launcher a teams root, as the variable would (STUDIO-61).</summary>
    public LauncherFixture WithTeamsRoot(string teamsRoot)
    {
        TeamsRoot = new TeamsRootResolution(teamsRoot, TeamsRootSource.Environment, null, null);
        return this;
    }

    /// <summary>
    /// Declares a workshop team right under the teams root — its folder, a crew file inside it —
    /// and the workshop's settings file for it, <c>settings/&lt;slug&gt;/appsettings.json</c> beside
    /// the root (STUDIO-62). Returns the crew file and the settings file.
    /// </summary>
    public (string Crew, string SettingsFile) WithWorkshopTeam(string slug, string settingsJson = "{}")
    {
        var root = TeamsRoot?.Path ?? throw new InvalidOperationException("Call WithTeamsRoot first.");
        var team = Path.Combine(root, slug);
        var crew = Path.Combine(team, "crew.yaml");
        var settingsFile = WorkshopLayout.SettingsFileOf(root, slug);

        Targets.WithDirectories(root, team).WithFiles(crew, settingsFile);
        Settings.WithFile(settingsFile, settingsJson);
        return (crew, settingsFile);
    }

    /// <summary>Declares a YAML crew file and returns its path.</summary>
    public string WithYamlCrew(string path = "/crews/demo/crew.yaml")
    {
        Targets.WithDirectories(Path.GetDirectoryName(path)!).WithFiles(path);
        return path;
    }

    /// <summary>Declares a directory holding the conventional script entry point.</summary>
    public string WithScriptDirectory(string directory = "/crews/script")
    {
        Targets.WithDirectories(directory)
            .WithFiles(Path.Combine(directory, RunTargetDetector.CrewScriptFileName));
        return directory;
    }

    /// <summary>Builds the launcher over the declared doubles.</summary>
    public RunLauncherViewModel Build() => new(
        new RunTargetDetector(Targets),
        new OrkeonProcessRunner(Processes, new OrkeonBinaryLocator(Executables)),
        History,
        new MountValidator(Directories),
        Settings,
        TeamsRoot,
        Targets);
}
