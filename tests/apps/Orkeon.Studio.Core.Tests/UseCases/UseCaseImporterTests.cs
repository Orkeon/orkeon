using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Teams;
using Orkeon.Studio.Core.Tests.Doubles;
using Orkeon.Studio.Core.UseCases;

namespace Orkeon.Studio.Core.Tests.UseCases;

/// <summary>
/// « Import as is » in Core (STUDIO-41): the <c>usecases export</c> argv and its answer, the sidecar
/// the CLI writes read back whole, and the import of the exported folder through the path every
/// team takes — named, described, modifiable, its folders resolved, its staging folder gone.
/// </summary>
public sealed class UseCaseImporterTests : IDisposable
{
    private static readonly string InstallDirectory = Path.Combine("/", "opt", "orkeon");
    private static readonly string BinaryPath = Path.Combine(InstallDirectory, "orkeon");

    /// <summary>When a copy entered the teams root (STUDIO-32): a duplicate and an import date their arrival.</summary>
    private static readonly DateTimeOffset AddedOn = new(2026, 9, 24, 11, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-usecase-import-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string TeamsRoot => Path.Combine(_root, "teams");

    private static OrkeonBinaryLocator Locator(bool installed = true)
    {
        var executables = new FakeExecutableProbe { BaseDirectory = InstallDirectory };
        if (installed)
            executables.WithFile(BinaryPath);
        return new OrkeonBinaryLocator(executables);
    }

    private static UseCase Digest(string? titleZh = null) => new()
    {
        Id = "01-daily-mail-digest",
        Category = "01-enterprise",
        Importable = true,
        Title = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fr"] = "Résumé quotidien des e-mails",
            ["en"] = "Daily email digest",
            ["zh-Hans"] = titleZh ?? "每日邮件摘要",
        },
    };

    [Fact]
    public void The_export_argv_follows_the_cli_grammar_in_the_catalogues_language()
    {
        Assert.Equal(
            ["usecases", "export", "03-email-pipeline", "--to", "/tmp/team", "--lang", "zh-Hans", "--events", "jsonl"],
            UseCaseArgumentsBuilder.BuildExport("03-email-pipeline", "/tmp/team", "zh"));
        Assert.Equal(
            ["usecases", "export", "03-email-pipeline", "--to", "/tmp/team", "--lang", "en", "--events", "jsonl"],
            UseCaseArgumentsBuilder.BuildExport("03-email-pipeline", "/tmp/team", null));
    }

    [Fact]
    public async Task The_export_is_read_off_its_exported_line()
    {
        var destination = Path.Combine(_root, "staged", "daily-email-digest");
        var processes = new FakeUseCaseExportLauncher();
        using var client = new UseCaseClient(processes, Locator());

        var result = await client.ExportAsync("01-daily-mail-digest", destination, "fr", TestContext.Current.CancellationToken);

        Assert.Null(result.Failure);
        Assert.Equal(destination, result.Folder);
        var request = Assert.Single(processes.Requests);
        Assert.Equal(BinaryPath, request.FileName);
        Assert.Equal(UseCaseArgumentsBuilder.BuildExport("01-daily-mail-digest", destination, "fr"), request.Arguments);
    }

    [Fact]
    public async Task A_refused_export_is_the_clis_typed_refusal_in_its_own_words()
    {
        var processes = new FakeUseCaseExportLauncher
        {
            Refusal = ("USECASES-NOT-IMPORTABLE", "'31-algo-trading' is reference only"),
        };
        using var client = new UseCaseClient(processes, Locator());

        var result = await client.ExportAsync("31-algo-trading", Path.Combine(_root, "t"), "en", TestContext.Current.CancellationToken);

        Assert.Null(result.Folder);
        Assert.Equal(UseCaseFailureKind.Refused, result.Failure!.Kind);
        Assert.Equal("USECASES-NOT-IMPORTABLE", result.Failure.Code);
        Assert.Equal("USECASES-NOT-IMPORTABLE: '31-algo-trading' is reference only", result.Failure.Detail);
    }

