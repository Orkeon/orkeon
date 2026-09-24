using System.Text.Json;
using Orkeon.Scripting.Cli.Commands.Forge;
using Orkeon.Scripting.Cli.Commands.UseCases;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// STUDIO-40 through the command: <c>forge --reference &lt;id&gt;</c> refused before anything when
/// the id names no use case (D-05), recorded in the session (D-01), handed to the composer — on a
/// resume too — and traced by the promotion in <c>FORGE.md</c> and <c>forge.json</c> (D-04).
/// Offline: where a cycle runs, a probe pack stands in for the composer and calls no model.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class ForgeReferenceCommandTests : IDisposable
{
    private const string EmailPipeline = "03-email-pipeline";

    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);

    /// <summary>What <c>--reference 03-email-pipeline</c> records at creation: the English title, no brief yet.</summary>
    private static readonly ForgeReferenceRecord Recorded = new() { Id = EmailPipeline, Title = "Email triage and replies" };

    /// <summary>
    /// A pack whose assistant answers every turn with the reference the engine handed it —
    /// <c>null</c> when none — and never calls a model.
    /// </summary>
    private const string ProbeAssistant = """
        /// <reference orkeon-script="1.0" />
        "use strict";
        var reference = (globalThis.inputs || {}).reference;
        var probe = agentBuilder()
          .name("probe")
          .role("Probe")
          .goal("Say which reference this turn was handed")
          .body(async function () { return "REFERENCE " + JSON.stringify(reference === undefined ? null : reference); })
          .build();
        globalThis.crew = crewBuilder()
          .name("probe")
          .goal("One forge turn")
          .process("autonomous")
          .budget({ toolCalls: 1, wallTime: 60, tokens: 1000, delegationDepth: 1 })
          .withAgent(probe)
          .build();
        """;

    /// <summary>
    /// Under the working directory, as the CLI's own workspace always is: a cycle reads its pack
    /// through the file system service, whose path validator admits nothing outside it.
    /// </summary>
    private readonly string _workspace =
        Path.Combine(Directory.GetCurrentDirectory(), "orkeon-forge-reference-" + Guid.NewGuid().ToString("N"));

    private string Team => Path.Combine(_workspace, "teams", "tri-des-e-mails");

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
            Directory.Delete(_workspace, recursive: true);
    }

    // ── the option ──

    [Fact]
    public void Reference_takes_a_use_case_id_for_a_new_session_only()
    {
        var options = ForgeCommandOptions.Parse(["trier mes e-mails", "--reference", EmailPipeline, "--dry"]);

        Assert.Null(options.Error);
        Assert.Equal(EmailPipeline, options.Reference);
        Assert.Equal("trier mes e-mails", options.Need);
        Assert.True(options.Dry);

        Assert.Contains("--reference needs a use case id", ForgeCommandOptions.Parse(["--reference"]).Error, StringComparison.Ordinal);
        Assert.Contains("--reference needs a use case id", ForgeCommandOptions.Parse(["--reference", "--dry"]).Error, StringComparison.Ordinal);

        // A session keeps the reference it was created with: nowhere else does the option apply.
        Assert.Contains("--reference only applies to a new session",
            ForgeCommandOptions.Parse(["resume", "trier", "--reference", EmailPipeline]).Error, StringComparison.Ordinal);
        Assert.Contains("--reference only applies to a new session",
            ForgeCommandOptions.Parse(["promote", "trier", "--to", "/t", "--reference", EmailPipeline]).Error, StringComparison.Ordinal);
        Assert.Contains("--reference only applies to a new session",
            ForgeCommandOptions.Parse(["reopen", "/t", "--reference", EmailPipeline]).Error, StringComparison.Ordinal);
    }

    // ── D-05: an unknown id, refused before anything ──

    [Fact]
    public async Task An_unknown_reference_is_refused_before_any_session_host_or_model()
    {
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(["trier mes e-mails", "--reference", "99-nowhere"], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("unknown use case '99-nowhere' (USECASES-UNKNOWN-ID)", console.Stderr, StringComparison.Ordinal);
        // No host booted — the refusal a host without a model would have produced is absent —
        // and no session was left behind for a mistyped id.
        Assert.DoesNotContain("FORGE-LLM-UNAVAILABLE", console.Stderr, StringComparison.Ordinal);
        Assert.Empty(ForgeSession.List(_workspace));
    }

    [Fact]
    public async Task An_unknown_reference_is_a_typed_error_on_the_event_stream()
    {
        using var console = new TestConsole();

        var exitCode = await ForgeCommand.DispatchAsync(
            ["trier mes e-mails", "--reference", "99-nowhere", "--events", "jsonl"], _workspace);

        Assert.Equal(1, exitCode);
        var events = Events(console.Stdout);
        Assert.Equal(["error", "session.finished"], events.Select(Kind));
        Assert.Equal(UnknownUseCaseException.Code, events[0].GetProperty("code").GetString());
        Assert.Contains("'99-nowhere'", events[0].GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(events[0].GetProperty("recoverable").GetBoolean());
        Assert.Equal("failed", events[1].GetProperty("status").GetString());
        Assert.Empty(ForgeSession.List(_workspace));
    }

    // ── D-01: recorded, and composed with — resume included ──

    [Fact]
    public async Task The_reference_is_recorded_in_the_new_session()
    {
        using var console = new TestConsole();

        // No model is configured here: the cycle stops at the host's refusal, once the session exists.
        var exitCode = await ForgeCommand.DispatchAsync(["trier mes e-mails", "--reference", EmailPipeline], _workspace);

        Assert.Equal(1, exitCode);
        Assert.Contains("FORGE-LLM-UNAVAILABLE", console.Stderr, StringComparison.Ordinal);
        var summary = Assert.Single(ForgeSession.List(_workspace));
        Assert.True(ForgeSession.TryLoadBySlug(_workspace, summary.Slug, out var session, out _));
        Assert.Equal(Recorded, session!.Document.Reference);
        Assert.Contains(
            "\"reference\": {",
            await File.ReadAllTextAsync(Path.Combine(session.Directory, ForgeSession.SessionFileName), TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The new session announces its reference, and its interview is never handed it: the brief
    /// is the user's need, and the structure only matters once there is one to design.
    /// </summary>
    [Fact]
    public async Task A_new_session_announces_its_reference_and_its_interview_is_not_handed_it()
    {
        var (settings, pack) = WriteProbe();
        using var console = new TestConsole(stdin: "");

        var exitCode = await ForgeCommand.DispatchAsync(
            ["trier mes e-mails", "--reference", EmailPipeline, "--settings", settings, "--pack", pack, "--events", "jsonl"],
            _workspace);

        // The interview asked its question; the closed stdin interrupted it, the session saved.
        Assert.Equal(130, exitCode);
        var events = Events(console.Stdout);
        var started = Assert.Single(events, e => Kind(e) == "session.started");
        Assert.Equal(EmailPipeline, started.GetProperty("reference").GetProperty("id").GetString());
        Assert.Equal("Email triage and replies", started.GetProperty("reference").GetProperty("title").GetString());
        Assert.Equal(
            "REFERENCE null",
            Assert.Single(events, e => Kind(e) == "assistant.message").GetProperty("text").GetString());
    }

    [Fact]
    public async Task A_resumed_session_composes_with_the_reference_it_recorded()
    {
        var (settings, pack) = WriteProbe();
        var session = ForgeSession.Create(_workspace, "trier", now: Now, reference: Recorded);
        Assert.True(ForgeBrief.TryParse(ForgeDocuments.ValidBrief, out var brief, out _));
        session.SaveArtifact(ForgeSession.BriefFileName, brief!);
        session.SetState(ForgeState.Blueprint);
        session.Save(Now);
        using var console = new TestConsole();

        await ForgeCommand.DispatchAsync(
            ["resume", "trier", "--settings", settings, "--pack", pack, "--events", "jsonl"], _workspace);

        var events = Events(console.Stdout);
        Assert.Equal(
            EmailPipeline,
            Assert.Single(events, e => Kind(e) == "session.started").GetProperty("reference").GetProperty("id").GetString());

        var said = events.First(e => Kind(e) == "assistant.message").GetProperty("text").GetString()!;
        Assert.StartsWith("REFERENCE {", said, StringComparison.Ordinal);
        var handed = JsonElement.Parse(said["REFERENCE ".Length..]);
        Assert.Equal(EmailPipeline, handed.GetProperty("id").GetString());
        // Titled in the brief's language, now that there is a brief.
        Assert.Equal("Tri et réponse aux e-mails", handed.GetProperty("title").GetString());
        Assert.Contains("- trieur (Trieur d'Emails): ", handed.GetProperty("outline").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A session whose use case this build no longer carries still resumes: without a model, said
    /// on stderr, its provenance kept as recorded.
    /// </summary>
    [Fact]
    public async Task A_resumed_session_whose_use_case_is_gone_goes_on_without_it()
    {
        var (settings, pack) = WriteProbe();
        var gone = new ForgeReferenceRecord { Id = "99-gone", Title = "Gone" };
        var session = ForgeSession.Create(_workspace, "trier", now: Now, reference: gone);
        Assert.True(ForgeBrief.TryParse(ForgeDocuments.ValidBrief, out var brief, out _));
        session.SaveArtifact(ForgeSession.BriefFileName, brief!);
        session.SetState(ForgeState.Blueprint);
        session.Save(Now);
        using var console = new TestConsole();

        await ForgeCommand.DispatchAsync(
            ["resume", "trier", "--settings", settings, "--pack", pack, "--events", "jsonl"], _workspace);

        Assert.Contains("'99-gone'", console.Stderr, StringComparison.Ordinal);
        var events = Events(console.Stdout);
        Assert.Equal("REFERENCE null", events.First(e => Kind(e) == "assistant.message").GetProperty("text").GetString());
        Assert.True(ForgeSession.TryLoadBySlug(_workspace, "trier", out var reloaded, out _));
        Assert.Equal(gone, reloaded!.Document.Reference);
    }

    // ── D-04: traced by the promotion ──

    [Fact]
    public async Task The_promotion_traces_the_reference_in_the_card_the_record_and_the_session_in_the_briefs_language()
    {
        var session = ReadySession("trier", Recorded);
        using var console = new TestConsole();

        Assert.Equal(0, await ForgeCommand.DispatchAsync(["promote", "trier", "--to", Team, "--events", "jsonl"], _workspace));

        // FORGE.md: next to the provenance sentence it already had, in the brief's language.
        var lines = (await File.ReadAllTextAsync(Path.Combine(Team, ForgePromoter.CardFileName), TestContext.Current.CancellationToken))
            .ReplaceLineEndings("\n").Split('\n');
        var generated = Array.FindIndex(lines, line => line.StartsWith("> Généré par l'Atelier Orkeon le", StringComparison.Ordinal));
        Assert.True(generated > 0);
        Assert.Equal(">", lines[generated + 1]);
        Assert.Equal("> Inspiré de : Tri et réponse aux e-mails (03-email-pipeline).", lines[generated + 2]);

        // forge.json and session.json say the same.
        var retitled = Recorded with { Title = "Tri et réponse aux e-mails" };
        Assert.Equal(retitled, ForgeTeamRecord.TryRead(Team)!.Reference);
        Assert.Equal(retitled, ForgeSession.FindById(_workspace, session.Document.Id!.Value)!.Document.Reference);
    }

    [Fact]
    public void An_english_brief_says_inspired_by_and_no_reference_says_nothing()
    {
        var english = ForgeDocuments.ValidBrief.Replace("\"language\": \"fr\"", "\"language\": \"en\"", StringComparison.Ordinal);
        var referenced = ReadySession("trier", Recorded, english);

        ForgePromoter.Promote(referenced, Team, schedule: null, settingsPath: null, copySettings: false, ForgePromotePlatform.Linux, Now);

        Assert.Contains(
            "> Inspired by: Email triage and replies (03-email-pipeline).",
            File.ReadAllText(Path.Combine(Team, ForgePromoter.CardFileName)),
            StringComparison.Ordinal);

        var plain = ReadySession("veille", reference: null);
        var elsewhere = Path.Combine(_workspace, "teams", "veille");
        ForgePromoter.Promote(plain, elsewhere, schedule: null, settingsPath: null, copySettings: false, ForgePromotePlatform.Linux, Now);

        var card = File.ReadAllText(Path.Combine(elsewhere, ForgePromoter.CardFileName));
        Assert.DoesNotContain("Inspiré de", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Inspired by", card, StringComparison.Ordinal);
        Assert.Null(ForgeTeamRecord.TryRead(elsewhere)!.Reference);
    }

    /// <summary>
    /// A reference hand-edited out of its id costs the reference — no model, no card line — and
    /// never the session or the team record that carry it: both stay readable, brief included.
    /// </summary>
    [Fact]
    public void A_reference_without_an_id_leaves_the_session_and_its_team_record_readable()
    {
        var session = ReadySession("trier", Recorded);
        var file = Path.Combine(session.Directory, ForgeSession.SessionFileName);
        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        document["reference"] = new System.Text.Json.Nodes.JsonObject { ["title"] = "Tri" };
        File.WriteAllText(file, document.ToJsonString());

        Assert.True(ForgeSession.TryLoad(session.Directory, out var reloaded, out var error), error);
        Assert.Equal("", reloaded!.Document.Reference!.Id);
        Assert.Null(ForgeReference.Recorded(reloaded.Document.Reference));

        ForgePromoter.Promote(reloaded, Team, schedule: null, settingsPath: null, copySettings: false, ForgePromotePlatform.Linux, Now);

        Assert.DoesNotContain("Inspiré de", File.ReadAllText(Path.Combine(Team, ForgePromoter.CardFileName)), StringComparison.Ordinal);
        var record = ForgeTeamRecord.TryRead(Team);
        Assert.NotNull(record);
        Assert.NotNull(record.Brief);
    }

    /// <summary>
    /// A reopen keeps it: a session rebuilt from its team folder — the original gone — takes the
    /// reference back from <c>forge.json</c>, and announces it.
    /// </summary>
    [Fact]
    public async Task A_session_rebuilt_from_its_team_keeps_the_reference_the_team_recorded()
    {
        var original = ReadySession("trier", Recorded);
        using (new TestConsole())
            Assert.Equal(0, await ForgeCommand.DispatchAsync(["promote", "trier", "--to", Team], _workspace));
        Directory.Delete(ForgeSession.FindById(_workspace, original.Document.Id!.Value)!.Directory, recursive: true);

        using var console = new TestConsole();
        Assert.Equal(0, await ForgeCommand.DispatchAsync(["reopen", Team, "--events", "jsonl"], _workspace));

        var events = Events(console.Stdout);
        Assert.Equal(EmailPipeline, events[0].GetProperty("reference").GetProperty("id").GetString());
        var rebuilt = ForgeSession.FindById(_workspace, ForgeTeamRecord.ReadSessionId(Team)!.Value);
        Assert.Equal("Tri et réponse aux e-mails", rebuilt!.Document.Reference!.Title);
    }

    /// <summary>A Ready session over a rendered crew — what a promotion takes, and a rebuild reads back.</summary>
    private ForgeSession ReadySession(string slug, ForgeReferenceRecord? reference, string brief = ForgeDocuments.ValidBrief)
    {
        var session = ForgeSession.Create(_workspace, slug, now: Now, reference: reference);
        Assert.True(ForgeBrief.TryParse(brief, out var parsedBrief, out _));
        Assert.True(ForgeBlueprint.TryParse(ForgeDocuments.ValidBlueprint, out var blueprint, out _));
        session.SaveArtifact(ForgeSession.BriefFileName, parsedBrief!);
        session.SaveArtifact(ForgeSession.BlueprintFileName, blueprint!);
        ForgeYamlRenderer.Render(ForgeBlueprintCompiler.Compile(blueprint!), session.Directory);
        session.SetState(ForgeState.Ready);
        session.SetStatus(ForgeSessionStatus.Ready);
        session.Save(Now);
        return session;
    }

    /// <summary>A settings file whose Llm section satisfies the host gate, and the probe pack; nothing is ever called.</summary>
    private (string Settings, string Pack) WriteProbe()
    {
        Directory.CreateDirectory(_workspace);
        var settings = Path.Combine(_workspace, "settings.json");
        File.WriteAllText(settings, """{ "Llm": { "Provider": "ollama", "Model": "never-called", "BaseUrl": "http://127.0.0.1:9" } }""");
        var pack = Directory.CreateDirectory(Path.Combine(_workspace, "probe-pack")).FullName;
        File.WriteAllText(Path.Combine(pack, ForgePack.AssistantFileName), ProbeAssistant);
        return (settings, pack);
    }

    private static string? Kind(JsonElement line) => line.GetProperty("kind").GetString();

    private static List<JsonElement> Events(string stdout) => stdout
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => JsonElement.Parse(line))
        .ToList();
}
