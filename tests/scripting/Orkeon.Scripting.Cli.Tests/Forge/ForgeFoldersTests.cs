using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// A bench that runs nothing but goes through the trial's real mount set: the host mounts
/// <see cref="ForgeCommand.BuildMountPlan"/> spells, entered through <see cref="ForgeTrialScope"/>
/// — then reads what the agents would read and writes the deliverable where they would write it.
/// </summary>
internal sealed class FolderBench(string workspace, string? readRoot, string read, string deliverable) : IForgeTestBench
{
    /// <summary>The virtual roots the trial could resolve, and what it could not.</summary>
    public List<string> Resolved { get; } = [];

    /// <summary>The roots the trial was refused.</summary>
    public List<string> Refused { get; } = [];

    /// <summary>What the bench read through the input folder.</summary>
    public string? ReadText { get; private set; }

    /// <inheritdoc />
    public async Task<ForgeTestRun> ExecuteAsync(ForgeSession session, int runNumber, CancellationToken cancellationToken)
    {
        var plan = ForgeCommand.BuildMountPlan(workspace, readRoot, session);
        using var boot = new FileSystemRegistry(plan.CliMounts.Select(FileSystemMount.Parse));
        var mounts = ForgeFolders.TrialMounts(session, ForgeFolders.Of(session), workspace, readRoot);
        using var trial = ForgeTrialScope.Compose(boot, mounts);

        foreach (var root in new[] { "/inpdf", "/outmd", "/workspace", "/output", "/forge" })
        {
            try
            {
                trial.ResolveAndCheckRights(root + "/probe", FileAccessRights.Read);
                Resolved.Add(root);
            }
            catch (FileAccessDeniedException)
            {
                Refused.Add(root);
            }
        }

        ReadText = await File.ReadAllTextAsync(trial.ResolveAndCheckRights(read, FileAccessRights.Read), cancellationToken);
        var written = trial.ResolveAndCheckRights(deliverable, FileAccessRights.Write);
        await File.WriteAllTextAsync(written, "# Synthèse\n", cancellationToken);

        return new ForgeTestRun
        {
            Run = runNumber,
            Success = true,
            Output = "Synthèse écrite.",
            TaskCount = 1,
        };
    }
}

