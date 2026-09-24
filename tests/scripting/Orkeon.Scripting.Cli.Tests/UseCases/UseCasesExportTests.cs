using System.Text.Json;
using Orkeon.Constants.Protocol;
using Orkeon.Scripting.Cli.Commands.UseCases;

namespace Orkeon.Scripting.Cli.Tests.UseCases;

/// <summary>
/// <c>orkeon usecases export</c> (STUDIO-41, D-02): one use case written as a team folder Orkeon
/// Studio imports as it is — the crew under <c>crew/</c>, the sample data, a folder behind each
/// mount, and the sidecar that names the team and records its mounts. Driven in-process on the
/// real disk: the five-sheet fixture through the options' seam, the embedded catalogue through
/// the grammar.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class UseCasesExportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"orkeon-usecases-export-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Export_writes_the_crew_its_data_its_output_folder_and_a_sidecar_with_its_mounts()
    {
        using var console = new TestConsole();
        var destination = Path.Combine(_root, "digest");

        var exit = await UseCasesCommand.ExecuteExportAsync(Export(UseCaseFixtures.MailDigest, destination, "fr"));

        Assert.Equal(Program.ExitOk, exit);
        // The example's crew, as it is.
        Assert.Equal(
            "name: \"daily-mail-digest\"\nprocess: \"sequential\"\n",
            await File.ReadAllTextAsync(Path.Combine(destination, "crew", "config.yaml"), TestContext.Current.CancellationToken));
        Assert.Equal(
            "Subject: hello\n",
            await File.ReadAllTextAsync(Path.Combine(destination, "data", "inbox.eml"), TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(destination, "output")));

        var sidecar = Sidecar(destination);
        Assert.Equal("Résumé quotidien des e-mails", sidecar.GetProperty("name").GetString());
        Assert.Equal("Chaque matin, lire les e-mails reçus et en faire un résumé court.", sidecar.GetProperty("description").GetString());
        Assert.Equal(["./data:/data:ro", "./output:/output:rw"], sidecar.GetProperty("mounts").EnumerateArray().Select(m => m.GetString()));

        // No session made this team: no forge.json — and nothing else beside what it needs.
        Assert.Equal(
            ["crew", "data", "output", "studio-team.json"],
            Directory.EnumerateFileSystemEntries(destination).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The drift pin of the sidecar's shape, the CLI's half: Studio's <c>StudioTeamMetadata</c>
    /// reads these very names, and <c>UseCaseImporterTests.The_sidecar_the_cli_writes_is_read_whole</c>
    /// reads this very text on Studio's side — the two cannot reference each other.
    /// </summary>
    [Fact]
    public async Task The_sidecar_is_written_in_the_shape_studio_reads()
    {
        using var console = new TestConsole();
        var destination = Path.Combine(_root, "digest");

        await UseCasesCommand.ExecuteExportAsync(Export(UseCaseFixtures.MailDigest, destination, "en"));

        Assert.Equal(
            """
            {
              "name": "Daily email digest",
              "description": "Every morning, read the emails that came in and write a short summary.",
              "mounts": [
                "./data:/data:ro",
                "./output:/output:rw"
              ]
            }
            """.ReplaceLineEndings("\n"),
            (await File.ReadAllTextAsync(Path.Combine(destination, "studio-team.json"), TestContext.Current.CancellationToken)).ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Export_names_and_describes_the_team_in_the_asked_language_chinese_included()
    {
        using var console = new TestConsole();
        var destination = Path.Combine(_root, "digest");

        // Studio says «zh», the catalogue «zh-Hans»: both are read.
        var exit = await UseCasesCommand.ExecuteExportAsync(Export(UseCaseFixtures.MailDigest, destination, "zh"));

        Assert.Equal(Program.ExitOk, exit);
        var sidecar = Sidecar(destination);
        Assert.Equal("每日邮件摘要", sidecar.GetProperty("name").GetString());
        Assert.Equal("每天早上阅读收到的邮件并写一份简短的摘要。", sidecar.GetProperty("description").GetString());
        // Written as the letters themselves, not as escape sequences: the file is read by people too.
        Assert.Contains(
            "每日邮件摘要",
            await File.ReadAllTextAsync(Path.Combine(destination, "studio-team.json"), TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_case_without_a_title_yet_is_named_after_its_id_and_mounts_nothing()
    {
        using var console = new TestConsole();
        var destination = Path.Combine(_root, "untitled");
        var catalog = UseCaseCatalog.Parse(
            UseCaseFixtures.Manifest,
            UseCaseFixtures.Files().With("09-experimental/05-untitled-example/config.yaml", "name: \"untitled\"\n"));

        var exit = await UseCasesCommand.ExecuteExportAsync(Export(UseCaseFixtures.Untitled, destination, "fr", catalog));

        Assert.Equal(Program.ExitOk, exit);
        var sidecar = Sidecar(destination);
        Assert.Equal(UseCaseFixtures.Untitled, sidecar.GetProperty("name").GetString());
        Assert.False(sidecar.TryGetProperty("description", out _));
        Assert.False(sidecar.TryGetProperty("mounts", out _));
        // A crew that writes nothing has no output folder to write into.
        Assert.Equal(
            ["crew", "studio-team.json"],
            Directory.EnumerateFileSystemEntries(destination).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task In_events_mode_the_export_answers_with_the_folder_and_what_it_holds()
    {
        using var console = new TestConsole();
        var destination = Path.Combine(_root, "digest");

        var exit = await UseCasesCommand.ExecuteExportAsync(Export(UseCaseFixtures.MailDigest, destination, "en", events: true));

        Assert.Equal(Program.ExitOk, exit);
        var exported = SingleEvent(console.Stdout, UseCaseEventKinds.Exported);
        Assert.Equal(UseCaseFixtures.MailDigest, exported.GetProperty("id").GetString());
        Assert.Equal(Path.GetFullPath(destination), exported.GetProperty("path").GetString());
        Assert.Equal("Daily email digest", exported.GetProperty("name").GetString());
        Assert.Equal("en", exported.GetProperty("lang").GetString());
        Assert.Equal(
            ["crew/config.yaml", "data/inbox.eml", "studio-team.json"],
            exported.GetProperty("files").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(["./data:/data:ro", "./output:/output:rw"], exported.GetProperty("mounts").EnumerateArray().Select(m => m.GetString()));
        Assert.All(Events(console.Stdout), e => Assert.Contains(e.GetProperty("kind").GetString(), UseCaseEventKinds.All));
    }

    [Fact]
    public async Task In_text_mode_the_export_says_what_it_wrote_and_how_to_run_it()
    {
        using var console = new TestConsole();
        var destination = Path.Combine(_root, "digest");

        var exit = await UseCasesCommand.ExecuteExportAsync(Export(UseCaseFixtures.MailDigest, destination, "en"));

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains(UseCaseFixtures.MailDigest, console.Stdout, StringComparison.Ordinal);
        Assert.Contains(Path.GetFullPath(destination), console.Stdout, StringComparison.Ordinal);
        Assert.Contains("\"Daily email digest\"", console.Stdout, StringComparison.Ordinal);
        foreach (var written in (string[])["crew/config.yaml", "data/inbox.eml", "output/", "studio-team.json"])
            Assert.Contains(written, console.Stdout, StringComparison.Ordinal);
        // The folder's own launch: from inside it, with its mounts — a bare run would write nowhere.
        Assert.Contains("orkeon run crew/config.yaml --mount ./data:/data:ro ./output:/output:rw", console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reference_only_case_is_refused_with_a_typed_error_and_nothing_is_written()
    {
        using var console = new TestConsole();
        var destination = Path.Combine(_root, "invoices");

        var exit = await UseCasesCommand.ExecuteExportAsync(Export(UseCaseFixtures.InvoiceMatching, destination, "en"));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains(UseCaseErrorCodes.NotImportable, console.Stderr, StringComparison.Ordinal);
        Assert.Contains(UseCaseFixtures.InvoiceMatching, console.Stderr, StringComparison.Ordinal);
        Assert.False(Path.Exists(destination));
    }

    [Fact]
    public async Task A_reference_only_case_of_the_embedded_catalogue_is_refused_in_events_mode()
    {
        using var console = new TestConsole();
        var destination = Path.Combine(_root, "trading");

        var exit = await Program.DispatchAsync(["usecases", "export", "31-algo-trading", "--to", destination, "--events", "jsonl"]);

        Assert.Equal(Program.ExitScriptError, exit);
        var error = SingleEvent(console.Stdout, UseCaseEventKinds.Error);
        Assert.Equal(UseCaseErrorCodes.NotImportable, error.GetProperty("code").GetString());
        Assert.False(error.GetProperty("recoverable").GetBoolean());
        Assert.False(Path.Exists(destination));
    }

    [Fact]
    public async Task An_unknown_id_is_the_typed_unknown_id_error()
    {
        using var console = new TestConsole();
        var destination = Path.Combine(_root, "nothing");

        var exit = await Program.DispatchAsync(["usecases", "export", "99-no-such-example", "--to", destination]);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains(UseCaseErrorCodes.UnknownId, console.Stderr, StringComparison.Ordinal);
        Assert.Contains("99-no-such-example", console.Stderr, StringComparison.Ordinal);
        Assert.False(Path.Exists(destination));
    }

    [Fact]
    public async Task A_destination_that_holds_anything_is_refused_and_left_as_it_was()
    {
        using var console = new TestConsole();
        var destination = Path.Combine(_root, "taken");
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(destination, "notes.txt"), "mine", TestContext.Current.CancellationToken);

        var exit = await UseCasesCommand.ExecuteExportAsync(Export(UseCaseFixtures.MailDigest, destination, "en"));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains(UseCaseErrorCodes.DestinationNotEmpty, console.Stderr, StringComparison.Ordinal);
        Assert.Equal(["notes.txt"], Directory.EnumerateFileSystemEntries(destination).Select(Path.GetFileName));
        Assert.Equal("mine", await File.ReadAllTextAsync(Path.Combine(destination, "notes.txt"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_file_where_the_folder_would_go_is_refused()
    {
        using var console = new TestConsole();
        var destination = Path.Combine(_root, "file");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(destination, "mine", TestContext.Current.CancellationToken);

        var exit = await UseCasesCommand.ExecuteExportAsync(Export(UseCaseFixtures.MailDigest, destination, "en"));

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains(UseCaseErrorCodes.DestinationNotEmpty, console.Stderr, StringComparison.Ordinal);
        Assert.Equal("mine", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_empty_existing_destination_is_written_into()
    {
        using var console = new TestConsole();
        var destination = Path.Combine(_root, "empty");
        Directory.CreateDirectory(destination);

        var exit = await UseCasesCommand.ExecuteExportAsync(Export(UseCaseFixtures.MailDigest, destination, "en"));

        Assert.Equal(Program.ExitOk, exit);
        Assert.True(File.Exists(Path.Combine(destination, "crew", "config.yaml")));
    }

    /// <summary>
    /// The acceptance line of the fiche on the real catalogue, through the real grammar: an
    /// importable example with sample data leaves as a team folder, its crew byte for byte.
    /// </summary>
    [Fact]
    public async Task An_example_of_the_embedded_catalogue_is_exported_with_its_data()
    {
        using var console = new TestConsole();
        var destination = Path.Combine(_root, "research");
        const string id = "01-research-assistant";

        var exit = await Program.DispatchAsync(["usecases", "export", id, "--to", destination, "--lang", "en", "--events", "jsonl"]);

        Assert.Equal(Program.ExitOk, exit);
        var catalog = UseCaseCatalog.Embedded;
        Assert.Equal(Bytes(catalog, id, "config.yaml"), await File.ReadAllBytesAsync(Path.Combine(destination, "crew", "config.yaml"), TestContext.Current.CancellationToken));
        foreach (var data in catalog.FilesOf(id).Where(f => f.Path.StartsWith("data/", StringComparison.Ordinal)))
        {
            Assert.Equal(
                Bytes(catalog, id, data.Path),
                await File.ReadAllBytesAsync(Path.Combine(destination, data.Path), TestContext.Current.CancellationToken));
        }

        var exported = SingleEvent(console.Stdout, UseCaseEventKinds.Exported);
        Assert.Contains("data/ev-sales-by-region.csv", exported.GetProperty("files").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(["./data:/data:ro", "./output:/output:rw"], Sidecar(destination).GetProperty("mounts").EnumerateArray().Select(m => m.GetString()));
        Assert.True(Directory.Exists(Path.Combine(destination, "output")));
    }

    // --- helpers ---

    private static UseCasesExportOptions Export(
        string id, string destination, string language, UseCaseCatalog? catalog = null, bool events = false) =>
        new()
        {
            Id = id,
            To = destination,
            Language = language,
            Events = events ? "jsonl" : null,
            Catalog = catalog ?? UseCaseFixtures.Catalog(),
        };

    private static JsonElement Sidecar(string destination) =>
        JsonElement.Parse(File.ReadAllText(Path.Combine(destination, "studio-team.json")));

    private static byte[] Bytes(UseCaseCatalog catalog, string id, string path)
    {
        using var stream = catalog.OpenFile(id, path);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static List<JsonElement> Events(string stdout) =>
        [.. stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonElement.Parse(line))];

    private static JsonElement SingleEvent(string stdout, string kind) =>
        Assert.Single(Events(stdout), e => e.GetProperty("kind").GetString() == kind);
}
