using System.Text.Json;
using Orkeon.Domain.Common;
using Orkeon.Domain.HumanInput;
using Orkeon.Scripting.Cli.Commands.Run;
using Orkeon.Scripting.Cli.Events;
using Orkeon.Scripting.Cli.Tests.Forge;

namespace Orkeon.Scripting.Cli.Tests.Run;

/// <summary>Scripted answers, so a mid-run dialogue can be driven without a process.</summary>
internal sealed class ScriptedAnswerChannel : IAnswerChannel
{
    private readonly Queue<string?> _answers = new();

    /// <summary>Correlation ids the provider waited on, in order.</summary>
    public List<string> Waited { get; } = [];

    /// <summary>Queues one answer.</summary>
    public ScriptedAnswerChannel Answers(string value)
    {
        _answers.Enqueue(value);
        return this;
    }

    /// <inheritdoc />
    public Task<string?> ReadAnswerAsync(string correlationId, CancellationToken cancellationToken)
    {
        Waited.Add(correlationId);
        return Task.FromResult(_answers.Count > 0 ? _answers.Dequeue() : null);
    }
}

/// <summary>
/// The structured human-input provider (BUS-04). The behaviour worth pinning is what happens
/// when nobody answers: `humanInput: true` used to be approved on the user's behalf, and this
/// provider exists to stop that.
/// </summary>
public sealed class HumanInputTests : IDisposable
{
    private readonly StringWriter _output = new();

    public void Dispose() => _output.Dispose();

    private JsonLinesHumanInputProvider Provider(IAnswerChannel answers) =>
        new(new OrkeonEventWriter(_output, new FakeOrkeonClock()), answers);

    private JsonElement LastEvent()
    {
        var lines = _output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return JsonElement.Parse(lines[^1]);
    }

    [Fact]
    public async Task A_question_reaches_the_stream_and_the_answer_comes_back()
    {
        var channel = new ScriptedAnswerChannel().Answers("exemple.fr");
        var context = HumanInputContext.CreateTextInput(
            AgentId.Create(), TaskId.Create(), "Quel fournisseur ?");

        var answer = await Provider(channel).GetInputAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal("exemple.fr", answer);

        var e = LastEvent();
        Assert.Equal("input.needed", e.GetProperty("kind").GetString());

        // The question's own kind is `inputKind`, not `kind`: the envelope owns that name
        // and a payload naming it would be dropped, leaving the client unable to tell a
        // text question from a confirmation.
        Assert.Equal("text", e.GetProperty("inputKind").GetString());
        Assert.Equal("Quel fournisseur ?", e.GetProperty("prompt").GetString());
        // The correlation id travels in the envelope, and it is what the answer must quote.
        var correlationId = e.GetProperty("correlationId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(correlationId));
        Assert.Equal([correlationId], channel.Waited);
    }

    [Fact]
    public async Task Silence_is_not_consent()
    {
        // The whole reason this provider exists: AutoApproveHumanInputProvider returns true
        // without asking anyone. When the channel closes with no answer, we refuse.
        var context = HumanInputContext.CreateConfirmationInput(
            AgentId.Create(), TaskId.Create(), "Publier le rapport ?");

        var approved = await Provider(new ScriptedAnswerChannel())
            .GetConfirmationAsync(context, TestContext.Current.CancellationToken);

        Assert.False(approved);
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData("y", true)]
    [InlineData("oui", true)]
    [InlineData("true", true)]
    [InlineData("no", false)]
    [InlineData("n'importe quoi", false)]
    public async Task A_confirmation_reads_the_answer_it_was_given(string answer, bool expected)
    {
        var approved = await Provider(new ScriptedAnswerChannel().Answers(answer))
            .GetConfirmationAsync(
                HumanInputContext.CreateConfirmationInput(AgentId.Create(), TaskId.Create(), "Publier ?"),
                TestContext.Current.CancellationToken);

        Assert.Equal(expected, approved);
    }

    [Fact]
    public async Task A_choice_offers_its_options_and_refuses_an_answer_outside_them()
    {
        var context = HumanInputContext.CreateChoiceInput(
            AgentId.Create(), TaskId.Create(), "Quel format ?", ["markdown", "pdf"]);

        var chosen = await Provider(new ScriptedAnswerChannel().Answers("pdf"))
            .GetChoiceAsync(context, TestContext.Current.CancellationToken);
        Assert.Equal("pdf", chosen);

        var choices = LastEvent().GetProperty("choices").EnumerateArray().Select(c => c.GetString()).ToList();
        Assert.Equal(["markdown", "pdf"], choices);

        // An answer outside the offered set falls back to the first option rather than
        // passing a value the caller never advertised.
        var stray = await Provider(new ScriptedAnswerChannel().Answers("docx"))
            .GetChoiceAsync(context, TestContext.Current.CancellationToken);
        Assert.Equal("markdown", stray);
    }

    [Theory]
    [InlineData("""{"kind":"input.given","correlationId":"c-1","value":"ok"}""", "c-1", "ok")]
    [InlineData("""{"kind":"input.given","value":"ok"}""", null, "ok")]   // no id: answers what is pending
    [InlineData("""{"kind":"input.given","correlationId":"other","value":"ok"}""", "other", "ok")]
    [InlineData("""{"kind":"input.given","correlationId":"c-1"}""", "c-1", null)]
    [InlineData("not json at all", null, null)]
    public void The_inbound_answer_is_read_tolerantly(string line, string? expectedId, string? expected)
    {
        var read = InboundCommandPump.TryReadAnswer(line, out var correlationId, out var value);

        Assert.Equal(expected is not null, read);
        Assert.Equal(expected, value);
        if (expected is not null)
            Assert.Equal(expectedId, correlationId);
    }

    [Theory]
    [InlineData("""{"kind":"input.given","value":"ok"}""", "input.given")]
    [InlineData("""{"kind":"post","to":"agent://c/a"}""", "post")]
    [InlineData("""{"text":"no kind here"}""", null)]
    [InlineData("""["not an object"]""", null)]
    [InlineData("not json at all", null)]
    [InlineData("", null)]
    public void The_pump_routes_by_kind_and_ignores_what_it_cannot_read(string line, string? expected)
    {
        Assert.Equal(expected, InboundCommandPump.TryReadKind(line));
    }
}
