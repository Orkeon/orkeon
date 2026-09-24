using Orkeon.Scripting.Cli.Commands.Forge;
using Orkeon.Scripting.Cli.Events;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>A clock the test scripts: each read advances by one second, so lines differ visibly.</summary>
internal sealed class FakeOrkeonClock : IOrkeonClock
{
    private DateTimeOffset _now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Reads the current instant and advances by one second.</summary>
    public DateTimeOffset UtcNow
    {
        get
        {
            var now = _now;
            _now = _now.AddSeconds(1);
            return now;
        }
    }
}

/// <summary>
/// The event protocol (SPEC-ORKEON-FORGE §6) is a versioned wire contract, like
/// <c>orkeon doctor --json</c>: this golden test pins the exact serialized lines. A failure
/// here means the protocol changed — bump <c>v</c> and the clients, or fix the regression.
/// </summary>
public class ForgeEventWriterTests
{
    [Fact]
    public void The_envelope_is_the_pinned_golden_form()
    {
        var output = new StringWriter();
        var writer = new ForgeEventWriter(output, new FakeOrkeonClock());

        writer.Emit("stage.entered", new { stage = "brief", iteration = 1 });
        writer.Emit("question.asked", new { id = "q1", text = "Quel est l'objectif ?", answerKind = "free" });
        writer.Error("FORGE-BUDGET-EXHAUSTED", "The session's token budget is exhausted.", recoverable: true);
        writer.SessionFinished("abandoned", 0);

        var expected = string.Join('\n',
            """{"v":2,"seq":1,"ts":"2026-08-19T12:00:00Z","kind":"stage.entered","stage":"brief","iteration":1}""",
            """{"v":2,"seq":2,"ts":"2026-08-19T12:00:01Z","kind":"question.asked","id":"q1","text":"Quel est l'objectif ?","answerKind":"free"}""",
            """{"v":2,"seq":3,"ts":"2026-08-19T12:00:02Z","kind":"error","code":"FORGE-BUDGET-EXHAUSTED","message":"The session's token budget is exhausted.","recoverable":true}""",
            """{"v":2,"seq":4,"ts":"2026-08-19T12:00:03Z","kind":"session.finished","status":"abandoned","exitCode":0}""",
            "");

        Assert.Equal(expected.ReplaceLineEndings("\n"), output.ToString().ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// STUDIO-26's two lines, pinned like the rest: <c>session.renamed</c> when the session folder
    /// followed its team, <c>warning</c> when the disk refused — the promotion stands either way.
    /// Studio.Core's <c>ForgeProtocolTests</c> carries the same lines, verbatim.
    /// </summary>
    [Fact]
    public void The_rename_and_warning_lines_are_the_pinned_golden_form()
    {
        var output = new StringWriter();
        var writer = new ForgeEventWriter(output, new FakeOrkeonClock());

        writer.SessionRenamed("veille", "ma-veille-2", "/ws/.orkeon/forge/ma-veille-2", suffixed: true);
        writer.Warning(ForgeErrorCodes.SessionNotRenamed, "The session folder 'veille' keeps its name: access denied.");

        var expected = string.Join('\n',
            """{"v":2,"seq":1,"ts":"2026-08-19T12:00:00Z","kind":"session.renamed","from":"veille","to":"ma-veille-2","dir":"/ws/.orkeon/forge/ma-veille-2","suffixed":true}""",
            """{"v":2,"seq":2,"ts":"2026-08-19T12:00:01Z","kind":"warning","code":"FORGE-SESSION-NOT-RENAMED","message":"The session folder 'veille' keeps its name: access denied."}""",
            "");

        Assert.Equal(expected.ReplaceLineEndings("\n"), output.ToString().ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// Without <c>--events</c>, both lines are said in words: a raw JSON line for an event every
    /// adoption can carry would read as a bug to whoever runs <c>forge promote</c> by hand.
    /// </summary>
    [Fact]
    public void The_terminal_says_the_rename_and_the_warning_in_words()
    {
        var console = new StringWriter();
        using var renderer = new ForgeTerminalRenderer(console);
        var writer = new ForgeEventWriter(renderer, new FakeOrkeonClock());

        writer.SessionRenamed("veille", "ma-veille-2", "/ws/.orkeon/forge/ma-veille-2", suffixed: true);
        writer.Warning(ForgeErrorCodes.SessionNotRenamed, "The session folder 'veille' keeps its name.");

        var lines = console.ToString().ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            ["  session renamed after the team: veille → ma-veille-2 (another session already had that name)",
             "⚠ [FORGE-SESSION-NOT-RENAMED] The session folder 'veille' keeps its name."],
            lines);
    }

    /// <summary>
    /// STUDIO-27's lines, pinned like the rest: <c>schedule.state</c> for each of the three states,
    /// and the <c>error</c> of a refusal carrying the command a person can run instead. Studio.Core's
    /// <c>ForgeProtocolTests</c> carries the same lines, verbatim.
    /// </summary>
    [Fact]
    public void The_schedule_lines_are_the_pinned_golden_form()
    {
        var output = new StringWriter();
        var writer = new ForgeEventWriter(output, new FakeOrkeonClock());

        writer.ScheduleState(new ForgeScheduleReport
        {
            TeamDirectory = "/home/u/Orkeon/teams/ma-veille",
            State = ForgeScheduleState.Installed,
            Expression = "daily@08:00",
            Family = "windows",
            Names = ["Orkeon ma-veille"],
        });
        writer.ScheduleState(new ForgeScheduleReport
        {
            TeamDirectory = "/home/u/Orkeon/teams/ma-veille",
            State = ForgeScheduleState.Stale,
            Reason = ForgeScheduleReasons.Moved,
            Expression = "daily@08:00",
            Family = "linux",
            Names = ["orkeon-ma-veille.timer", "orkeon-ma-veille.service"],
        });
        writer.ScheduleState(new ForgeScheduleReport
        {
            TeamDirectory = "/home/u/Orkeon/teams/ma-veille",
            State = ForgeScheduleState.Absent,
            Reason = ForgeScheduleReasons.NotInstalled,
            Family = "other",
            Names = ["orkeon:ma-veille"],
            Removed = true,
        });
        writer.Error(ForgeErrorCodes.ScheduleRefused, "The other scheduler refused: crontab: permission denied", recoverable: true,
            "( crontab -l 2>/dev/null; cat \"/home/u/Orkeon/teams/ma-veille/schedule/cron.txt\" ) | crontab -");

        var expected = string.Join('\n',
            """{"v":2,"seq":1,"ts":"2026-08-19T12:00:00Z","kind":"schedule.state","path":"/home/u/Orkeon/teams/ma-veille","state":"installed","expression":"daily@08:00","family":"windows","names":["Orkeon ma-veille"]}""",
            """{"v":2,"seq":2,"ts":"2026-08-19T12:00:01Z","kind":"schedule.state","path":"/home/u/Orkeon/teams/ma-veille","state":"stale","reason":"moved","expression":"daily@08:00","family":"linux","names":["orkeon-ma-veille.timer","orkeon-ma-veille.service"]}""",
            """{"v":2,"seq":3,"ts":"2026-08-19T12:00:02Z","kind":"schedule.state","path":"/home/u/Orkeon/teams/ma-veille","state":"absent","reason":"not-installed","family":"other","names":["orkeon:ma-veille"],"removed":true}""",
            """{"v":2,"seq":4,"ts":"2026-08-19T12:00:03Z","kind":"error","code":"FORGE-SCHEDULE-REFUSED","message":"The other scheduler refused: crontab: permission denied","recoverable":true,"command":"( crontab -l 2>/dev/null; cat \"/home/u/Orkeon/teams/ma-veille/schedule/cron.txt\" ) | crontab -"}""",
            "");

        Assert.Equal(expected.ReplaceLineEndings("\n"), output.ToString().ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// STUDIO-28's lines, pinned like the rest: the session's folder following its team, the team
    /// renamed, and a refusal — after which nothing changed. Studio.Core's
    /// <c>ForgeRenameClientTests</c> carries the same lines, verbatim.
    /// </summary>
    [Fact]
    public void The_rename_lines_are_the_pinned_golden_form()
    {
        var output = new StringWriter();
        var writer = new ForgeEventWriter(output, new FakeOrkeonClock());

        writer.SessionRenamed("ma-veille", "veille-du-matin", "/home/u/.config/Orkeon/.orkeon/forge/veille-du-matin", suffixed: false);
        writer.TeamRenamed("/home/u/Orkeon/teams/ma-veille", "/home/u/Orkeon/teams/veille-du-matin", "Veille du matin");
        writer.Error(ForgeErrorCodes.RenameTaken,
            "The name's folder '/home/u/Orkeon/teams/veille-du-matin' is taken: another team is already there. Choose another name.",
            recoverable: true);

        var expected = string.Join('\n',
            """{"v":2,"seq":1,"ts":"2026-08-19T12:00:00Z","kind":"session.renamed","from":"ma-veille","to":"veille-du-matin","dir":"/home/u/.config/Orkeon/.orkeon/forge/veille-du-matin","suffixed":false}""",
            """{"v":2,"seq":2,"ts":"2026-08-19T12:00:01Z","kind":"team.renamed","from":"/home/u/Orkeon/teams/ma-veille","path":"/home/u/Orkeon/teams/veille-du-matin","name":"Veille du matin"}""",
            """{"v":2,"seq":3,"ts":"2026-08-19T12:00:02Z","kind":"error","code":"FORGE-RENAME-TAKEN","message":"The name's folder '/home/u/Orkeon/teams/veille-du-matin' is taken: another team is already there. Choose another name.","recoverable":true}""",
            "");

        Assert.Equal(expected.ReplaceLineEndings("\n"), output.ToString().ReplaceLineEndings("\n"));
    }

    /// <summary>Without <c>--events</c>, a folder's schedule and a refusal are said in words — the refusal with what to run by hand.</summary>
    [Fact]
    public void The_terminal_says_the_schedule_and_the_manual_command_in_words()
    {
        var console = new StringWriter();
        using var renderer = new ForgeTerminalRenderer(console);
        var writer = new ForgeEventWriter(renderer, new FakeOrkeonClock());

        writer.ScheduleState(new ForgeScheduleReport
        {
            TeamDirectory = "/t/ma-veille", State = ForgeScheduleState.Installed, Expression = "hourly",
            Family = "windows", Names = ["Orkeon ma-veille"],
        });
        writer.ScheduleState(new ForgeScheduleReport
        {
            TeamDirectory = "/t/ma-veille", State = ForgeScheduleState.Stale, Reason = ForgeScheduleReasons.Renamed,
            Family = "windows", Names = ["Orkeon veille"],
        });
        writer.ScheduleState(new ForgeScheduleReport
        {
            TeamDirectory = "/t/ma-veille", State = ForgeScheduleState.Absent, Reason = ForgeScheduleReasons.NotInstalled,
            Family = "windows", Names = ["Orkeon ma-veille"], Removed = true,
        });
        writer.Error(ForgeErrorCodes.ScheduleRefused, "The windows scheduler refused: ERROR: Access is denied.", recoverable: true,
            "schtasks /Delete /TN \"Orkeon ma-veille\" /F");

        var lines = console.ToString().ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            ["✔ scheduled (hourly): Orkeon ma-veille",
             "⚠ schedule to reinstall (renamed): Orkeon veille — run `orkeon forge schedule \"/t/ma-veille\"`",
             "✔ schedule removed: Orkeon ma-veille",
             "✖ [FORGE-SCHEDULE-REFUSED] The windows scheduler refused: ERROR: Access is denied.",
             "  by hand: schtasks /Delete /TN \"Orkeon ma-veille\" /F"],
            lines);
    }

    [Fact]
    public void The_sequence_is_strictly_increasing_from_one()
    {
        var output = new StringWriter();
        var writer = new ForgeEventWriter(output, new FakeOrkeonClock());

        for (var i = 0; i < 3; i++)
            writer.Emit("stage.entered", new { stage = "brief", iteration = 1 });

        var sequences = output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => System.Text.Json.JsonElement.Parse(line).GetProperty("seq").GetInt32());

        Assert.Equal([1, 2, 3], sequences);
    }

    [Fact]
    public void A_payload_never_overrides_the_envelope_fields()
    {
        var output = new StringWriter();
        var writer = new ForgeEventWriter(output, new FakeOrkeonClock());

        // A hostile or buggy payload naming an envelope field must not corrupt the contract.
        // The identity names are reserved too, and the forge emits with an empty scope — so
        // a payload cannot smuggle a crewId in through the back door either.
        writer.Emit("stage.entered", new
        {
            v = 99,
            seq = 99,
            kind = "spoofed",
            crewId = "forged",
            causationId = "forged",
            stage = "brief",
        });

        var root = System.Text.Json.JsonElement.Parse(output.ToString());

        Assert.Equal(OrkeonEventWriter.ProtocolVersion, root.GetProperty("v").GetInt32());
        Assert.Equal(1, root.GetProperty("seq").GetInt32());
        Assert.Equal("stage.entered", root.GetProperty("kind").GetString());
        Assert.Equal("brief", root.GetProperty("stage").GetString());

        // Absent identity stays absent: the key is omitted, never written as null.
        Assert.False(root.TryGetProperty("crewId", out _));
        Assert.False(root.TryGetProperty("causationId", out _));
    }

    [Fact]
    public void A_scoped_event_carries_its_identity_in_the_envelope()
    {
        var output = new StringWriter();
        var writer = new OrkeonEventWriter(output, new FakeOrkeonClock());

        writer.Emit(
            "task.completed",
            new OrkeonEventScope { CrewId = "c-7f3a", AgentId = "a-91b", CorrelationId = "r-3311" },
            new { taskId = "collect", success = true });

        Assert.Equal(
            """{"v":2,"seq":1,"ts":"2026-08-19T12:00:00Z","kind":"task.completed","crewId":"c-7f3a","agentId":"a-91b","correlationId":"r-3311","taskId":"collect","success":true}""",
            output.ToString().TrimEnd('\r', '\n'));
    }

    [Fact]
    public void Stage_names_are_spelled_lowercase_on_the_wire()
    {
        Assert.Equal("brief", ForgeEventWriter.Spell(ForgeState.Brief));
        Assert.Equal("verdict", ForgeEventWriter.Spell(ForgeState.Verdict));
    }

    [Fact]
    public void A_null_payload_field_is_omitted_never_written()
    {
        // "An absent key is omitted, never written as null" is the contract's own sentence,
        // and the writer is the only place that can hold every emitter to it. The first
        // version enforced it for envelope fields alone; payloads with nulls (a topic-less
        // hub.message, a question without choices) leaked `null` onto the wire, and the E2E
        // invariant only ever ran on a stream that could not fault.
        var output = new StringWriter();
        var writer = new OrkeonEventWriter(output, new FakeOrkeonClock());

        writer.Emit("x", new { present = "yes", missing = (string?)null });

        var root = System.Text.Json.JsonElement.Parse(output.ToString());
        Assert.Equal("yes", root.GetProperty("present").GetString());
        Assert.False(root.TryGetProperty("missing", out _));
    }
}
