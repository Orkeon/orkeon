using Orkeon.Studio.Core.Run;
using Orkeon.Studio.Wpf.ViewModels.Launch;

namespace Orkeon.Studio.Wpf.Tests.Launch;

/// <summary>
/// The progress panel of a watched run (BUS-06). The folding itself is Core's; what is tested
/// here is the projection a screen binds to — and the two refusals that keep it honest.
/// </summary>
public class RunProgressViewModelTests
{
    private static RunProgressViewModel Watching(Func<string, string, bool>? answer = null)
    {
        var panel = new RunProgressViewModel();
        panel.Reset(answer ?? ((_, _) => true));
        return panel;
    }

    [Fact]
    public void A_line_the_panel_cannot_read_is_handed_back_to_the_caller()
    {
        // The caller shows it raw. A line nobody can read must never be lost.
        var panel = Watching();

        Assert.False(panel.TryApply("this is not a protocol line"));
        Assert.False(panel.TryApply(""));
        Assert.True(panel.TryApply("""{"v":2,"seq":1,"ts":"t","kind":"cost.updated","tokens":10}"""));
    }

    [Fact]
    public void Tasks_and_cost_reach_the_bindable_surface()
    {
        var panel = Watching();

        panel.TryApply("""{"v":2,"seq":1,"ts":"t","kind":"task.completed","taskId":"t1","agentRole":"analyst","success":true,"durationMs":1500,"tokens":340}""");
        panel.TryApply("""{"v":2,"seq":2,"ts":"t","kind":"cost.updated","tokens":340,"usd":0.004}""");

        var task = Assert.Single(panel.Tasks);
        Assert.Equal("analyst", task.Title);
        Assert.True(task.Success);
        Assert.Contains("1", task.Duration, StringComparison.Ordinal);
        Assert.NotEmpty(task.Tokens);
        Assert.Contains("340", panel.CostSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_silent_run_says_so_instead_of_implying_progress()
    {
        // "No news" is not "going well", and the summary has to keep them apart.
        var panel = Watching();

        Assert.Equal("Nothing reported yet.", panel.Summary);
        Assert.Empty(panel.CostSummary);
        Assert.False(panel.IsAsking);
    }

    [Fact]
    public void A_question_is_answered_on_screen_and_then_stops_being_asked()
    {
        var sent = new List<(string Id, string Value)>();
        var panel = Watching((id, value) => { sent.Add((id, value)); return true; });

        panel.TryApply("""{"v":2,"seq":1,"ts":"t","correlationId":"c-1","kind":"input.needed","inputKind":"text","prompt":"Your name?"}""");

        Assert.True(panel.IsAsking);
        Assert.Equal("Your name?", panel.QuestionPrompt);

        panel.AnswerText = "Ada";
        panel.AnswerCommand.Execute(null);

        Assert.Equal(("c-1", "Ada"), Assert.Single(sent));
        Assert.False(panel.IsAsking);
        Assert.Empty(panel.AnswerText);
    }

    [Fact]
    public void An_answer_that_could_not_be_sent_leaves_the_question_open()
    {
        // Clearing it anyway would pretend the answer landed. The run is still waiting.
        var panel = Watching((_, _) => false);

        panel.TryApply("""{"v":2,"seq":1,"ts":"t","correlationId":"c-1","kind":"input.needed","inputKind":"confirm","prompt":"Continue?"}""");
        panel.AnswerCommand.Execute(null);

        Assert.True(panel.IsAsking);
    }

    [Fact]
    public void A_choice_sends_the_option_the_user_clicked()
    {
        var sent = new List<(string Id, string Value)>();
        var panel = Watching((id, value) => { sent.Add((id, value)); return true; });

        panel.TryApply("""{"v":2,"seq":1,"ts":"t","correlationId":"c-1","kind":"input.needed","inputKind":"choice","prompt":"Format?","choices":["markdown","json"]}""");

        Assert.True(panel.IsChoice);
        Assert.Equal(["markdown", "json"], panel.QuestionChoices);

        panel.ChooseCommand.Execute("json");

        Assert.Equal(("c-1", "json"), Assert.Single(sent));
        Assert.False(panel.IsAsking);
    }

    [Fact]
    public void A_confirmation_left_blank_reads_as_yes_because_the_user_pressed_the_button()
    {
        // Pressing "Answer" on a yes/no question with nothing typed is an act, not silence —
        // unlike a closed channel, which BUS-04 refuses.
        var sent = new List<(string Id, string Value)>();
        var panel = Watching((id, value) => { sent.Add((id, value)); return true; });

        panel.TryApply("""{"v":2,"seq":1,"ts":"t","correlationId":"c-1","kind":"input.needed","inputKind":"confirm","prompt":"Continue?"}""");
        panel.AnswerCommand.Execute(null);

        Assert.Equal("yes", Assert.Single(sent).Value);
    }

    [Fact]
    public void Resetting_forgets_the_previous_run()
    {
        var panel = Watching();
        panel.TryApply("""{"v":2,"seq":1,"ts":"t","kind":"task.completed","taskId":"t1","success":true,"durationMs":10}""");
        Assert.Single(panel.Tasks);

        panel.Reset((_, _) => true);

        Assert.Empty(panel.Tasks);
        Assert.Empty(panel.CostSummary);
        Assert.False(panel.IsAsking);
    }

    [Fact]
    public void A_failed_run_says_it_failed()
    {
        var panel = Watching();

        panel.TryApply("""{"v":2,"seq":1,"ts":"t","kind":"error","code":"llm.unreachable","message":"no endpoint","recoverable":false}""");
        panel.TryApply("""{"v":2,"seq":2,"ts":"t","kind":"run.finished","success":false,"exitCode":1}""");

        Assert.True(panel.HasError);
        Assert.Contains("llm.unreachable", panel.LastError, StringComparison.Ordinal);
        Assert.Equal("Finished with a failure.", panel.Summary);
    }
}

/// <summary>
/// The argv the launch form composes once the screen watches a run rather than tailing it.
/// </summary>
public class LaunchOptionsObservationTests
{
    [Fact]
    public void Watching_is_the_default_and_streaming_is_not()
    {
        var options = new LaunchOptionsViewModel();

        Assert.True(options.WatchProgress);
        Assert.False(options.StreamGeneratedText);
        Assert.True(options.CanStreamGeneratedText);
    }

    [Fact]
    public void Turning_watching_off_takes_streaming_with_it()
    {
        var options = new LaunchOptionsViewModel { WatchProgress = false, StreamGeneratedText = true };

        Assert.False(options.CanStreamGeneratedText);

        var built = options.ToOptions();
        Assert.False(built.Events);
        Assert.False(built.Stream);
        Assert.Null(built.ClientName);
    }

    [Fact]
    public void A_watched_run_names_studio_on_the_hub()
    {
        var built = new LaunchOptionsViewModel { StreamGeneratedText = true }.ToOptions();

        Assert.True(built.Events);
        Assert.True(built.Stream);
        Assert.Equal(LaunchOptionsViewModel.StudioClientName, built.ClientName);
    }

    [Fact]
    public void A_dry_run_asks_for_no_stream_at_all()
    {
        // Validation reports a verdict, not progress: a stream with nothing in it would only
        // make the screen look like it is watching something.
        var built = new LaunchOptionsViewModel().ToOptions(validate: true);

        Assert.False(built.Events);
        Assert.False(built.Stream);
        Assert.Null(built.ClientName);
    }
}
