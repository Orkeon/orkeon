using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Host.Files;
using Orkeon.Host.Tests.Api;
using Orkeon.Host.Tests.Doubles;
using Orkeon.Infrastructure.FileSystem;

namespace Orkeon.Host.Tests.Files;

/// <summary>Which folders of a crew people may reach: its declaration, held to what the operator mounted.</summary>
public sealed class CrewFileFoldersTests : IDisposable
{
    private const string Output = "files:\n  - path: /output\n    download: true\n";

    private readonly HostApiHarness _harness = new();
    private readonly RecordingLoggerProvider _logs = new();
    private readonly List<IDisposable> _held = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Crews => Path.Combine(_harness.Root, "teams");

    public void Dispose()
    {
        foreach (var held in _held)
            held.Dispose();
        _logs.Dispose();
        _harness.Dispose();
    }

    private string Folder(params string[] names)
    {
        var path = Path.Combine([_harness.Root, .. names]);
        Directory.CreateDirectory(path);
        return path;
    }

    private void Declare(string file, string text)
    {
        var path = Path.Combine(Crews, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private void Remove(string file) => File.Delete(Path.Combine(Crews, file));

    private HostedCrewOptions Crew(string name, string file, params string[] mounts) =>
        new() { Name = name, Path = Path.Combine(Crews, file), Mounts = mounts };

    private CrewFileFolders Reader(IReadOnlyList<HostedCrewOptions> crews, params FileSystemMount[] boot)
    {
        var plan = HostCrewMounts.For(crews);
        var registry = new FileSystemRegistry([.. plan.Mounts.Select(FileSystemMount.Parse), .. boot]);
        _held.Add(registry);
        var factory = LoggerFactory.Create(b => b.AddProvider(_logs));
        _held.Add(factory);
        return new CrewFileFolders(
            Options.Create(new OrkeonHostOptions { Crews = [.. crews] }), plan, registry,
            new FileSystemService(registry, new FakePathValidator(), NullLogger<FileSystemService>.Instance),
            factory.CreateLogger<CrewFileFolders>());
    }

    private string Mount(string folder, string root, string rights = "rw") => $"{Folder(folder)}:{root}:{rights}";

    private static string[] Text(OpenedFolders opened) => [.. opened.Open.Select(f => $"{f.Path} {f.Download} {f.Upload}")];

    private int Warnings => _logs.Logger.Entries.Count(e => e.Level == LogLevel.Warning);

    [Fact]
    public async Task A_yaml_crew_opens_what_it_declares_within_its_own_mounts()
    {
        Declare("a.yaml", Output + "  - path: /output/depots/\n    upload: true\n");
        Declare("b.yaml", Output);
        var reader = Reader([Crew("a", "a.yaml", Mount("outA", "/output")), Crew("b", "b.yaml")]);

        var a = await reader.ReadAsync("A", Ct);
        var b = await reader.ReadAsync("b", Ct);

        Assert.Equal(["/output True False", "/output/depots False True"], Text(a));
        Assert.Null(a.Reason);
        // What one crew was granted opens nothing to another.
        Assert.Empty(b.Open);
        Assert.True(b.Invalid);
    }

    [Fact]
    public async Task A_crew_directory_is_read_from_its_settings_else_from_the_fallback()
    {
        Declare("d/crew.yaml", Output);
        var reader = Reader([Crew("d", "d", Mount("out", "/output"))]);

        var fallback = await reader.ReadAsync("d", Ct);
        Declare("d/config.yaml", "name: d\n");
        var settings = await reader.ReadAsync("d", Ct);

        Assert.Equal(["/output True False"], Text(fallback));
        Assert.Empty(settings.Open);
        Assert.False(settings.Invalid);
    }

    [Fact]
    public async Task A_script_is_never_evaluated_and_its_neighbour_alone_is_read()
    {
        Declare("s.ork.ts", "throw new Error('evaluated'); // files: /output");
        var reader = Reader([Crew("s", "s.ork.ts", Mount("out", "/output"))]);

        var without = await reader.ReadAsync("s", Ct);
        Declare("s.files.yaml", Output);
        var with = await reader.ReadAsync("s", Ct);
        Declare("s.files.yaml", "name: s\n" + Output);
        var more = await reader.ReadAsync("s", Ct);

        Assert.Empty(without.Open);
        Assert.False(without.Invalid);
        Assert.Contains("s.files.yaml", without.Reason, StringComparison.Ordinal);
        Assert.Equal(["/output True False"], Text(with));
        Assert.True(more.Invalid);
    }

    [Fact]
    public async Task The_neighbour_of_a_yaml_crew_is_not_read()
    {
        Declare("a.yaml", "name: a\n");
        Declare("a.files.yaml", Output);

        Assert.Empty((await Reader([Crew("a", "a.yaml", Mount("out", "/output"))]).ReadAsync("a", Ct)).Open);
    }

    [Fact]
    public async Task A_declaration_is_read_again_even_when_its_date_stands_still()
    {
        Declare("a.yaml", Output);
        var file = Path.Combine(Crews, "a.yaml");
        var date = File.GetLastWriteTimeUtc(file);
        var reader = Reader([Crew("a", "a.yaml", Mount("out", "/output"))]);

        var before = await reader.ReadAsync("a", Ct);
        Declare("a.yaml", Output.Replace("download", "upload", StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(file, date);
        var after = await reader.ReadAsync("a", Ct);
        Remove("a.yaml");
        var gone = await reader.ReadAsync("a", Ct);

        Assert.Equal(["/output True False"], Text(before));
        Assert.Equal(["/output False True"], Text(after));
        Assert.Empty(gone.Open);
        Assert.Contains("/a.yaml", gone.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(_harness.Root, gone.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_invalid_declaration_closes_its_crew_alone_and_is_logged_once()
    {
        Declare("bad.yaml", Output + "  - path: /output/../x\n    download: true\n");
        Declare("good.yaml", Output);
        var reader = Reader([Crew("bad", "bad.yaml", Mount("o1", "/output")), Crew("good", "good.yaml", Mount("o2", "/output"))]);

        var bad = await reader.ReadAsync("bad", Ct);
        await reader.ReadAsync("bad", Ct);
        var good = await reader.ReadAsync("good", Ct);

        Assert.Empty(bad.Open);
        Assert.True(bad.Invalid);
        Assert.NotNull(bad.Reason);
        Assert.Equal(["/output True False"], Text(good));
        Assert.Equal(1, Warnings);

        Declare("bad.yaml", Output);
        Assert.Equal(["/output True False"], Text(await reader.ReadAsync("bad", Ct)));
        Declare("bad.yaml", "files: 3");
        await reader.ReadAsync("bad", Ct);
        Assert.Equal(2, Warnings);
    }

    [Theory]
    [InlineData("/nowhere", "download")]
    [InlineData("/outputs", "download")]
    [InlineData("/OUTPUT", "download")]
    [InlineData("/output/../crews", "download")]
    [InlineData("/crews", "download")]
    [InlineData("/Crews/a", "download")]
    [InlineData("/crews-1", "download")]
    [InlineData("/inbox", "upload")]
    [InlineData("/OUTBOX/x", "download")]
    [InlineData("/secrets", "download")]
    [InlineData("/secrets/x", "download")]
    [InlineData("/output/hidden", "download")]
    [InlineData("/readonly", "upload")]
    [InlineData("/output/private", "upload")]
    [InlineData("/output/Private/x", "upload")]
    [InlineData("/noread", "download")]
    [InlineData("/all", "download")]
    [InlineData("/teams", "download")]
    [InlineData("/dir/agents", "download")]
    public async Task A_folder_the_operator_did_not_open_is_refused_and_closes_the_others(string path, string right)
    {
        Declare("a.yaml", $"files:\n  - path: /fine\n    download: true\n  - path: {path}\n    {right}: true\n");
        Declare("d/config.yaml", "name: d\n");
        Folder("teams", "d", "agents");
        var reader = Reader(
            [
                Crew("a", "a.yaml", Mount("fine", "/fine"), Mount("readonly", "/readonly", "ro"), Mount("out", "/output", "rw;private:ro")),
                Crew("d", "d"),
            ],
            new FileSystemMount(Folder("out", "hidden"), "/secrets", FileAccessRights.ReadWrite, visibility: MountVisibility.Internal),
            new FileSystemMount(Folder("noread"), "/noread", FileAccessRights.Write | FileAccessRights.Create),
            new FileSystemMount(_harness.Root, "/all", FileAccessRights.Read),
            new FileSystemMount(Crews, "/teams", FileAccessRights.Read),
            new FileSystemMount(Path.Combine(Crews, "d"), "/dir", FileAccessRights.Read));

        var opened = await reader.ReadAsync("a", Ct);

        Assert.Empty(opened.Open);
        Assert.True(opened.Invalid);
        Assert.Contains(path, opened.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(_harness.Root, opened.Reason, StringComparison.Ordinal);
        Assert.Equal(1, Warnings);
    }

    [Theory]
    [InlineData("/inbox", "/inbox", "belongs to the conversation")]
    [InlineData("/INBOX/x", "/INBOX", "belongs to the conversation")]
    [InlineData("/outbox", "/outbox", "belongs to the conversation, and is always open")]
    [InlineData("/OUTBOX/x", "/OUTBOX", "belongs to the conversation, and is always open")]
    [InlineData("/crews/elsewhere", "/crews/elsewhere", "definitions")]
    [InlineData("/Crews", "/Crews", "definitions")]
    [InlineData("/crews-7", "/crews-7", "definitions")]
    [InlineData("/crews-7/x", "/crews-7", "definitions")]
    public async Task A_folder_that_is_not_the_operators_is_refused_even_where_one_is_mounted(string path, string mounted, string reason)
    {
        Declare("a.yaml", $"files:\n  - path: {path}\n    download: true\n");
        var reader = Reader([Crew("a", "a.yaml")], new FileSystemMount(Folder("mounted"), mounted, FileAccessRights.ReadWrite));

        var opened = await reader.ReadAsync("a", Ct);

        Assert.Empty(opened.Open);
        Assert.Contains($"'{path}' is refused", opened.Reason, StringComparison.Ordinal);
        Assert.Contains(reason, opened.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FileAccessRights.Write | FileAccessRights.Create, "download: true")]
    [InlineData(FileAccessRights.Read | FileAccessRights.Write, "download: true\n    upload: true")]
    [InlineData(FileAccessRights.Read | FileAccessRights.Create, "download: true\n    upload: true")]
    public async Task A_folder_is_refused_where_one_right_it_takes_is_missing(FileAccessRights granted, string opens)
    {
        Declare("a.yaml", $"files:\n  - path: /part\n    {opens}\n");
        var reader = Reader([Crew("a", "a.yaml")], new FileSystemMount(Folder("part"), "/part", granted));

        var opened = await reader.ReadAsync("a", Ct);

        Assert.Empty(opened.Open);
        Assert.Contains("'/part' is refused", opened.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_declaration_that_cannot_be_read_closes_its_crew_and_shows_no_path_of_the_machine()
    {
        Declare("a.yaml", Output);
        var reader = Reader([Crew("a", "a.yaml", Mount("out", "/output"))]);
        OpenedFolders opened;
        using (new FileStream(Path.Combine(Crews, "a.yaml"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            opened = await reader.ReadAsync("a", Ct);

        Assert.Empty(opened.Open);
        Assert.True(opened.Invalid);
        Assert.Contains("cannot be read", opened.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(_harness.Root, opened.Reason, StringComparison.Ordinal);
        Assert.Equal(["/output True False"], Text(await reader.ReadAsync("a", Ct)));
    }

    [Fact]
    public async Task A_fault_that_comes_back_is_logged_again()
    {
        var reader = Reader([Crew("a", "a.yaml", Mount("out", "/output"))]);
        foreach (var text in new[] { "files: 3", Output, "files: 3" })
        {
            Declare("a.yaml", text);
            await reader.ReadAsync("a", Ct);
        }

        Assert.Equal(2, Warnings);
    }

    [Fact]
    public async Task A_second_address_of_crews_that_are_reached_by_a_link_is_refused()
    {
        if (OperatingSystem.IsWindows())
            return;
        var real = Folder("real");
        Directory.CreateSymbolicLink(Crews, real);
        Declare("a.yaml", "files:\n  - path: /second\n    download: true\n");
        var reader = Reader([Crew("a", "a.yaml")], new FileSystemMount(real, "/second", FileAccessRights.Read));

        Assert.Contains("crew's definition", (await reader.ReadAsync("a", Ct)).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_is_shown_where_the_mount_forbids_reading()
    {
        using var registry = new FileSystemRegistry(
            [new FileSystemMount(_harness.Root, "/m", FileAccessRights.ReadWrite, [new SubPathOverride("private", FileAccessRights.Write)])]);
        var fs = new FileSystemService(registry, new FakePathValidator(), NullLogger<FileSystemService>.Instance);

        Assert.True(DownloadableFiles.IsShown(fs, "/m", "/m/a.txt"));
        Assert.False(DownloadableFiles.IsShown(fs, "/m", "/m/private/a.txt"));
    }

    [Fact]
    public async Task A_folder_written_only_takes_uploads()
    {
        Declare("a.yaml", "files:\n  - path: /drop\n    upload: true\n");
        var reader = Reader([Crew("a", "a.yaml")],
            new FileSystemMount(Folder("drop"), "/drop", FileAccessRights.Write | FileAccessRights.Create));

        Assert.Equal(["/drop False True"], Text(await reader.ReadAsync("a", Ct)));
    }

    [Fact]
    public async Task A_link_into_the_crews_is_refused()
    {
        if (OperatingSystem.IsWindows())
            return;
        Declare("a.yaml", "files:\n  - path: /linked\n    download: true\n");
        var link = Path.Combine(_harness.Root, "link");
        Directory.CreateSymbolicLink(link, Crews);
        var reader = Reader([Crew("a", "a.yaml")], new FileSystemMount(link, "/linked", FileAccessRights.Read));

        Assert.True((await reader.ReadAsync("a", Ct)).Invalid);
    }

    [Fact]
    public async Task A_crew_the_host_does_not_serve_opens_nothing() =>
        Assert.Empty((await Reader([]).ReadAsync("who", Ct)).Open);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_daemon_reads_them_when_files_are_on(bool enabled)
    {
        using var host = _harness.Build(new HeldLlmProvider("unused"), port: 1, configure: (root, api) =>
        {
            api["Enabled"] = false;
            var orkeon = (Dictionary<string, object?>)root["Orkeon"]!;
            ((Dictionary<string, object?>)orkeon["Host"]!)["Files"] = new Dictionary<string, object?>
            {
                ["Enabled"] = enabled,
                ["Root"] = Path.Combine(_harness.Root, "conversations"),
            };
        });

        var reader = host.Services.GetService<CrewFileFolders>();

        Assert.Equal(enabled, reader is not null);
        if (reader is not null)
            Assert.Contains("no 'files:' block", (await reader.ReadAsync("support", Ct)).Reason, StringComparison.Ordinal);
    }

    private sealed class FakePathValidator : IPathValidator
    {
        public PathValidationResult ValidatePath(string requestedPath, string? workspaceRoot = null)
            => PathValidationResult.Allowed(requestedPath);
    }
}
