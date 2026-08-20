using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>A clock the test scripts: each read advances by one second, so lines differ visibly.</summary>
internal sealed class FakeForgeClock : IForgeClock
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
        var writer = new ForgeEventWriter(output, new FakeForgeClock());

        writer.Emit("stage.entered", new { stage = "brief", iteration = 1 });
        writer.Emit("question.asked", new { id = "q1", text = "Quel est l'objectif ?", answerKind = "free" });
        writer.Error("FORGE-BUDGET-EXHAUSTED", "The session's token budget is exhausted.", recoverable: true);
        writer.SessionFinished("abandoned", 0);

        var expected = string.Join('\n',
            """{"v":1,"seq":1,"ts":"2026-08-19T12:00:00Z","kind":"stage.entered","stage":"brief","iteration":1}""",
            """{"v":1,"seq":2,"ts":"2026-08-19T12:00:01Z","kind":"question.asked","id":"q1","text":"Quel est l'objectif ?","answerKind":"free"}""",
            """{"v":1,"seq":3,"ts":"2026-08-19T12:00:02Z","kind":"error","code":"FORGE-BUDGET-EXHAUSTED","message":"The session's token budget is exhausted.","recoverable":true}""",
            """{"v":1,"seq":4,"ts":"2026-08-19T12:00:03Z","kind":"session.finished","status":"abandoned","exitCode":0}""",
            "");

        Assert.Equal(expected.ReplaceLineEndings("\n"), output.ToString().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void The_sequence_is_strictly_increasing_from_one()
    {
        var output = new StringWriter();
        var writer = new ForgeEventWriter(output, new FakeForgeClock());

        for (var i = 0; i < 3; i++)
            writer.Emit("stage.entered", new { stage = "brief", iteration = 1 });

        var sequences = output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => System.Text.Json.JsonDocument.Parse(line).RootElement.GetProperty("seq").GetInt32());

        Assert.Equal([1, 2, 3], sequences);
    }

    [Fact]
    public void A_payload_never_overrides_the_envelope_fields()
    {
        var output = new StringWriter();
        var writer = new ForgeEventWriter(output, new FakeForgeClock());

        // A hostile or buggy payload naming an envelope field must not corrupt the contract…
        writer.Emit("stage.entered", new { v = 99, seq = 99, kind = "spoofed", stage = "brief" });

        var root = System.Text.Json.JsonDocument.Parse(output.ToString()).RootElement;

        // …the payload's spelling wins on collisions is NOT acceptable for v/seq/kind:
        Assert.Equal(1, root.GetProperty("v").GetInt32());
        Assert.Equal(1, root.GetProperty("seq").GetInt32());
        Assert.Equal("stage.entered", root.GetProperty("kind").GetString());
        Assert.Equal("brief", root.GetProperty("stage").GetString());
    }

    [Fact]
    public void Stage_names_are_spelled_lowercase_on_the_wire()
    {
        Assert.Equal("brief", ForgeEventWriter.Spell(ForgeState.Brief));
        Assert.Equal("verdict", ForgeEventWriter.Spell(ForgeState.Verdict));
    }
}