/// <summary>
/// The folders step (STUDIO-46): the folders the request names — or the defaults — proposed
/// between the brief and the plan, confirmed by the user, then the one list the plan, the
/// trial, the deliverable check and the adopted team use.
/// </summary>
public sealed class ForgeFoldersTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-folders-" + Guid.NewGuid().ToString("N"));

    private readonly StringWriter _output = new();

    public ForgeFoldersTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        _output.Dispose();
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    /// <summary>The fiche's example: PDFs in, Markdown out, two folders named by the request.</summary>
    private const string PdfBrief = """
        {
          "goal": "Convertir des PDF en Markdown",
          "inputs": [ { "name": "pdf", "description": "Les PDF à convertir", "example": "facture.pdf" } ],
          "expectedOutput": { "format": "markdown", "description": "Un .md par PDF" },
          "folders": [
            { "path": "/inpdf", "role": "input", "purpose": "Les PDF à convertir" },
            { "path": "/outmd", "role": "output", "purpose": "Les fichiers Markdown produits" }
          ],
          "acceptance": [ { "id": "A1", "statement": "Chaque PDF a son .md", "kind": "must" } ],
          "language": "fr"
        }
        """;

    private const string PdfBlueprint = """
        {
          "crew": { "name": "pdf-vers-md", "goal": "Convertir les PDF", "process": "sequential" },
          "agents": [
            { "key": "lecteur", "role": "Reader", "goal": "Lire les PDF", "tools": [ "file_read" ] },
            { "key": "redacteur", "role": "Writer", "goal": "Écrire le Markdown", "tools": [ "file_write" ] }
          ],
          "tasks": [
            { "key": "lire", "description": "Lire les PDF de /inpdf", "expectedOutput": "Le texte", "agent": "lecteur" },
            { "key": "ecrire", "description": "Écrire le Markdown", "expectedOutput": "Le .md", "agent": "redacteur",
              "dependencies": [ "lire" ], "deliverable": "/outmd/synthese.md" }
          ]
        }
        """;

    private ForgeEngine Engine(ForgeSession session, params IForgeStageRunner[] runners) =>
        new(session, new ForgeEventWriter(_output, new FakeOrkeonClock()), runners, new FakeOrkeonClock());

    private IReadOnlyList<JsonElement> Events() =>
    [
        .. _output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonElement.Parse(line)),
    ];

    private JsonElement Event(string kind) => Events().Single(e => e.GetProperty("kind").GetString() == kind);

    private static ForgeBrief Brief(string json)
    {
        Assert.True(ForgeBrief.TryParse(json, out var brief, out var errors), string.Join(" ", errors));
        return brief!;
    }

    private static ForgeFolder Folder(string path, string role, string? dir = null) =>
        new() { Path = path, Role = role, Purpose = "p", Directory = dir };

    // ── the fiche's example, end to end ──────────────────────────────────────────

    [Fact]
    public async Task The_inpdf_outmd_request_proposes_both_folders_tries_on_them_and_adopts_them()
    {
        // The user's real folder of PDFs, bound behind /inpdf when the folders are confirmed.
        var pdfs = Directory.CreateDirectory(Path.Combine(_workspace, "mes-pdf")).FullName;
        await File.WriteAllTextAsync(Path.Combine(pdfs, "facture.pdf"), "%PDF facture", TestContext.Current.CancellationToken);

        var session = ForgeSession.Create(_workspace, "pdf");
        var assistant = new ScriptedAssistant().SubmitsBrief(PdfBrief).SubmitsBlueprint(PdfBlueprint);
        var channel = new ScriptedUserChannel().ConfirmsFolders(
        [
            Folder("/inpdf", "input", pdfs),
            Folder("/outmd", "output"),
        ]);
        var bench = new FolderBench(_workspace, readRoot: null, read: "/inpdf/facture.pdf", deliverable: "/outmd/synthese.md");

        var result = await Engine(session,
            new BriefStage(assistant, channel),
            new BlueprintStage(assistant),
            new RenderStage(),
            new ValidateStage(["file_read", "file_write"]),
            new TestStage(bench),
            new DiagnoseStage(new FakeJudge().Approves()),
            new VerdictStage(auto: true, channel, ["file_read", "file_write"]))
            .RunAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ForgeEngineOutcome.Ready, result.Outcome);

        // Proposed: the two folders the request named, nothing else.
        var proposed = Assert.Single(channel.ProposedFolders);
        Assert.Equal(["/inpdf", "/outmd"], proposed.Select(f => f.Path));
        Assert.Equal(["input", "output"], proposed.Select(f => f.Role));
        var wire = Event("folders.proposed").GetProperty("folders");
        Assert.Equal(["/inpdf", "/outmd"], wire.EnumerateArray().Select(f => f.GetProperty("path").GetString()));

        // Read from the request, not the defaults (STUDIO-57).
        Assert.False(Event("folders.proposed").GetProperty("defaults").GetBoolean());

        // Confirmed: the brief the plan reads carries them — never the physical path.
        var briefFolders = Event("brief.ready").GetProperty("brief").GetProperty("folders");
        Assert.Equal(["/inpdf", "/outmd"], briefFolders.EnumerateArray().Select(f => f.GetProperty("path").GetString()));
        Assert.DoesNotContain(pdfs, Event("brief.ready").GetRawText(), StringComparison.Ordinal);

        // Held: the one folder kept inside the team, created now so the user can fill it (STUDIO-57).
        var held = Assert.Single(Event("brief.ready").GetProperty("heldFolders").EnumerateArray());
        Assert.Equal("/outmd", held.GetProperty("path").GetString());
        Assert.Equal(Path.Combine(session.Directory, "folders", "outmd"), held.GetProperty("dir").GetString());
        Assert.Equal(pdfs, ForgeFolders.Confirmed(session)![0].Directory);
        var blueprintRequest = assistant.Requests.Single(r => r.Phase == ForgeAssistantPhase.Blueprint);
        Assert.NotNull(blueprintRequest.Brief?.Folders);
        Assert.Equal(["/inpdf", "/outmd"], blueprintRequest.Brief.Folders.Select(f => f.Path));

        // The crew names the two roots it expects, and only those.
        var config = await File.ReadAllTextAsync(Path.Combine(session.Directory, ForgeYamlRenderer.CrewDirectoryName, "config.yaml"), TestContext.Current.CancellationToken);
        Assert.Contains("/inpdf", config, StringComparison.Ordinal);
        Assert.Contains("/outmd", config, StringComparison.Ordinal);
        Assert.DoesNotContain("/output", config, StringComparison.Ordinal);
        Assert.DoesNotContain("/workspace", config, StringComparison.Ordinal);

        // The trial read the bound folder and wrote into its own /outmd; the canonical roots
        // were not there.
        Assert.Equal("%PDF facture", bench.ReadText);
        Assert.Contains("/inpdf", bench.Resolved);
        Assert.Contains("/outmd", bench.Resolved);
        Assert.Contains("/forge", bench.Resolved);
        Assert.Contains("/workspace", bench.Refused);
        Assert.Contains("/output", bench.Refused);

        // The deliverable was found where /outmd landed, so no mechanical finding.
        Assert.True(File.Exists(Path.Combine(session.Directory, "runs", "1", "folders", "outmd", "synthese.md")));
        var verdict = session.TryLoadArtifact<ForgeVerdict>(ForgeSession.VerdictFileName)!;
        Assert.DoesNotContain(verdict.Findings, f => (f.Id ?? "").StartsWith("F-DELIVERABLE", StringComparison.Ordinal));

        // Adopted: the team has both folders, and its launcher mounts both — nothing else.
        var destination = Path.Combine(_workspace, "teams", "pdf-vers-md");
        var promoted = ForgePromoter.Promote(
            session, destination, schedule: null, settingsPath: null, copySettings: false, ForgePromotePlatform.Linux, Now);
        Assert.True(Directory.Exists(Path.Combine(destination, "inpdf")));
        Assert.True(Directory.Exists(Path.Combine(destination, "outmd")));
        var launcher = await File.ReadAllTextAsync(Path.Combine(destination, promoted.Launcher), TestContext.Current.CancellationToken);
        Assert.Contains(":/inpdf:ro", launcher, StringComparison.Ordinal);
        Assert.Contains(":/outmd:rw", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain(":/output:", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain(":/workspace:", launcher, StringComparison.Ordinal);
    }

    // ── the proposal ─────────────────────────────────────────────────────────────

    [Fact]
    public void A_brief_naming_inpdf_and_outmd_proposes_those_two_folders()
    {
        var proposal = ForgeFolders.ProposalOf(Brief(PdfBrief));

        Assert.Equal(["/inpdf", "/outmd"], proposal.Select(f => f.Path));
        Assert.Equal(["input", "output"], proposal.Select(f => f.Role));
    }

    [Fact]
    public void A_brief_naming_no_folder_proposes_the_defaults()
    {
        // The assistant did not say whether files are read: something comes in, so a folder to read.
        var reading = ForgeFolders.ProposalOf(Brief(ForgeDocuments.ValidBrief));
        Assert.Equal(["/workspace", "/output"], reading.Select(f => f.Path));
        Assert.Equal(["input", "output"], reading.Select(f => f.Role));
        Assert.True(ForgeFolders.IsDefaultProposal(Brief(ForgeDocuments.ValidBrief)));
        Assert.False(ForgeFolders.IsDefaultProposal(Brief(PdfBrief)));

        // Nothing comes in: nothing to read, so no /workspace.
        var writing = ForgeFolders.ProposalOf(Brief("""
            { "goal": "Écrire un poème chaque matin", "acceptance": [ { "id": "A1", "statement": "Un poème", "kind": "must" } ] }
            """));
        Assert.Equal(["/output"], writing.Select(f => f.Path));
    }

    /// <summary>
    /// STUDIO-57. A URL comes in, a Markdown summary comes out: the team reads no folder, and
    /// a « /workspace » to read answered a question nobody asked. <c>readsFiles</c> decides:
    /// false drops the folder to read even with an input, true proposes it even with none.
    /// </summary>
    [Fact]
    public void Reads_files_decides_whether_a_folder_to_read_is_proposed_at_all()
    {
        var url = Brief(ForgeDocuments.ValidBrief.Replace("\"language\": \"fr\"", "\"readsFiles\": false, \"language\": \"fr\"", StringComparison.Ordinal));
        Assert.Equal(["/output"], ForgeFolders.ProposalOf(url).Select(f => f.Path));

        var files = Brief("""
            { "goal": "Convertir les PDF qu'on me donne", "readsFiles": true, "acceptance": [ { "id": "A1", "statement": "Un .md", "kind": "must" } ] }
            """);
        Assert.Equal(["/workspace", "/output"], ForgeFolders.ProposalOf(files).Select(f => f.Path));
    }

    /// <summary>STUDIO-57. A default's purpose is said in the brief's language, not always in English.</summary>
    [Fact]
    public void A_default_folder_says_its_purpose_in_the_briefs_language()
    {
        var french = ForgeFolders.ProposalOf(Brief(ForgeDocuments.ValidBrief));
        Assert.Equal(["Ce que l'équipe lit.", "Où l'équipe écrit ses résultats."], french.Select(f => f.Purpose));

        var english = ForgeFolders.ProposalOf(Brief(ForgeDocuments.ValidBrief.Replace("\"language\": \"fr\"", "\"language\": \"en\"", StringComparison.Ordinal)));
        Assert.Equal(["What the team reads.", "Where the team writes its results."], english.Select(f => f.Purpose));
    }

    /// <summary>
    /// STUDIO-57. The proposal says it is the defaults, and once the list is confirmed the
    /// session holds every folder kept inside the team: each directory exists — an explorer
    /// can open it, the user can drop files in it before the trial — and <c>brief.ready</c>
    /// names it.
    /// </summary>
    [Fact]
    public async Task The_defaults_are_said_as_such_and_the_held_folders_are_created_and_announced()
    {
        var session = ForgeSession.Create(_workspace, "held");

        await Engine(session, new BriefStage(new ScriptedAssistant().SubmitsBrief(ForgeDocuments.ValidBrief), new ScriptedUserChannel()))
            .RunAsync(stopBefore: ForgeState.Blueprint, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(Event("folders.proposed").GetProperty("defaults").GetBoolean());

        var held = Event("brief.ready").GetProperty("heldFolders").EnumerateArray().ToList();
        Assert.Equal(["/workspace", "/output"], held.Select(h => h.GetProperty("path").GetString()));
        Assert.Equal(["input", "output"], held.Select(h => h.GetProperty("role").GetString()));
        Assert.Equal(Path.Combine(session.Directory, "folders", "workspace"), held[0].GetProperty("dir").GetString());
        Assert.Equal(Path.Combine(session.Directory, "folders", "output"), held[1].GetProperty("dir").GetString());
        Assert.All(held, h => Assert.True(Directory.Exists(h.GetProperty("dir").GetString())));

        // The directories stay out of the brief: a physical path never reaches a prompt.
        Assert.DoesNotContain(session.Directory, Event("brief.ready").GetProperty("brief").GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/forge")]
    [InlineData("/credentials")]
    [InlineData("/sandbox")]
    public void A_reserved_root_is_refused_in_the_brief(string reserved)
    {
        var json = PdfBrief.Replace("/inpdf", reserved, StringComparison.Ordinal);

        Assert.False(ForgeBrief.TryParse(json, out _, out var errors));
        Assert.Contains(errors, e => e.Contains($"'{reserved}' is reserved", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("inpdf")]
    [InlineData("/in/pdf")]
    [InlineData("/in pdf")]
    [InlineData("/in:pdf")]
    [InlineData("/..")]
    public void A_folder_that_is_not_one_absolute_segment_is_refused(string path)
    {
        var errors = ForgeFolders.Validate([Folder(path, "input"), Folder("/outmd", "output")]);

        Assert.Contains(errors, e => e.Contains("one absolute segment", StringComparison.Ordinal));
    }

    /// <summary>
    /// STUDIO-57: a list with no output, or no folder at all, is a valid one — a team that sends
    /// mails from what it reads writes no file. What is still refused: a root twice, an unknown role.
    /// </summary>
    [Fact]
    public void A_list_without_an_output_is_valid_and_a_root_twice_is_refused()
    {
        Assert.Empty(ForgeFolders.Validate([Folder("/inpdf", "input")]));
        Assert.Empty(ForgeFolders.Validate([]));
        Assert.Empty(ForgeFolders.Validate(null));
        Assert.Contains(
            ForgeFolders.Validate([Folder("/outmd", "output"), Folder("/OUTMD", "output")]),
            e => e.Contains("listed twice", StringComparison.Ordinal));
        Assert.Contains(
            ForgeFolders.Validate([Folder("/outmd", "write")]),
            e => e.Contains("'role' must be 'input' or 'output'", StringComparison.Ordinal));
    }

    // ── the confirmation ─────────────────────────────────────────────────────────

    /// <summary>STUDIO-57: the user kept no folder at all — the confirmed empty list is the team's, not the plan's guess.</summary>
    [Fact]
    public async Task A_confirmed_empty_list_is_honoured_over_what_the_plan_implies()
    {
        var session = ForgeSession.Create(_workspace, "mails");
        var channel = new ScriptedUserChannel().ConfirmsFolders([]);

        await Engine(session, new BriefStage(new ScriptedAssistant().SubmitsBrief(ForgeDocuments.ValidBrief), channel))
            .RunAsync(stopBefore: ForgeState.Blueprint, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(ForgeFolders.Confirmed(session)!);
        Assert.Empty(ForgeFolders.Of(session));
        Assert.Empty(Event("brief.ready").GetProperty("heldFolders").EnumerateArray());
        Assert.Empty(ForgeFolders.TrialMounts(session, ForgeFolders.Of(session), _workspace, null));
    }

    [Fact]
    public async Task A_renamed_folder_is_the_one_the_brief_keeps()
    {
        var session = ForgeSession.Create(_workspace, "renamed");
        var channel = new ScriptedUserChannel().ConfirmsFolders([Folder("/pdfs", "input"), Folder("/markdown", "output")]);

        await Engine(session, new BriefStage(new ScriptedAssistant().SubmitsBrief(PdfBrief), channel))
            .RunAsync(stopBefore: ForgeState.Blueprint, cancellationToken: TestContext.Current.CancellationToken);

        var brief = session.TryLoadArtifact<ForgeBrief>(ForgeSession.BriefFileName)!;
        Assert.Equal(["/pdfs", "/markdown"], brief.Folders!.Select(f => f.Path));
        Assert.Equal(["/pdfs", "/markdown"], ForgeFolders.Of(session).Select(f => f.Path));
    }

    [Fact]
    public async Task An_invalid_confirmation_is_said_and_the_folders_are_proposed_again()
    {
        var session = ForgeSession.Create(_workspace, "invalid");
        var channel = new ScriptedUserChannel()
            .ConfirmsFolders([Folder("/forge", "output")])
            .ConfirmsFolders([Folder("/inpdf", "input", Path.Combine(_workspace, "absent")), Folder("/outmd", "output")])
            .ConfirmsFolders([Folder("/outmd", "output")]);

        await Engine(session, new BriefStage(new ScriptedAssistant().SubmitsBrief(PdfBrief), channel))
            .RunAsync(stopBefore: ForgeState.Blueprint, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, channel.ProposedFolders.Count);
        var errors = Events().Where(e => e.GetProperty("kind").GetString() == "error").ToList();
        Assert.Equal(2, errors.Count);
        Assert.All(errors, e => Assert.Equal("FORGE-FOLDERS-INVALID", e.GetProperty("code").GetString()));
        Assert.All(errors, e => Assert.True(e.GetProperty("recoverable").GetBoolean()));
        Assert.Contains("does not exist", errors[1].GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(["/outmd"], ForgeFolders.Confirmed(session)!.Select(f => f.Path));
    }

    [Fact]
    public async Task A_folder_bound_outside_the_working_directory_is_refused_when_this_run_tries_the_team()
    {
        // GAP-27: the trial of a run that does not stop before it (no --dry) runs in this same
        // process, whose path validator was built before the folders were confirmed: a bound
        // directory it refuses would be mounted for the trial and then refused on every read.
        // The confirmation is refused instead, recoverable, with the way that works.
        var workingDirectory = Directory.CreateDirectory(Path.Combine(_workspace, "cwd")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(_workspace, "elsewhere", "pdfs")).FullName;
        var inside = Directory.CreateDirectory(Path.Combine(workingDirectory, "pdfs")).FullName;
        var validator = new Orkeon.Infrastructure.Security.PathValidator(
            new Orkeon.Infrastructure.Configuration.PathSecurityOptions { DefaultWorkspaceRoot = workingDirectory },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Orkeon.Infrastructure.Security.PathValidator>.Instance);
        var session = ForgeSession.Create(_workspace, "outside");
        var channel = new ScriptedUserChannel()
            .ConfirmsFolders([Folder("/inpdf", "input", outside), Folder("/outmd", "output")])
            .ConfirmsFolders([Folder("/inpdf", "input", inside), Folder("/outmd", "output")]);

        await Engine(session, new BriefStage(new ScriptedAssistant().SubmitsBrief(PdfBrief), channel, trialPathValidator: validator))
            .RunAsync(stopBefore: ForgeState.Blueprint, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, channel.ProposedFolders.Count);
        var error = Assert.Single(Events(), e => e.GetProperty("kind").GetString() == "error");
        Assert.Equal("FORGE-FOLDERS-INVALID", error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("recoverable").GetBoolean());
        var message = error.GetProperty("message").GetString()!;
        Assert.Contains("'/inpdf'", message, StringComparison.Ordinal);
        Assert.Contains($"orkeon forge resume {session.Document.Slug} --dry", message, StringComparison.Ordinal);
        Assert.Contains($"orkeon forge resume {session.Document.Slug}", message, StringComparison.Ordinal);
        // A physical path never reaches the stream (the brief's rule).
        Assert.DoesNotContain(outside, message, StringComparison.Ordinal);
        Assert.Equal(inside, ForgeFolders.Confirmed(session)![0].Directory);
    }

    [Fact]
    public async Task A_dry_run_confirms_a_folder_outside_the_working_directory_its_trial_comes_later()
    {
        // Studio's path: the folders are confirmed in a run that stops before its trial, which
        // a resume then runs in a process that mounts and allows them from its start.
        var outside = Directory.CreateDirectory(Path.Combine(_workspace, "elsewhere", "pdfs")).FullName;
        var session = ForgeSession.Create(_workspace, "dry");
        var channel = new ScriptedUserChannel().ConfirmsFolders([Folder("/inpdf", "input", outside), Folder("/outmd", "output")]);

        await Engine(session, new BriefStage(new ScriptedAssistant().SubmitsBrief(PdfBrief), channel))
            .RunAsync(stopBefore: ForgeState.Blueprint, cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain(Events(), e => e.GetProperty("kind").GetString() == "error");
        Assert.Equal(outside, ForgeFolders.Confirmed(session)![0].Directory);
    }

    [Fact]
    public async Task A_session_stopped_at_the_folders_resumes_there_without_a_new_interview()
    {
        var session = ForgeSession.Create(_workspace, "stopped");
        var closed = new ScriptedUserChannel().ConfirmsFolders(null);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Engine(session, new BriefStage(new ScriptedAssistant().SubmitsBrief(PdfBrief), closed))
                .RunAsync(stopBefore: ForgeState.Blueprint, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Null(ForgeFolders.Confirmed(session));

        var silent = new ScriptedAssistant();
        var channel = new ScriptedUserChannel();
        Assert.True(ForgeSession.TryLoad(session.Directory, out var resumed, out _));
        await Engine(resumed!, new BriefStage(silent, channel)).RunAsync(resumed: true, stopBefore: ForgeState.Blueprint, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(silent.Requests);
        Assert.Single(channel.ProposedFolders);
        Assert.Equal(["/inpdf", "/outmd"], ForgeFolders.Confirmed(resumed!)!.Select(f => f.Path));
    }

    [Fact]
    public async Task Auto_takes_the_proposal_as_it_is()
    {
        var session = ForgeSession.Create(_workspace, "auto");
        var channel = new ScriptedUserChannel().ConfirmsFolders(null);

        await Engine(session, new BriefStage(new ScriptedAssistant().SubmitsBrief(PdfBrief), channel, autoConfirmFolders: true))
            .RunAsync(stopBefore: ForgeState.Blueprint, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(channel.ProposedFolders);
        Assert.Equal(["/inpdf", "/outmd"], ForgeFolders.Confirmed(session)!.Select(f => f.Path));
    }

    [Fact]
    public async Task The_jsonl_channel_reads_folders_confirmed_and_skips_everything_else()
    {
        var input = new StringReader(
            """
            {"kind":"user.message","text":"hello"}
            not json
            {"kind":"folders.confirmed","folders":[{"path":"/inpdf","role":"input","purpose":"PDF","dir":"/data/pdf"},{"path":"/outmd","role":"output"}]}
            """);

        var folders = await new JsonLinesUserChannel(input).ReadFoldersAsync([], TestContext.Current.CancellationToken);

        Assert.NotNull(folders);
        Assert.Equal(["/inpdf", "/outmd"], folders.Select(f => f.Path));
        Assert.Equal("/data/pdf", folders[0].Directory);
        Assert.Null(folders[1].Directory);
    }

    [Fact]
    public async Task The_terminal_accepts_the_proposal_and_prints_it()
    {
        var console = new StringWriter();
        var proposal = new[] { Folder("/inpdf", "input"), Folder("/outmd", "output") };

        var accepted = await new TerminalUserChannel(new StringReader(""), console).ReadFoldersAsync(proposal, TestContext.Current.CancellationToken);
        using var renderer = new ForgeTerminalRenderer(console);
        new ForgeEventWriter(renderer, new FakeOrkeonClock()).Emit("folders.proposed", new { folders = proposal });

        Assert.Same(proposal, accepted);
        var text = console.ToString();
        Assert.Contains("accepted as proposed", text, StringComparison.Ordinal);
        Assert.Contains("/inpdf (input) — p", text, StringComparison.Ordinal);
        Assert.Contains("/outmd (output) — p", text, StringComparison.Ordinal);
    }

    // ── what the list is used for ────────────────────────────────────────────────

    [Fact]
    public void The_trial_bench_mounts_inpdf_and_outmd_and_not_the_canonical_roots()
    {
        var session = ForgeSession.Create(_workspace, "bench");
        var pdfs = Directory.CreateDirectory(Path.Combine(_workspace, "pdfs")).FullName;
        session.SaveArtifact(ForgeFolders.FileName, new ForgeFolderList
        {
            Folders = [Folder("/inpdf", "input", pdfs), Folder("/outmd", "output")],
        });

        var plan = ForgeCommand.BuildMountPlan(_workspace, null, session);
        Assert.Contains(plan.CliMounts, m => m.EndsWith(":/inpdf:ro", StringComparison.Ordinal) && m.Contains(pdfs, StringComparison.Ordinal));
        Assert.Contains(plan.CliMounts, m => m.EndsWith(":/outmd:rw", StringComparison.Ordinal)
            && m.Contains(Path.Combine(session.Directory, "folders", "outmd"), StringComparison.Ordinal));
        Assert.True(Directory.Exists(Path.Combine(session.Directory, "folders", "outmd")));

        using var boot = new FileSystemRegistry(plan.CliMounts.Select(FileSystemMount.Parse));
        using var trial = ForgeTrialScope.Compose(
            boot, ForgeFolders.TrialMounts(session, ForgeFolders.Of(session), _workspace, null));
        Assert.Equal(
            ["/forge", "/inpdf", "/outmd"],
            trial.GetMounts().Select(m => m.VirtualPath).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void An_unbound_input_reads_read_then_the_session_folder_inside_the_team()
    {
        var session = ForgeSession.Create(_workspace, "unbound");
        var documents = Directory.CreateDirectory(Path.Combine(_workspace, "docs")).FullName;
        IReadOnlyList<ForgeFolder> folders = [Folder("/inpdf", "input"), Folder("/notes", "input"), Folder("/outmd", "output")];

        var mounts = ForgeFolders.TrialMounts(session, folders, _workspace, documents);

        Assert.Equal(documents, mounts[0].PhysicalPath);
        Assert.Equal(Path.Combine(session.Directory, "folders", "notes"), mounts[1].PhysicalPath);
        Assert.True(Directory.Exists(mounts[1].PhysicalPath));

        // A session that never confirmed a list — rebuilt from a team folder — keeps reading
        // the workspace behind /workspace, as it did before the list existed.
        var defaults = ForgeFolders.TrialMounts(session, [Folder("/workspace", "input"), Folder("/output", "output")], _workspace, null);
        Assert.Equal(_workspace, defaults[0].PhysicalPath);
        Assert.Equal(Path.Combine(session.Directory, "folders", "output"), defaults[1].PhysicalPath);

        // Once a list is confirmed, a /workspace kept inside the team reads the session's own
        // folder — what the user dropped there (STUDIO-57) — like every other such input.
        session.SaveArtifact(ForgeFolders.FileName, new ForgeFolderList { Folders = [Folder("/workspace", "input"), Folder("/output", "output")] });
        var confirmed = ForgeFolders.TrialMounts(session, ForgeFolders.Of(session), _workspace, null);
        Assert.Equal(Path.Combine(session.Directory, "folders", "workspace"), confirmed[0].PhysicalPath);
        Assert.True(Directory.Exists(confirmed[0].PhysicalPath));
        Assert.Equal(documents, ForgeFolders.TrialMounts(session, ForgeFolders.Of(session), _workspace, documents)[0].PhysicalPath);
    }

    [Fact]
    public void A_deliverable_under_a_confirmed_output_is_accepted_and_one_elsewhere_is_refused()
    {
        Assert.True(ForgeBlueprint.TryParse(PdfBlueprint, out var blueprint, out _));
        IReadOnlyList<ForgeFolder> folders = [Folder("/inpdf", "input"), Folder("/outmd", "output")];

        var accepted = ForgeBlueprintCompiler.Validate(
            ForgeBlueprintCompiler.Compile(blueprint!, folders), ["file_read", "file_write"], folders);
        Assert.Empty(accepted.Errors);

        var elsewhere = blueprint! with
        {
            Tasks = [.. blueprint.Tasks!.Select(t => t.Deliverable is null ? t : t with { Deliverable = "/output/synthese.md" })],
        };
        var refused = ForgeBlueprintCompiler.Validate(
            ForgeBlueprintCompiler.Compile(elsewhere, folders), ["file_read", "file_write"], folders);
        var error = Assert.Single(refused.Errors);
        Assert.Contains("'/output/synthese.md'", error, StringComparison.Ordinal);
        Assert.Contains("/outmd", error, StringComparison.Ordinal);

        // Delivering into an input folder is no output either.
        var intoInput = blueprint with
        {
            Tasks = [.. blueprint.Tasks!.Select(t => t.Deliverable is null ? t : t with { Deliverable = "/inpdf/x.md" })],
        };
        Assert.NotEmpty(ForgeBlueprintCompiler.Validate(
            ForgeBlueprintCompiler.Compile(intoInput, folders), ["file_read", "file_write"], folders).Errors);
    }

    [Fact]
    public void Promotion_mounts_the_confirmed_list_and_moves_an_in_team_folder_into_the_team()
    {
        var session = ForgeSession.Create(_workspace, "promote");
        Assert.True(ForgeBrief.TryParse(PdfBrief, out var brief, out _));
        session.SaveArtifact(ForgeSession.BriefFileName, brief!);
        Assert.True(ForgeBlueprint.TryParse(PdfBlueprint, out var blueprint, out _));
        session.SaveArtifact(ForgeSession.BlueprintFileName, blueprint!);
        IReadOnlyList<ForgeFolder> folders = [Folder("/inpdf", "input"), Folder("/outmd", "output")];
        session.SaveArtifact(ForgeFolders.FileName, new ForgeFolderList { Folders = folders });
        ForgeYamlRenderer.Render(ForgeBlueprintCompiler.Compile(blueprint!, folders), session.Directory);

        // What the user dropped into the in-team input during the trial.
        var held = ForgeFolders.SessionFolder(session, folders[0]);
        Directory.CreateDirectory(held);
        File.WriteAllText(Path.Combine(held, "facture.pdf"), "%PDF");

        session.SetState(ForgeState.Ready);
        session.SetStatus(ForgeSessionStatus.Ready);
        session.Save(Now);

        var destination = Path.Combine(_workspace, "teams", "pdf");
        var result = ForgePromoter.Promote(
            session, destination, schedule: null, settingsPath: null, copySettings: false, ForgePromotePlatform.Windows, Now);

        Assert.Equal("%PDF", File.ReadAllText(Path.Combine(destination, "inpdf", "facture.pdf")));
        Assert.False(Directory.Exists(held));
        var launcher = File.ReadAllText(Path.Combine(destination, result.Launcher));
        Assert.Contains("inpdf\\\":/inpdf:ro", launcher, StringComparison.Ordinal);
        Assert.Contains("outmd\\\":/outmd:rw", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain(":/workspace:", launcher, StringComparison.Ordinal);
        var card = File.ReadAllText(Path.Combine(destination, "FORGE.md"));
        Assert.Contains("`inpdf/`", card, StringComparison.Ordinal);
    }
}

/// <summary>
/// GAP-27, the host half: a folder bound to a directory outside the working directory is readable
/// by the trial of <c>forge resume</c>, whose engine host mounts the confirmed folders and allows
/// their directories when it is built — and not by a host built before the confirmation, whose
/// path validator is fixed at its start (the reason the confirmation is refused when the trial
/// would run in that same process). The hosts are built the way the verb builds them — its mount
/// plan and <see cref="ForgeCommand.AddEngineServices"/> — and never run.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ForgeFoldersResumeHostTests : IDisposable
{
    private readonly string _workspace =
        Path.Combine(Path.GetTempPath(), "orkeon-forge-folders-resume-" + Guid.NewGuid().ToString("N"));

    private readonly string _settings;

    public ForgeFoldersResumeHostTests()
    {
        Directory.CreateDirectory(_workspace);
        _settings = Path.Combine(_workspace, "appsettings.json");
        // Disabling RaggableTree keeps the on-device embedding model (ONNX) out of the host.
        File.WriteAllText(_settings, "{ \"RaggableTree\": { \"Enabled\": false } }");
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    private static ForgeFolder Folder(string path, string role, string? dir = null) =>
        new() { Path = path, Role = role, Purpose = "p", Directory = dir };

    private Microsoft.Extensions.Hosting.IHost BuildEngineHost(ForgeSession session)
    {
        Directory.CreateDirectory(Path.Combine(session.Directory, TestStage.OutputDirectoryName));
        var plan = ForgeCommand.BuildMountPlan(_workspace, readRoot: null, session);
        return Orkeon.Hosting.RunnerHost.Build(
            _settings,
            plan,
            configureServices: (_, services) => ForgeCommand.AddEngineServices(
                services, new ForgeSubmissionBox(), new ForgeUsageTally(), new ForgeRunObserver()));
    }

    /// <summary>A PDF folder outside the process working directory: the workspace is a temp directory.</summary>
    private string OutsideFolder()
    {
        var pdfs = Directory.CreateDirectory(Path.Combine(_workspace, "mes-pdf")).FullName;
        File.WriteAllText(Path.Combine(pdfs, "facture.pdf"), "%PDF facture");
        Assert.False(
            Orkeon.Domain.FileSystem.PhysicalPathContainment.IsUnder(pdfs, Directory.GetCurrentDirectory()),
            "The fixture's folder must lie outside the working directory.");
        return pdfs;
    }

    [Fact]
    public async Task Under_forge_resume_the_folder_is_mounted_and_readable()
    {
        var pdfs = OutsideFolder();
        var session = ForgeSession.Create(_workspace, "resumed");
        session.SaveArtifact(ForgeFolders.FileName, new ForgeFolderList
        {
            Folders = [Folder("/inpdf", "input", pdfs), Folder("/outmd", "output")],
        });

        using var host = BuildEngineHost(session);
        using var trial = ForgeTrialScope.Enter(host.Services, session, _workspace, readRoot: null);
        var fileSystem = host.Services.GetRequiredService<IFileSystemService>();

        Assert.Equal("%PDF facture", await fileSystem.TryReadAllTextAsync("/inpdf/facture.pdf", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_folder_confirmed_after_the_host_was_built_is_mounted_but_refused()
    {
        var pdfs = OutsideFolder();
        var session = ForgeSession.Create(_workspace, "same-process");
        using var host = BuildEngineHost(session);

        // Confirmed in the run that built the host: the trial scope mounts it, the validator
        // the host started with does not allow it.
        session.SaveArtifact(ForgeFolders.FileName, new ForgeFolderList
        {
            Folders = [Folder("/inpdf", "input", pdfs), Folder("/outmd", "output")],
        });
        using var trial = ForgeTrialScope.Enter(host.Services, session, _workspace, readRoot: null);
        var fileSystem = host.Services.GetRequiredService<IFileSystemService>();

        Assert.Contains("/inpdf", fileSystem.GetAvailableMounts().Select(m => m.VirtualPath));
        await Assert.ThrowsAsync<FileAccessDeniedException>(
            () => fileSystem.TryReadAllTextAsync("/inpdf/facture.pdf", TestContext.Current.CancellationToken));
    }
}
