using System.Globalization;
using System.IO;
using System.Text.Json;
using Orkeon.Compliance.Vfs;
using Orkeon.Studio.Core.History;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Profiles;
using Orkeon.Studio.Core.Teams;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

/// <summary>
/// Lays a <see cref="CaptureWorldPlan"/> down on disk and hands back the world that reads it.
/// <para>
/// Everything is written through the production writers wherever one exists —
/// <see cref="TeamCatalog.SaveMetadata"/>, <see cref="LaunchHistory.ToJson"/>,
/// <see cref="ModelProfileFileStore"/> — so the seeded tree cannot drift from the shape the app
/// actually reads. Only the forge session artefacts are hand-written, because Studio.Core reads
/// those files and never produces them.
/// </para>
/// </summary>
[SuppressVfsCompliance(
    "UI-layer capture harness seeding its own throwaway scenario under the operator's temp " +
    "directory — Studio's own state, not framework I/O; same exception class as " +
    "ScreenCaptureRunner and ForgeSessionHydrator.")]
internal static class CaptureWorldWriter
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Writes <paramref name="plan"/> under <paramref name="root"/>.</summary>
    public static async Task<CaptureWorld> WriteAsync(CaptureWorldPlan plan, string root)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var config = Path.Combine(root, "config");
        var data = Path.Combine(root, "data");
        var teams = Path.Combine(root, "teams");
        var bin = Path.Combine(root, "bin");

        foreach (var directory in new[] { config, data, teams, bin })
            Directory.CreateDirectory(directory);

        // Declared folders exist for real, and the ones the plan deliberately omits do not: an
        // unreadable mount row has to be genuinely unreadable, not simulated.
        foreach (var folder in plan.DataFolders)
            Directory.CreateDirectory(Path.Combine(data, folder));

        var settingsPath = Path.Combine(config, "appsettings.json");
        await File.WriteAllTextAsync(settingsPath, Settings(plan, data));

        foreach (var team in plan.Teams)
            WriteTeam(team, teams, data);

        foreach (var session in plan.Sessions)
            await WriteSessionAsync(session, config, teams);

        var historyPath = Path.Combine(config, "studio-history.json");
        await File.WriteAllTextAsync(historyPath, History(plan, teams).ToJson());

        var profilePath = Path.Combine(config, "studio-model-profiles.json");
        var profileStore = new ModelProfileFileStore(profilePath);
        await profileStore.SaveAsync(plan.Profiles);

        var binaryDirectory = plan.CliInstalled ? bin : null;
        if (binaryDirectory is not null)
        {
            // A file the locator can find. It is never executed — the scripted launcher answers
            // every request — which is what makes an empty stub safe.
            await File.WriteAllTextAsync(Path.Combine(bin, "orkeon"), "");
            await File.WriteAllTextAsync(Path.Combine(bin, "orkeon.exe"), "");
        }

        var cli = new ScriptedOrkeonCli()
            .Answer("doctor", 0, plan.DoctorJson)
            .Answer("--version", 0, plan.VersionLine)
            .Answer("run", 0, [.. plan.RunStream])
            .Answer("forge", 0, [.. plan.ForgeStream]);

        return new CaptureWorld
        {
            Plan = plan,
            Root = root,
            SettingsPath = settingsPath,
            TeamsRoot = teams,
            ForgeWorkspace = config,
            DataDirectory = data,
            Cli = cli,
            Locator = new OrkeonBinaryLocator(new ScriptedExecutableProbe(binaryDirectory)),
            HistoryStore = new LaunchHistoryFileStore(historyPath),
            ProfileStore = profileStore,
        };
    }

    /// <summary>
    /// The history, with each run pointed at the team folder that was actually written. Built
    /// through the production entry factories so the on-disk shape cannot drift from the reader.
    /// </summary>
    private static LaunchHistory History(CaptureWorldPlan plan, string teamsRoot)
    {
        var entries = plan.History.Select(run =>
            LaunchHistoryEntry.Starting(
                    Path.Combine(teamsRoot, run.TeamSlug),
                    ["run", Path.Combine(teamsRoot, run.TeamSlug)],
                    startedAt: run.StartedAt)
                with
                {
                    ExitCode = run.ExitCode,
                    Outcome = run.Outcome,
                    DurationSeconds = run.DurationSeconds,
                    Tokens = run.Tokens,
                    CacheHitTokens = run.CacheHitTokens,
                    CacheMissTokens = run.Tokens is { } total && run.CacheHitTokens is { } hit
                        ? Math.Max(0, total - hit)
                        : null,
                });

        return new LaunchHistory { Entries = [.. entries] };
    }

    /// <summary>The settings file, with every declared folder resolved to a real path.</summary>
    private static string Settings(CaptureWorldPlan plan, string data)
    {
        var mounts = plan.DeclaredMounts.Select(mount => MountString(mount, data)).ToArray();
        var sections = new List<string>
        {
            $"  \"Llm\": {plan.LlmJson}",
            "  \"Orkeon\": { \"FileSystem\": { \"Mounts\": "
                + JsonSerializer.Serialize(mounts)
                + " } }",
        };
        sections.AddRange(plan.ExtraSections.Select(section => $"  \"{section.Key}\": {section.Value}"));

        return "{\n" + string.Join(",\n", sections) + "\n}\n";
    }

    private static void WriteTeam(TeamSeed team, string teamsRoot, string data)
    {
        var directory = Path.Combine(teamsRoot, team.Slug);
        var crew = Path.Combine(directory, "crew");
        Directory.CreateDirectory(Path.Combine(crew, "agents"));
        Directory.CreateDirectory(Path.Combine(crew, "tasks"));

        foreach (var agent in team.AgentFileNames)
            File.WriteAllText(Path.Combine(crew, "agents", agent), AgentYaml(agent));

        foreach (var task in team.TaskFileNames)
            File.WriteAllText(Path.Combine(crew, "tasks", task), TaskYaml(task));

        var metadata = team.Metadata with
        {
            Mounts = [.. team.Mounts.Select(mount => MountString(mount, data))],
        };
        TeamCatalog.SaveMetadata(directory, metadata);
    }

    /// <summary>
    /// The five artefacts the hydrator reads. Hand-written because Studio.Core reads this layout
    /// and never produces it — the CLI owns the writer, and Studio does not reference the CLI.
    /// </summary>
    private static async Task WriteSessionAsync(SessionSeed session, string workspace, string teamsRoot)
    {
        var directory = Path.Combine(workspace, ".orkeon", "forge", session.Slug);
        Directory.CreateDirectory(directory);

        var document = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["slug"] = session.Slug,
            ["title"] = session.Title,
            ["state"] = session.State,
            ["status"] = session.Status,
            ["format"] = session.Format,
            ["updatedAt"] = session.UpdatedAt,
            ["promotedTo"] = session.PromotedToTeamSlug is { } slug
                ? Path.Combine(teamsRoot, slug)
                : null,
        };
        await File.WriteAllTextAsync(
            Path.Combine(directory, "session.json"),
            JsonSerializer.Serialize(document, Indented));

        if (session.Transcript.Count > 0)
        {
            await File.WriteAllLinesAsync(
                Path.Combine(directory, "transcript.jsonl"), session.Transcript);
        }

        await WriteIfPresentAsync(directory, "brief.json", session.Brief);
        await WriteIfPresentAsync(directory, "blueprint.json", session.Blueprint);
        await WriteIfPresentAsync(directory, "verdict.json", session.Verdict);
        await WriteIfPresentAsync(directory, "last-run.json", session.LastRun);
    }

    private static Task WriteIfPresentAsync(string directory, string name, string? body) =>
        body is { Length: > 0 }
            ? File.WriteAllTextAsync(Path.Combine(directory, name), body)
            : Task.CompletedTask;

    /// <summary>
    /// A Docker-style mount string over a real folder of this world. The physical half goes through
    /// <see cref="Orkeon.Domain.FileSystem.FileSystemMount.Quote"/> because on Windows it carries a
    /// drive colon, which is the separator the string itself uses.
    /// </summary>
    private static string MountString(MountSeed mount, string data) =>
        Orkeon.Domain.FileSystem.FileSystemMount.Quote(Path.Combine(data, mount.Folder))
        + ":" + mount.VirtualPath + ":" + mount.Rights;

    private static string AgentYaml(string fileName) => string.Create(
        CultureInfo.InvariantCulture,
        $"role: {Path.GetFileNameWithoutExtension(fileName)}\ngoal: seeded for the screenshot campaign\n");

    private static string TaskYaml(string fileName) => string.Create(
        CultureInfo.InvariantCulture,
        $"description: {Path.GetFileNameWithoutExtension(fileName)}\nexpected_output: a short note\n");
}
