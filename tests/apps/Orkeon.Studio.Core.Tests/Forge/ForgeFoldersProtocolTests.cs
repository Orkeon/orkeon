using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.Forge;
using Orkeon.Studio.Core.Process;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Forge;

/// <summary>
/// The folders step on Studio's side of the wire (STUDIO-46): <c>folders.proposed</c> read into
/// the model, <c>folders.confirmed</c> written back down stdin, <c>brief.ready</c> carrying the
/// confirmed list, and that list as the team's mounts. The proposed line is the CLI's golden
/// one (<c>ForgeEventWriterTests.The_folders_proposed_line_is_the_pinned_golden_form</c>); the
/// confirmed line is pinned on the CLI side as what its channel reads.
/// </summary>
public sealed class ForgeFoldersProtocolTests : IDisposable
{
    private const string InstallDirectory = "/opt/orkeon";
    private static readonly string BinaryPath = Path.Combine(InstallDirectory, "orkeon");

    /// <summary>Verbatim from the CLI's golden test.</summary>
    private const string GoldenProposed =
        """{"v":2,"seq":1,"ts":"2026-08-19T12:00:00Z","kind":"folders.proposed","folders":[{"path":"/inpdf","role":"input","purpose":"Les PDF à convertir"},{"path":"/outmd","role":"output","purpose":"Les fichiers Markdown"}],"defaults":false}""";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "studio-folders-" + Guid.NewGuid().ToString("N"));

    public ForgeFoldersProtocolTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static OrkeonEvent Parse(string line)
    {
        Assert.True(OrkeonEventParser.TryParse(line, out var parsed), line);
        return parsed!;
    }

    private static string BriefReady(string folders) =>
        $$$"""{"v":2,"seq":2,"ts":"t","kind":"brief.ready","brief":{"goal":"Convertir","folders":{{{folders}}},"acceptance":[]}}""";

    private const string Blueprint =
        """{"v":2,"seq":3,"ts":"t","kind":"blueprint.ready","blueprint":{"crew":{"name":"pdf"},"agents":[{"key":"l","role":"Reader","tools":["file_read"]},{"key":"w","role":"Writer","tools":["file_write"]}],"tasks":[{"key":"t","description":"d","agent":"w","deliverable":"/outmd/x.md"}]}}""";

    [Fact]
    public async Task Folders_proposed_then_folders_confirmed_round_trips_over_the_protocol()
    {
        var model = new ForgeSessionModel();
        model.Feed(Parse(GoldenProposed));

        Assert.True(model.FoldersPending);
        Assert.Equal(["/inpdf", "/outmd"], model.ProposedFolders.Select(f => f.Path));
        Assert.True(model.ProposedFolders[0].IsInput);
        Assert.Equal("Les PDF à convertir", model.ProposedFolders[0].Purpose);

        // The user renames the output and binds the input to a real folder; Studio answers.
        var executables = new FakeExecutableProbe { BaseDirectory = InstallDirectory }.WithFile(BinaryPath);
        var processes = new FakeProcessLauncher { RunsUntilCancelled = true };
        var client = new ForgeClient(processes, new OrkeonBinaryLocator(executables));
        var run = client.RunAsync(new ForgeStartRequest { Need = "pdf" }, _ => { }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(client.SendFolders(
        [
            model.ProposedFolders[0] with { Directory = "/data/pdf" },
            model.ProposedFolders[1] with { Path = "/markdown" },
        ]));
        model.AcknowledgeFolders();
        Assert.True(client.RequestCancellation());
        await run;

        Assert.False(model.FoldersPending);
        Assert.Equal(
            ["""{"kind":"folders.confirmed","folders":[{"path":"/inpdf","role":"input","purpose":"Les PDF \u00E0 convertir","dir":"/data/pdf"},{"path":"/markdown","role":"output","purpose":"Les fichiers Markdown"}]}"""],
            processes.InputLines);
        Assert.False(client.SendFolders([]));   // no child: said, not thrown

        // The engine answers with brief.ready carrying the confirmed list.
        model.Feed(Parse(BriefReady("""[{"path":"/inpdf","role":"input"},{"path":"/markdown","role":"output"}]""")));
        Assert.Equal(["/inpdf", "/markdown"], model.Folders.Select(f => f.Path));
    }

    [Fact]
    public void The_confirmed_folders_are_the_teams_mounts_with_who_reads_and_writes_them()
    {
        var model = new ForgeSessionModel();
        model.Feed(Parse(BriefReady("""[{"path":"/inpdf","role":"input"},{"path":"/outmd","role":"output"}]""")));
        model.Feed(Parse(Blueprint));

        Assert.Equal(["/inpdf", "/outmd"], model.DerivedMounts.Select(m => m.VirtualPath));
        Assert.False(model.DerivedMounts[0].IsReadWrite);
        Assert.Equal(["Reader"], model.DerivedMounts[0].Agents!);
        Assert.True(model.DerivedMounts[1].IsReadWrite);
        Assert.Equal(["Writer"], model.DerivedMounts[1].Agents!);

        // No /workspace, no /output: the canonical roots are no longer added by default.
        Assert.DoesNotContain(model.DerivedMounts, m => m.VirtualPath is "/workspace" or "/output");
    }

    [Fact]
    public void A_session_without_a_confirmed_list_keeps_what_the_plan_implies()
    {
        var model = new ForgeSessionModel();
        model.Feed(Parse(Blueprint));

        Assert.False(model.FoldersConfirmed);
        Assert.Equal(["/workspace", "/outmd"], model.DerivedMounts.Select(m => m.VirtualPath));
    }

    /// <summary>STUDIO-57: the user kept no folder — a confirmed empty list is a team with none, not a plan's guess.</summary>
    [Fact]
    public void A_confirmed_empty_list_is_a_team_with_no_folder()
    {
        var model = new ForgeSessionModel();
        model.Feed(Parse(BriefReady("[]")));
        model.Feed(Parse(Blueprint));

        Assert.True(model.FoldersConfirmed);
        Assert.Empty(model.Folders);
        Assert.Empty(model.DerivedMounts);

        model.Feed(Parse("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"x","dir":"/ws/.orkeon/forge/x","format":"yaml","resumed":false}"""));
        Assert.False(model.FoldersConfirmed);
    }

    [Fact]
    public void The_end_of_a_run_clears_a_pending_folders_step()
    {
        var model = new ForgeSessionModel();
        model.Feed(Parse(GoldenProposed));
        model.Feed(Parse("""{"v":2,"seq":2,"ts":"t","kind":"session.finished","status":"failed","exitCode":2}"""));

        Assert.False(model.FoldersPending);
    }

    [Fact]
    public void Hydration_brings_the_confirmed_folders_back_with_their_directories_and_drops_unconfirmed_ones()
    {
        File.WriteAllText(Path.Combine(_directory, "brief.json"),
            """{"goal":"Convertir","folders":[{"path":"/inpdf","role":"input"},{"path":"/outmd","role":"output"}],"acceptance":[]}""");

        var unconfirmed = new ForgeSessionModel();
        ForgeSessionHydrator.Hydrate(unconfirmed, _directory);
        Assert.Empty(unconfirmed.Folders);

        File.WriteAllText(Path.Combine(_directory, "folders.json"),
            """{"folders":[{"path":"/inpdf","role":"input","purpose":"PDF","dir":"/data/pdf"},{"path":"/outmd","role":"output"}]}""");

        var confirmed = new ForgeSessionModel();
        ForgeSessionHydrator.Hydrate(confirmed, _directory);
        Assert.Equal(["/inpdf", "/outmd"], confirmed.Folders.Select(f => f.Path));
        Assert.Equal("/data/pdf", confirmed.Folders[0].Directory);
        Assert.Null(confirmed.Folders[1].Directory);
    }

    /// <summary>
    /// STUDIO-57. The proposal says whether it is the defaults; <c>brief.ready</c> names the
    /// folders the session holds for the team, with their directories; a new session forgets both.
    /// </summary>
    [Fact]
    public void The_defaults_flag_and_the_held_folders_reach_the_model_and_a_new_session_forgets_them()
    {
        var model = new ForgeSessionModel();
        model.Feed(Parse(GoldenProposed));
        Assert.False(model.ProposedFoldersAreDefaults);

        model.Feed(Parse("""{"v":2,"seq":1,"ts":"t","kind":"folders.proposed","folders":[{"path":"/workspace","role":"input","purpose":"Ce que l'équipe lit."},{"path":"/output","role":"output","purpose":"Où l'équipe écrit ses résultats."}],"defaults":true}"""));
        Assert.True(model.ProposedFoldersAreDefaults);
        Assert.Empty(model.HeldFolders);

        model.Feed(Parse("""{"v":2,"seq":2,"ts":"t","kind":"brief.ready","brief":{"goal":"g","folders":[{"path":"/workspace","role":"input"},{"path":"/output","role":"output"}],"acceptance":[]},"heldFolders":[{"path":"/workspace","role":"input","dir":"/ws/.orkeon/forge/s/folders/workspace"},{"path":"/output","role":"output","dir":"/ws/.orkeon/forge/s/folders/output"}]}"""));
        Assert.Equal(["/workspace", "/output"], model.HeldFolders.Select(f => f.Path));
        Assert.Equal("/ws/.orkeon/forge/s/folders/workspace", model.HeldFolders[0].Directory);
        Assert.True(model.HeldFolders[0].IsInput);
        Assert.False(model.HeldFolders[1].IsInput);
        Assert.Equal(["/workspace", "/output"], model.Folders.Select(f => f.Path));

        // An older engine names no held folder: nothing to fill.
        model.Feed(Parse(BriefReady("""[{"path":"/inpdf","role":"input"},{"path":"/markdown","role":"output"}]""")));
        Assert.Empty(model.HeldFolders);

        model.Feed(Parse("""{"v":2,"seq":2,"ts":"t","kind":"brief.ready","brief":{"goal":"g","folders":[],"acceptance":[]},"heldFolders":[{"path":"/output","role":"output","dir":"/d"}]}"""));
        Assert.Single(model.HeldFolders);
        model.Feed(Parse("""{"v":2,"seq":1,"ts":"t","kind":"session.started","slug":"x","dir":"/ws/.orkeon/forge/x","format":"yaml","resumed":false}"""));
        Assert.Empty(model.HeldFolders);
        Assert.False(model.ProposedFoldersAreDefaults);
    }

    [Fact]
    public void A_folder_without_a_path_or_a_known_role_is_skipped()
    {
        var model = new ForgeSessionModel();
        model.Feed(Parse("""{"v":2,"seq":1,"ts":"t","kind":"folders.proposed","folders":[{"path":"/a","role":"write"},{"role":"input"},{"path":"/b","role":"output"},"x"]}"""));

        Assert.Equal("/b", Assert.Single(model.ProposedFolders).Path);
    }
}
