using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.Run;

namespace Orkeon.Studio.Core.Tests.Run;

/// <summary>
/// The fold from a watched run's event stream to what a screen shows (BUS-06). Everything the
/// model exposes has to come from an event — a screen must never show progress the run did not
/// report.
/// </summary>
public class RunProgressModelTests
{
    private static RunProgressModel Fold(params string[] lines)
    {
        var model = new RunProgressModel();
        foreach (var line in lines)
        {
            Assert.True(OrkeonEventParser.TryParse(line, out var orkeonEvent), line);
            model.Apply(orkeonEvent!);
        }

        return model;
    }

    [Fact]
    public void A_run_that_said_nothing_shows_nothing()
    {
        // "No news" is not "going well", and the screen has to be able to tell them apart.
        var model = new RunProgressModel();

        Assert.Empty(model.Tasks);
        Assert.Null(model.Cost);
        Assert.Null(model.PendingQuestion);
        Assert.False(model.Finished);
        Assert.Null(model.Success);
    }

    [Fact]
    public void The_stream_becomes_progress_cost_and_an_outcome()
    {
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"run.started","target":"/ws/crew.yaml","stream":true}""",
            """{"v":2,"seq":2,"ts":"t","kind":"task.completed","taskId":"t1","agentRole":"analyst","success":true,"durationMs":1200,"tokens":340,"toolCalls":2}""",
            """{"v":2,"seq":3,"ts":"t","kind":"cost.updated","tokens":340,"usd":0.004,"budgetRemaining":9660}""",
            """{"v":2,"seq":4,"ts":"t","kind":"run.finished","success":true,"exitCode":0}""");

        Assert.Equal("/ws/crew.yaml", model.Target);
        Assert.True(model.Streaming);

        var task = Assert.Single(model.Tasks);
        Assert.Equal("analyst", task.AgentRole);
        Assert.True(task.Success);
        Assert.Equal(1200, task.DurationMs);
        Assert.Equal(340, task.Tokens);
        Assert.Equal(2, task.ToolCalls);

        Assert.Equal(340, model.Cost!.Tokens);
        Assert.Equal(0.004, model.Cost.Usd);
        Assert.Equal(9660, model.Cost.BudgetRemaining);

        Assert.True(model.Finished);
        Assert.True(model.Success);
        Assert.Equal(0, model.ExitCode);
    }

    [Fact]
    public void A_question_waits_until_it_is_answered()
    {
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","correlationId":"c-1","kind":"input.needed","inputKind":"choice","prompt":"Which format?","choices":["markdown","json"]}""");

        var question = model.PendingQuestion!;
        Assert.Equal("c-1", question.CorrelationId);
        Assert.Equal(RunQuestion.Choice, question.InputKind);
        Assert.Equal("Which format?", question.Prompt);
        Assert.Equal(["markdown", "json"], question.Choices);

        model.Apply(Parse("""{"v":2,"seq":2,"ts":"t","correlationId":"c-1","kind":"input.given","value":"markdown"}"""));
        Assert.Null(model.PendingQuestion);
    }

    [Fact]
    public void A_question_nobody_can_answer_is_not_shown_as_pending()
    {
        // Answering needs an address. Showing a prompt with no way to reply would strand the
        // user in front of a box that does nothing.
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"input.needed","inputKind":"text","prompt":"Your name?"}""");

        Assert.Null(model.PendingQuestion);
    }

    [Fact]
    public void The_end_of_a_run_clears_a_question_nobody_is_left_to_answer()
    {
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","correlationId":"c-1","kind":"input.needed","inputKind":"confirm","prompt":"Continue?"}""",
            """{"v":2,"seq":2,"ts":"t","kind":"run.finished","success":false,"exitCode":1}""");

        Assert.Null(model.PendingQuestion);
        Assert.False(model.Success);
    }

    [Fact]
    public void Hub_messages_keep_their_payload_whatever_shape_it_has()
    {
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"hub.message","from":"client://studio","topic":"orders","payload":{"id":42}}""");

        var message = Assert.Single(model.HubMessages);
        Assert.Equal("client://studio", message.From);
        Assert.Equal("orders", message.Topic);
        Assert.Contains("\"id\":42", message.Payload!, StringComparison.Ordinal);
    }

    [Fact]
    public void Deltas_accumulate_and_errors_are_kept()
    {
        var model = Fold(
            """{"v":2,"seq":1,"ts":"t","kind":"llm.delta","text":"Hel"}""",
            """{"v":2,"seq":2,"ts":"t","kind":"llm.delta","text":"lo"}""",
            """{"v":2,"seq":3,"ts":"t","kind":"error","code":"llm.unreachable","message":"no endpoint","recoverable":false}""");

        Assert.Equal("Hello", model.GeneratedText);
        Assert.Equal("llm.unreachable", model.LastError!.Code);
        Assert.False(model.LastError.Recoverable);
    }

    [Fact]
    public void An_unknown_kind_changes_nothing_and_announces_nothing()
    {
        // A newer CLI may say more than this build understands. Showing a little less beats
        // crashing on a line nobody has taught the screen to read.
        var model = new RunProgressModel();
        var announcements = 0;
        model.Changed += (_, _) => announcements++;

        model.Apply(Parse("""{"v":2,"seq":1,"ts":"t","kind":"something.new","detail":"x"}"""));
        Assert.Equal(0, announcements);

        model.Apply(Parse("""{"v":2,"seq":2,"ts":"t","kind":"cost.updated","tokens":10}"""));
        Assert.Equal(1, announcements);
    }

    private static OrkeonEvent Parse(string line)
    {
        Assert.True(OrkeonEventParser.TryParse(line, out var orkeonEvent), line);
        return orkeonEvent!;
    }
}