    [Fact]
    public async Task Without_the_engine_the_export_is_the_engine_missing_failure_and_nothing_is_spawned()
    {
        var processes = new FakeUseCaseExportLauncher();
        using var client = new UseCaseClient(processes, Locator(installed: false));

        var result = await client.ExportAsync("01-daily-mail-digest", Path.Combine(_root, "t"), "en", TestContext.Current.CancellationToken);

        Assert.Equal(UseCaseFailureKind.EngineMissing, result.Failure!.Kind);
        Assert.Empty(processes.Requests);
    }

    [Fact]
    public async Task A_run_that_answers_no_exported_line_says_so_with_its_exit_code()
    {
        var processes = new FakeProcessLauncher { ExitCode = 2 };
        using var client = new UseCaseClient(processes, Locator());

        var result = await client.ExportAsync("01-daily-mail-digest", Path.Combine(_root, "t"), "en", TestContext.Current.CancellationToken);

        Assert.Equal(UseCaseFailureKind.Stopped, result.Failure!.Kind);
        Assert.Equal(2, result.Failure.ExitCode);
        Assert.Contains("usecases.exported", result.Failure.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The drift pin of the sidecar's shape, Studio's half: the very text
    /// <c>UseCasesExportTests.The_sidecar_is_written_in_the_shape_studio_reads</c> pins on the
    /// CLI's side is read whole — the CLI and Studio cannot reference each other.
    /// </summary>
    [Fact]
    public void The_sidecar_the_cli_writes_is_read_whole()
    {
        var team = Path.Combine(_root, "digest");
        Directory.CreateDirectory(team);
        File.WriteAllText(Path.Combine(team, StudioTeamMetadata.FileName), """
            {
              "name": "Daily email digest",
              "description": "Every morning, read the emails that came in and write a short summary.",
              "mounts": [
                "./data:/data:ro",
                "./output:/output:rw"
              ]
            }
            """);

        var summary = TeamCatalog.Describe(team);

        Assert.Equal("Daily email digest", summary.Name);
        Assert.Equal("Every morning, read the emails that came in and write a short summary.", summary.Description);
        Assert.Equal(["./data:/data:ro", "./output:/output:rw"], summary.Metadata!.Mounts);
        Assert.Equal(
            [$"{Path.Combine(team, "data")}:/data:ro", $"{Path.Combine(team, "output")}:/output:rw"],
            summary.Mounts);
    }

    [Fact]
    public async Task Importing_lands_a_named_described_modifiable_team_with_its_folders_and_no_staging_left()
    {
        var processes = new FakeUseCaseExportLauncher();
        using var client = new UseCaseClient(processes, Locator());
        var folder = Path.Combine(TeamsRoot, "daily-email-digest");

        var result = await UseCaseImporter.ImportAsync(
            client, "01-daily-mail-digest", "Daily email digest", folder, TeamsRoot, "en", AddedOn, TestContext.Current.CancellationToken);

        Assert.Null(result.Failure);
        Assert.Equal(folder, result.TeamPath);
        var team = TeamCatalog.Describe(folder);
        Assert.Equal("Daily email digest", team.Name);
        Assert.Equal("Every morning, read the emails that came in and write a short summary.", team.Description);
        // « Modify » rebuilds a session from crew/ (D-04): the folder holds a YAML crew.
        Assert.True(team.HasYamlCrew);
        Assert.Null(team.ForgeSessionId);
        Assert.Equal(["./data:/data:ro", "./output:/output:rw"], team.Metadata!.Mounts);
        Assert.Equal(
            [$"{Path.Combine(folder, "data")}:/data:ro", $"{Path.Combine(folder, "output")}:/output:rw"],
            team.Mounts);
        Assert.True(File.Exists(Path.Combine(folder, "data", "inbox.eml")));
        // Its arrival is its activity (STUDIO-32): the CLI writes no date, the import stamps one.
        Assert.Equal(AddedOn, team.AddedAt);
        Assert.Null(team.LastRunAt);

        // The export was staged under the very name the team's folder took, and the staging is gone.
        var staged = Assert.Single(processes.Destinations);
        Assert.Equal("daily-email-digest", Path.GetFileName(staged));
        Assert.False(Directory.Exists(Path.GetDirectoryName(staged)));
    }

    [Fact]
    public async Task A_free_name_is_the_name_the_imported_team_takes()
    {
        var processes = new FakeUseCaseExportLauncher();
        using var client = new UseCaseClient(processes, Locator());
        var taken = Path.Combine(TeamsRoot, "daily-email-digest");
        Directory.CreateDirectory(taken);
        var free = TeamCatalog.FreeSibling(taken);
        var name = TeamCatalog.FreeName("Daily email digest", taken, free);

        var result = await UseCaseImporter.ImportAsync(
            client, "01-daily-mail-digest", name, free, TeamsRoot, "en", AddedOn, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(TeamsRoot, "daily-email-digest-2"), result.TeamPath);
        Assert.Equal("Daily email digest (2)", TeamCatalog.Describe(result.TeamPath!).Name);
    }

    [Fact]
    public async Task A_refused_export_leaves_nothing_in_the_teams_root_and_no_staging()
    {
        var processes = new FakeUseCaseExportLauncher
        {
            Refusal = ("USECASES-NOT-IMPORTABLE", "'31-algo-trading' is reference only"),
        };
        using var client = new UseCaseClient(processes, Locator());

        var result = await UseCaseImporter.ImportAsync(
            client, "31-algo-trading", "Simulated trading room", Path.Combine(TeamsRoot, "simulated-trading-room"), TeamsRoot, "en", AddedOn,
            TestContext.Current.CancellationToken);

        Assert.Null(result.TeamPath);
        Assert.Equal("USECASES-NOT-IMPORTABLE", result.Failure!.Code);
        Assert.Empty(TeamCatalog.List(TeamsRoot));
        Assert.False(Directory.Exists(Path.GetDirectoryName(Assert.Single(processes.Destinations))));
    }

    [Fact]
    public void The_team_is_named_after_the_title_in_the_ui_language_and_foldered_by_the_slug_rule()
    {
        var digest = Digest();

        Assert.Equal("Résumé quotidien des e-mails", UseCaseImporter.TeamNameOf(digest, "fr"));
        Assert.Equal(
            Path.Combine(TeamsRoot, "resume-quotidien-des-e-mails"),
            UseCaseImporter.TeamFolderOf(digest, UseCaseImporter.TeamNameOf(digest, "fr"), TeamsRoot));
        // A Chinese title keeps nothing the folder rule can use: the id names the folder instead.
        Assert.Equal("每日邮件摘要", UseCaseImporter.TeamNameOf(digest, "zh"));
        Assert.Equal(
            Path.Combine(TeamsRoot, "01-daily-mail-digest"),
            UseCaseImporter.TeamFolderOf(digest, UseCaseImporter.TeamNameOf(digest, "zh"), TeamsRoot));
        // A sheet without a title yet is named after its id.
        Assert.Equal("05-untitled", UseCaseImporter.TeamNameOf(new UseCase { Id = "05-untitled", Category = "09-experimental" }, "fr"));
    }

    [Fact]
    public void The_free_name_carries_the_free_folders_suffix_within_the_name_cap()
    {
        Assert.Equal("Ma veille (2)", TeamCatalog.FreeName("Ma veille", "/teams/ma-veille", "/teams/ma-veille-2"));

        var longName = new string('a', TeamCatalog.MaxNameLength);
        var free = TeamCatalog.FreeName(longName, "/teams/a", "/teams/a-12");
        Assert.Equal(TeamCatalog.MaxNameLength, free.Length);
        Assert.EndsWith(" (12)", free, StringComparison.Ordinal);
    }
}
